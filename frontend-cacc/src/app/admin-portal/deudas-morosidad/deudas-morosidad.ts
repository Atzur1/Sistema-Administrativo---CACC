import {
  ChangeDetectorRef,
  Component,
  ElementRef,
  HostListener,
  NgZone,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import {
  Categoria,
  CategoriaDeuda,
  PagosService,
  PendienteJugador,
  ResumenPagos,
} from '../../services/pagos';
import { PlayerAccountModel } from '../../models/PlayerAccountModel';
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

// Un alumno deudor (api/pagos/player-accounts?onlyDebtors=true), con lo que debe
interface AlumnoRow {
  idJugador: number;
  nombreCompleto: string;
  dni: string;
  categoria: string;
  cantidadCuotas: number;
  montoTotal: number;
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
  'Enero',
  'Febrero',
  'Marzo',
  'Abril',
  'Mayo',
  'Junio',
  'Julio',
  'Agosto',
  'Septiembre',
  'Octubre',
  'Noviembre',
  'Diciembre',
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

// HU-020 (QA, 24/09): opción real y seleccionable del combo, no solo el
// placeholder de fondo — así "Todas las categorías" aparece en la lista de
// opciones en vez de ser un texto que nunca se puede elegir.
const TODAS_CATEGORIAS = 'Todas las categorías';

@Component({
  selector: 'app-deudas-morosidad',
  standalone: true,
  imports: [CommonModule, FormsModule, CustomSelect],
  templateUrl: './deudas-morosidad.html',
  styleUrl: './deudas-morosidad.css',
})
export class DeudasMorosidad implements OnInit {
  cargando = true;

  // Banner: valores globales del club por default; si hay una categoría
  // filtrada (HU-020, QA 24/09), se recalcula sobre ese subgrupo — ver
  // recalcularBanner().
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
  // Las opciones de categoría vienen del catálogo completo (api/categorias,
  // QA 24/09) — antes se armaban de la propia lista de deudores, así que una
  // categoría sin morosos hoy nunca aparecía para elegir. El filtrado en sí
  // se sigue aplicando en el cliente sobre la lista ya cargada: cambiar
  // cualquier filtro es instantáneo, sin ida y vuelta al servidor.
  categoriaOptions: string[] = [TODAS_CATEGORIAS];
  estadoOptions: string[] = ['Inhabilitado', 'Solo entrenamientos'];
  selectedCategoria: string | null = null;
  selectedEstado: string | null = null;
  busqueda = '';

  // nombre -> id, para poder filtrar/exportar por categoría aunque esa
  // categoría no tenga hoy ningún jugador en allPendientes (de donde se
  // resolvía el id antes de HU-020, y por eso fallaba con 0 deudores).
  private categoriasMap = new Map<string, number>();

  // HU-021: exportar la lista de deudores (respeta el filtro de categoría
  // activo, tal como pide el criterio de aceptación) a PDF o CSV.
  exportMenuOpen = false;
  exportando = false;

  // HU-029: la tabla muestra solo a los alumnos deudores (api/pagos/player-accounts),
  // del que más cuotas debe al que menos (a igual cantidad, el de mayor monto primero).
  // allPendientes sigue cargándose aparte: alimenta el banner y el idCategoria del export.
  private allRows: AlumnoRow[] = [];
  private allPendientes: PendienteJugador[] = [];
  // Guardado para poder recalcular el banner cuando cambia la categoría
  // elegida, sin pedirlo de nuevo al servidor cada vez (HU-020, QA 24/09).
  private resumenGlobal: ResumenPagos | null = null;

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
        this.resumenGlobal = resumen;
        this.recalcularBanner();
      },
      error: () => {
        this.cdr.detectChanges();
      },
    });

    this.cargarCategorias();
    this.cargarPadron();
    this.cargarDeudaPorCategoria();
  }

  // HU-020 (QA, 24/09): catálogo completo de categorías — antes el combo se
  // armaba con las categorías de la propia lista de deudores, así que una
  // categoría sin morosos hoy (12 de las 13 del club, en la prueba de QA)
  // nunca aparecía para elegir.
  private cargarCategorias(): void {
    this.pagosService.getCategorias().subscribe({
      next: (categorias) => {
        this.categoriasMap = new Map(categorias.map((c) => [c.nombre, c.idCategoria]));
        this.categoriaOptions = [TODAS_CATEGORIAS, ...categorias.map((c) => c.nombre)];
        this.cdr.detectChanges();
      },
      error: () => {
        // Si el catálogo no llega a cargar, cargarPadron() arma el combo con
        // las categorías que sí tienen deudores (degradado, no vacío).
        this.cdr.detectChanges();
      },
    });
  }

  private cargarPadron(): void {
    this.pagosService.getPlayerAccounts(true).subscribe({
      next: (accounts) => {
        this.allRows = accounts
          .map(mapAccountRow)
          .sort((a, b) => b.cantidadCuotas - a.cantidadCuotas || b.montoTotal - a.montoTotal);
        if (this.categoriaOptions.length <= 1) {
          this.categoriaOptions = [
            TODAS_CATEGORIAS,
            ...Array.from(new Set(this.allRows.map((r) => r.categoria))).sort((a, b) =>
              a.localeCompare(b, 'es'),
            ),
          ];
        }
        this.aplicarFiltro();
        this.cargando = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.cargando = false;
        this.notifications.notify(
          'No se pudo cargar la lista de jugadores. Intentá de nuevo.',
          'error',
        );
        this.cdr.detectChanges();
      },
    });
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

  // El valor real de la categoría elegida, o null si no hay ninguna (ni
  // seleccionada, ni "Todas las categorías" explícitamente elegida).
  private get categoriaActiva(): string | null {
    if (!this.selectedCategoria || this.selectedCategoria === TODAS_CATEGORIAS) return null;
    return this.selectedCategoria;
  }

  get hayFiltrosActivos(): boolean {
    return !!this.categoriaActiva || !!this.selectedEstado || this.busqueda.trim().length > 0;
  }

  get totalJugadores(): number {
    return this.allRows.length;
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
  // para acotar el reporte. Se resuelve contra el catálogo completo (HU-020),
  // no contra allPendientes: antes, filtrar/exportar una categoría sin
  // deudores hoy no encontraba ningún id (porque esa categoría no tenía
  // ninguna fila en allPendientes) y el filtro quedaba roto.
  private get idCategoriaActivo(): number | null {
    const categoria = this.categoriaActiva;
    if (!categoria) return null;
    return this.categoriasMap.get(categoria) ?? null;
  }

  exportarDeudores(formato: 'pdf' | 'csv'): void {
    if (this.exportando) return;

    this.exportMenuOpen = false;
    this.exportando = true;
    const idCategoria = this.idCategoriaActivo;
    const request =
      formato === 'pdf'
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
    const categoria = this.categoriaActiva;
    let filtrados = this.allRows;

    if (categoria) {
      filtrados = filtrados.filter((p) => p.categoria === categoria);
    }

    if (this.selectedEstado === 'Inhabilitado') {
      filtrados = filtrados.filter((p) => p.cantidadCuotas >= CUOTAS_PARA_INHABILITAR);
    } else if (this.selectedEstado === 'Solo entrenamientos') {
      filtrados = filtrados.filter((p) => p.cantidadCuotas === 1);
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
    this.recalcularBanner();
    this.cdr.detectChanges();
  }

  // HU-020 (QA, 24/09): "Métricas del Subgrupo" — al elegir una categoría, el
  // banner de arriba tiene que mostrar el monto y la cantidad de morosos de
  // ESA categoría, no seguir mostrando los totales globales del club. Se
  // calcula sobre allRows (la misma lista que alimenta la grilla de abajo)
  // para que el subtotal coincida exacto con lo que se ve en pantalla.
  private recalcularBanner(): void {
    const categoria = this.categoriaActiva;

    if (categoria) {
      const filas = this.allRows.filter((p) => p.categoria === categoria);
      const montoTotal = filas.reduce((acc, p) => acc + p.montoTotal, 0);
      const inhabilitados = filas.filter((p) => p.cantidadCuotas >= CUOTAS_PARA_INHABILITAR).length;
      this.animateBannerMetrics([
        { label: 'Deuda total', raw: montoTotal, format: formatCompactCurrency },
        { label: 'Deudores', raw: filas.length, format: (n) => String(n) },
        { label: 'Inhabilitados', raw: inhabilitados, format: (n) => String(n) },
      ]);
      return;
    }

    // Sin categoría elegida: los totales globales del club. Si todavía no
    // llegó la respuesta de /pagos/resumen, se deja el banner como está
    // (placeholders) — esa misma respuesta va a llamar a este método de nuevo.
    if (!this.resumenGlobal) return;
    this.animateBannerMetrics(buildBannerTargets(this.resumenGlobal, this.allPendientes));
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

function mapAccountRow(a: PlayerAccountModel): AlumnoRow {
  return {
    idJugador: a.playerId,
    nombreCompleto: `${a.lastName}, ${a.firstName}`,
    dni: a.dni,
    categoria: a.category,
    cantidadCuotas: a.pendingInstallments,
    montoTotal: a.amountOwed,
  };
}

function mapDebtorRow(p: AlumnoRow, index: number): DebtorRow {
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

function mapHighlight(p: AlumnoRow): DebtorHighlight {
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

function buildBannerTargets(
  resumen: ResumenPagos,
  pendientes: PendienteJugador[],
): BannerMetricTarget[] {
  const inhabilitados = pendientes.filter(
    (p) => p.cantidadCuotas >= CUOTAS_PARA_INHABILITAR,
  ).length;
  return [
    { label: 'Deuda total', raw: resumen.deudaGlobalTotal, format: formatCompactCurrency },
    { label: 'Deudores', raw: resumen.jugadoresMorosos, format: (n) => String(n) },
    { label: 'Inhabilitados', raw: inhabilitados, format: (n) => String(n) },
  ];
}
