import { ChangeDetectorRef, Component, ElementRef, HostListener, NgZone, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { CategoriaDeuda, PagosService, PendienteJugador, ResumenPagos } from '../../services/pagos';
import { formatCompactCurrency } from '../../shared/format-currency';
import { CustomSelect } from '../../shared/custom-select/custom-select';
import { ReportesService } from '../../services/reportes';
import { NotificationService } from '../../shared/notifications/notification.service';
import { triggerBlobDownload } from '../../shared/download-file';

// One row in the debtors ranking table
interface DebtorRow {
    rank: number;
    player: string;
    dni: string;
    category: string;
    instalments: string;
    debt: string;
    status: 'suspended' | 'partial';
    statusLabel: string;
}

// One row in the "Inhabilitados" panel
interface DebtorHighlight {
    initials: string;
    player: string;
    detail: string;
    amount: string;
}

// One row in "Deuda por categoría": a la vista de negocio le importa dónde
// está concentrada la deuda, no solo quién debe — esto responde "¿en qué
// categoría/división hay que enfocar la cobranza?" de un vistazo. Puede ser
// del mes en curso o del año completo, según periodoModo.
interface CategoriaDeudaRow {
    categoria: string;
    total: string;
    jugadores: number;
    width: number;
}

type PeriodoModo = 'mes' | 'anio';

const NOMBRES_MES = [
    'Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio',
    'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre',
];

interface BannerMetric {
    value: string;
    label: string;
}

// Un valor crudo + cómo formatearlo, para poder animar el conteo y recién
// ahí convertirlo a texto en cada cuadro (compacto para plata, entero para
// cantidades de jugadores).
interface BannerMetricTarget {
    label: string;
    raw: number;
    format: (n: number) => string;
}

const CURRENCY_FULL = new Intl.NumberFormat('es-AR', {
    style: 'currency',
    currency: 'ARS',
    maximumFractionDigits: 0,
});

// Regla de negocio del club (la valida y aplica el acceso a la cancha otro equipo; acá solo
// se calcula y se muestra como referencia):
//   1 cuota impaga   -> puede entrenar, NO puede jugar partidos oficiales.
//   2+ cuotas impagas -> inhabilitado por completo (ni entrenamientos ni oficiales).
// No hay un campo propio en la base para esto ni un registro de CUÁNDO cruzó el umbral, así
// que se deriva acá de la cantidad de cuotas pendientes que ya trae /api/pagos/pendientes
// (estado actual, no histórico del mes).
const CUOTAS_PARA_INHABILITAR = 2;

@Component({
    selector: 'app-deudas-morosidad',
    standalone: true,
    imports: [CommonModule, FormsModule, CustomSelect],
    templateUrl: './deudas-morosidad.html',
    styleUrl: './deudas-morosidad.css',
})
export class DeudasMorosidad implements OnInit {
    cargando = true;

    // Banner (siempre valores globales del club, no se ven afectados por los
    // filtros de abajo)
    bannerMetrics: BannerMetric[] = [
        { value: '—', label: 'Deuda total' },
        { value: '—', label: 'Deudores' },
        { value: '—', label: 'Inhabilitados' },
    ];

    // Debtors ranked by outstanding amount (ya viene ordenado desc. por monto desde el backend)
    debtors: DebtorRow[] = [];

    // Jugadores inhabilitados (2+ cuotas), los de mayor deuda primero
    topInhabilitados: DebtorHighlight[] = [];

    // Deuda por categoría/división, mayor a menor: del mes en curso (default) o
    // del año completo, según periodoModo. Global, no se ve afectada por los
    // filtros de la tabla de abajo — es un panel de "dónde está el problema".
    // Cada fila filtra la lista de deudores al hacer clic.
    categoriaBreakdown: CategoriaDeudaRow[] = [];
    categoriaCargando = true;
    periodoModo: PeriodoModo = 'mes';

    readonly anioActual = new Date().getFullYear();
    readonly mesActual = new Date().getMonth() + 1;
    readonly nombreMesActual = NOMBRES_MES[new Date().getMonth()];

    // HU-020: filtros por categoría/división, estado y búsqueda por nombre. Con
    // 700+ jugadores en el club la lista de deudores puede ser enorme, así que
    // esto no es opcional — sin buscador/filtros la tabla sería inutilizable.
    // Las opciones de categoría se arman de la propia lista de pendientes (sin
    // pedir un endpoint aparte). Todo se aplica en el cliente sobre la lista ya
    // cargada: cambiar cualquier filtro es instantáneo, sin ida y vuelta al servidor.
    categoriaOptions: string[] = [];
    estadoOptions: string[] = ['Inhabilitado', 'Solo entrenamientos'];
    selectedCategoria: string | null = null;
    selectedEstado: string | null = null;
    busqueda = '';

    // HU-021: exportar la lista de deudores (respeta el filtro de categoría
    // activo, tal como pide el criterio de aceptación) a PDF o CSV.
    exportMenuOpen = false;
    exportando = false;

    private allPendientes: PendienteJugador[] = [];

    constructor(
        private pagosService: PagosService,
        private reportesService: ReportesService,
        private notifications: NotificationService,
        private cdr: ChangeDetectorRef,
        private elementRef: ElementRef<HTMLElement>,
        private ngZone: NgZone,
    ) {}

    ngOnInit(): void {
        forkJoin({
            pendientes: this.pagosService.getPendientes(),
            resumen: this.pagosService.getResumen(),
        }).subscribe({
            next: ({ pendientes, resumen }) => {
                this.allPendientes = pendientes;
                this.categoriaOptions = Array.from(new Set(pendientes.map((p) => p.categoria))).sort((a, b) =>
                    a.localeCompare(b, 'es'),
                );
                this.aplicarFiltro();
                this.cargando = false;
                // Las métricas del banner son totales del club: siempre sobre la lista
                // completa, no la filtrada por categoría.
                this.animateBannerMetrics(buildBannerTargets(resumen, pendientes));
            },
            error: () => {
                this.cargando = false;
                this.cdr.detectChanges();
            },
        });

        this.cargarDeudaPorCategoria();
    }

    // "Deuda por categoría" viene de un endpoint aparte (agregado del lado del
    // servidor, no derivado de la lista de pendientes) porque necesita filtrar
    // por fecha_vencimiento — algo que la lista de deudores no trae desglosado
    // por período. Con 700+ jugadores, ese agregado tiene que resolverse en SQL.
    cambiarPeriodo(modo: PeriodoModo): void {
        if (this.periodoModo === modo) return;
        this.periodoModo = modo;
        this.cargarDeudaPorCategoria();
    }

    private cargarDeudaPorCategoria(): void {
        this.categoriaCargando = true;
        const mes = this.periodoModo === 'mes' ? this.mesActual : null;

        this.pagosService.getDeudaPorCategoria(this.anioActual, mes).subscribe({
            next: (filas) => {
                this.categoriaBreakdown = buildCategoriaRows(filas);
                this.categoriaCargando = false;
                this.cdr.detectChanges();
            },
            error: () => {
                this.categoriaBreakdown = [];
                this.categoriaCargando = false;
                this.cdr.detectChanges();
            },
        });
    }

    // The first three ranks are highlighted in the table
    isTopRank(rank: number): boolean {
        return rank <= 3;
    }

    onFiltrosChange(): void {
        this.aplicarFiltro();
    }

    get hayFiltrosActivos(): boolean {
        return !!this.selectedCategoria || !!this.selectedEstado || this.busqueda.trim().length > 0;
    }

    get totalJugadores(): number {
        return this.allPendientes.length;
    }

    limpiarFiltros(): void {
        this.selectedCategoria = null;
        this.selectedEstado = null;
        this.busqueda = '';
        this.aplicarFiltro();
    }

    toggleExportMenu(): void {
        this.ngZone.run(() => (this.exportMenuOpen = !this.exportMenuOpen));
    }

    // HU-021: el idCategoria activo (no el nombre) es lo que el backend necesita
    // para acotar el reporte — se busca en la propia lista ya cargada, sin pedir
    // un endpoint de categorías aparte.
    private get idCategoriaActivo(): number | null {
        if (!this.selectedCategoria) return null;
        return this.allPendientes.find((p) => p.categoria === this.selectedCategoria)?.idCategoria ?? null;
    }

    exportarDeudores(formato: 'pdf' | 'csv'): void {
        if (this.exportando) return;

        this.exportMenuOpen = false;
        this.exportando = true;
        const idCategoria = this.idCategoriaActivo;
        const request = formato === 'pdf'
            ? this.reportesService.exportarDeudoresPdf(idCategoria)
            : this.reportesService.exportarDeudoresCsv(idCategoria);

        request.subscribe({
            next: (response) => {
                this.exportando = false;
                triggerBlobDownload(response, `reporte-deudores.${formato}`);
                this.notifications.notify('Reporte de deudores exportado correctamente.', 'success');
                this.cdr.detectChanges();
            },
            error: () => {
                this.exportando = false;
                this.notifications.notify('No se pudo exportar el reporte. Intentá de nuevo.', 'error');
                this.cdr.detectChanges();
            },
        });
    }

    // Cierra el menú Exportar si el click fue afuera. Busca ".export-wrap"
    // puntualmente (no todo el host del componente, que es el dashboard entero)
    // porque si no, cualquier click en la pantalla — un filtro, una fila de la
    // tabla — quedaría "adentro" y el menú nunca se cerraría.
    @HostListener('document:click', ['$event'])
    onDocumentClick(event: MouseEvent): void {
        if (!this.exportMenuOpen) return;

        const wrap = this.elementRef.nativeElement.querySelector('.export-wrap');
        if (wrap && !wrap.contains(event.target as Node)) {
            this.ngZone.run(() => (this.exportMenuOpen = false));
        }
    }

    // Clic en una barra de "Deuda por categoría": salta directo al ranking de
    // esa categoría, sin tener que ir a buscarla en el combo de arriba.
    filtrarPorCategoria(categoria: string): void {
        this.selectedCategoria = categoria;
        this.aplicarFiltro();
    }

    private aplicarFiltro(): void {
        let filtrados = this.allPendientes;

        if (this.selectedCategoria) {
            filtrados = filtrados.filter((p) => p.categoria === this.selectedCategoria);
        }

        if (this.selectedEstado) {
            const buscaInhabilitado = this.selectedEstado === 'Inhabilitado';
            filtrados = filtrados.filter(
                (p) => (p.cantidadCuotas >= CUOTAS_PARA_INHABILITAR) === buscaInhabilitado,
            );
        }

        const termino = this.busqueda.trim().toLowerCase();
        if (termino) {
            filtrados = filtrados.filter(
                (p) => p.nombreCompleto.toLowerCase().includes(termino) || p.dni.includes(termino),
            );
        }

        this.debtors = filtrados.map(mapDebtorRow);
        this.topInhabilitados = filtrados
            .filter((p) => p.cantidadCuotas >= CUOTAS_PARA_INHABILITAR)
            .slice(0, 5)
            .map(mapHighlight);
        this.cdr.detectChanges();
    }

    // Cuenta de 0 hasta el valor real en vez de aparecer de golpe. Respeta
    // prefers-reduced-motion (si el visitante pidió menos movimiento, muestra
    // el valor final directamente, sin animar).
    private animateBannerMetrics(targets: BannerMetricTarget[]): void {
        this.bannerMetrics = targets.map((t) => ({ label: t.label, value: t.format(0) }));

        const reduceMotion = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
        if (reduceMotion) {
            this.bannerMetrics = targets.map((t) => ({ label: t.label, value: t.format(t.raw) }));
            this.cdr.detectChanges();
            return;
        }

        const duration = 900;
        const start = performance.now();

        const step = (now: number) => {
            const elapsed = Math.min(1, (now - start) / duration);
            const eased = 1 - Math.pow(1 - elapsed, 3); // ease-out cúbico

            this.bannerMetrics = targets.map((t) => ({
                label: t.label,
                value: t.format(Math.round(t.raw * eased)),
            }));
            this.cdr.detectChanges();

            if (elapsed < 1) {
                requestAnimationFrame(step);
            }
        };

        requestAnimationFrame(step);
    }
}

function initialsOf(nombreCompleto: string): string {
    const [apellido, nombre] = nombreCompleto.split(',').map((p) => p.trim());
    return `${apellido?.[0] ?? ''}${nombre?.[0] ?? ''}`.toUpperCase();
}

function mapDebtorRow(p: PendienteJugador, index: number): DebtorRow {
    const inhabilitado = p.cantidadCuotas >= CUOTAS_PARA_INHABILITAR;
    return {
        rank: index + 1,
        player: p.nombreCompleto,
        dni: p.dni,
        category: p.categoria,
        instalments: `${p.cantidadCuotas} ${p.cantidadCuotas === 1 ? 'cuota' : 'cuotas'}`,
        debt: CURRENCY_FULL.format(p.montoTotal),
        status: inhabilitado ? 'suspended' : 'partial',
        statusLabel: inhabilitado ? 'Inhabilitado' : 'Solo entrenamientos',
    };
}

function mapHighlight(p: PendienteJugador): DebtorHighlight {
    return {
        initials: initialsOf(p.nombreCompleto),
        player: p.nombreCompleto,
        detail: `${p.categoria} · ${p.cantidadCuotas} cuotas`,
        amount: formatCompactCurrency(p.montoTotal),
    };
}

// El backend ya devuelve las categorías ordenadas por deuda descendente; acá solo
// se calcula el ancho de la barra (relativo a la categoría con más deuda) y se
// formatea el monto.
function buildCategoriaRows(filas: CategoriaDeuda[]): CategoriaDeudaRow[] {
    const maxTotal = filas[0]?.montoTotal || 1;
    return filas.map((f) => ({
        categoria: f.categoria,
        total: formatCompactCurrency(f.montoTotal),
        jugadores: f.cantidadJugadores,
        width: (f.montoTotal / maxTotal) * 100,
    }));
}

function buildBannerTargets(resumen: ResumenPagos, pendientes: PendienteJugador[]): BannerMetricTarget[] {
    const inhabilitados = pendientes.filter((p) => p.cantidadCuotas >= CUOTAS_PARA_INHABILITAR).length;
    return [
        { label: 'Deuda total', raw: resumen.deudaGlobalTotal, format: formatCompactCurrency },
        { label: 'Deudores', raw: resumen.jugadoresMorosos, format: (n) => String(n) },
        { label: 'Inhabilitados', raw: inhabilitados, format: (n) => String(n) },
    ];
}
