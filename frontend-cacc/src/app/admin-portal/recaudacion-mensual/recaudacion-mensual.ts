import {
    ChangeDetectorRef, Component, DestroyRef, NgZone, inject, signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CustomSelect, CustomSelectOption } from '../../shared/custom-select/custom-select';
import { Cargando } from '../../shared/cargando/cargando';
import { EstadoVacio } from '../../shared/estado-vacio/estado-vacio';
import { NotificationService } from '../../shared/notifications/notification.service';
import { RecaudacionService } from '../../services/recaudacion';
import {
    CollectionPeriodModel, PeriodPaymentModel, PeriodPaymentsSortBy, SortDirection,
} from '../../models/CollectionPeriodModel';
import { PagosService } from '../../services/pagos';
import { triggerBlobDownload } from '../../shared/download-file';
import { animateCountUp } from '../../shared/count-up';
import { ExportFormat } from '../../shared/export-menu/export-menu';
import { ReportExportMenu as ExportMenu } from '../../shared/export-menu/report-export-menu';
import { ActiveFilter, ActiveFiltersCache, FilterBar } from '../../shared/filter-bar/filter-bar';
import { LatestRequest } from '../../shared/latest-request';
import { SECCIONES } from '../secciones';

// Mismo formateador que usan Cuotas y pagos y Actualización de aranceles, para que
// un monto se lea igual en todo el portal ("$5.000.000", sin centavos).
const CURRENCY_FULL = new Intl.NumberFormat('es-AR', {
    style: 'currency',
    currency: 'ARS',
    maximumFractionDigits: 0,
});

// Una fila del desglose por medio de pago (donut + leyenda + barra)
interface DesgloseItem {
    nombre: string;
    pagos: number;
    monto: number;
    porcentaje: number;
    tono: 'efectivo' | 'transferencia';
}

const MESES = [
    'Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio',
    'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre',
];

// value null = sin filtro. El resto de las opciones las trae /api/categorias.
const TODAS_LAS_CATEGORIAS: CustomSelectOption = { value: '', label: 'Todas' };

// Años que ofrece el selector: el actual y los anteriores (no hay cobros futuros que consultar).
const ANIOS_HACIA_ATRAS = 5;

// Radio del donut de la distribución. La circunferencia se deriva de acá para que
// el arco se calcule solo si algún día se cambia el tamaño del gráfico.
const DONUT_RADIO = 54;
const DONUT_CIRCUNFERENCIA = 2 * Math.PI * DONUT_RADIO;

// HU-107: mismo tamaño de página que "Pagos registrados" (HU-088).
const TAMANIO_PAGINA = 10;

// HU-107: la fecha llega del servidor ya en hora del club ("2026-10-31T14:35:12-03:00", o "2026-10-31" en los
// pagos viejos sin hora). Se muestran esos dígitos tal cual: pasarla por Date la convertiría a la zona del
// navegador, y en una compu con otra zona el cobro aparecería en otra hora o en otro día.
const FECHA_DEL_SERVIDOR = /^(\d{4})-(\d{2})-(\d{2})(?:T(\d{2}):(\d{2}))?/;

export function formatFechaPago(fila: Pick<PeriodPaymentModel, 'fechaPago' | 'fechaHoraRegistro'>): string {
    const partes = FECHA_DEL_SERVIDOR.exec(fila.fechaHoraRegistro ?? fila.fechaPago ?? '');
    if (!partes) return '—';
    const [, anio, mes, dia, hora, minutos] = partes;
    return hora ? `${dia}/${mes}/${anio} ${hora}:${minutos}` : `${dia}/${mes}/${anio}`;
}

@Component({
    selector: 'app-recaudacion-mensual',
    standalone: true,
    imports: [CommonModule, FormsModule, CustomSelect, Cargando, EstadoVacio, ExportMenu, FilterBar],
    templateUrl: './recaudacion-mensual.html',
    styleUrl: './recaudacion-mensual.css',
})
export class RecaudacionMensual {
  // Nombre oficial de la sección (HU-103): el mismo del menú lateral y de la barra superior.
  readonly tituloPantalla = SECCIONES['recaudacion-mensual'];


    private zone = inject(NgZone);
    private destroyRef = inject(DestroyRef);
    private cdr = inject(ChangeDetectorRef);
    private notifications = inject(NotificationService);
    private recaudacionService = inject(RecaudacionService);
    private pagosService = inject(PagosService);

    // HU-097: solo vale la respuesta del último período pedido; cambiar de mes rápido cancela el pedido anterior.
    private readonly consultaPeriodo = new LatestRequest();
    // HU-107: lo mismo para la página de la tabla (cambiar rápido de página u orden).
    private readonly consultaPagos = new LatestRequest();

    // Menú "Exportar Reporte" (compartido): el archivo lo arma el servidor con los mismos
    // datos del período que se están viendo, filtro de categoría incluido.
    exportando = false;
    get mesExportacion(): number { return MESES.indexOf(this.mesSeleccionado) + 1; }
    get categoriaExportacion(): number | null { return this.categoriaSeleccionada === '' ? null : Number(this.categoriaSeleccionada); }

    // La categoría trae las 13 del club, vengan o no con pagos en el período (HU-020 ya expone el catálogo
    // completo). Filtrar por una categoría sin cobros muestra la pantalla en cero, que es la respuesta correcta
    // y no un error.
    categorias: CustomSelectOption[] = [TODAS_LAS_CATEGORIAS];

    // Los filtros se aplican al instante (la HU pide que al cambiar el mes cambien los datos): no hay botón
    // "Aplicar". El período se elige con dos desplegables, Mes y Año (HU-070), y no puede pasar del mes actual,
    // que es lo último con cobros.
    mesSeleccionado: string;
    anioSeleccionado: number;
    categoriaSeleccionada: string = '';

    datos: CollectionPeriodModel | null = null;
    cargando = false;
    huboError = false;

    // Tabla (HU-107): solo la página visible. La filtra, ordena y pagina el servidor; el total de cobros del
    // período viene en la misma respuesta, no se cuenta con filas.length.
    filas: PeriodPaymentModel[] = [];
    readonly tamanioPagina = TAMANIO_PAGINA;
    pagina = 1;
    totalPagos = 0;
    totalPaginas = 1;
    ordenarPor: PeriodPaymentsSortBy = 'fecha';
    direccion: SortDirection = 'desc';
    cargandoPagos = false;
    errorPagos = false;

    // Desglose por medio de pago
    desglose: DesgloseItem[] = [];
    pagosDisplay = signal('0');

    // Geometría del donut
    readonly donutRadio = DONUT_RADIO;
    readonly donutCircunferencia = DONUT_CIRCUNFERENCIA;
    arcoDonut = 0;

    // Cancela la animación en curso si se cambia de período antes de que termine,
    // para que dos count-up no se peleen escribiendo el mismo signal.
    private cancelarAnimacion: () => void = () => {};

    constructor() {
        const hoy = new Date();
        this.mesSeleccionado = MESES[hoy.getMonth()];
        this.anioSeleccionado = hoy.getFullYear();

        this.cargarCategorias();
        this.cargarPeriodo();
        this.destroyRef.onDestroy(() => {
            this.cancelarAnimacion();
            this.consultaPeriodo.cancel();
            this.consultaPagos.cancel();
        });
    }

    // En el año en curso solo se ofrecen los meses que ya empezaron.
    get mesesOpciones(): CustomSelectOption[] {
        const hoy = new Date();
        const ultimo = this.anioSeleccionado === hoy.getFullYear() ? hoy.getMonth() : MESES.length - 1;
        return MESES.slice(0, ultimo + 1).map((mes) => ({ value: mes, label: mes }));
    }

    get aniosOpciones(): CustomSelectOption[] {
        const actual = new Date().getFullYear();
        return Array.from({ length: ANIOS_HACIA_ATRAS + 1 }, (_, i) => actual - i).map((anio) => ({ value: anio, label: String(anio) }));
    }

    onMesChange(mes: string | number | null): void {
        if (mes == null || mes === this.mesSeleccionado) return;
        this.mesSeleccionado = String(mes);
        this.cargarPeriodo();
    }

    // Al pasar al año en curso con un mes que todavía no llegó, se queda en el mes actual.
    onAnioChange(anio: string | number | null): void {
        if (anio == null || Number(anio) === this.anioSeleccionado) return;
        this.anioSeleccionado = Number(anio);
        const hoy = new Date();
        if (this.anioSeleccionado === hoy.getFullYear() && MESES.indexOf(this.mesSeleccionado) > hoy.getMonth()) {
            this.mesSeleccionado = MESES[hoy.getMonth()];
        }
        this.cargarPeriodo();
    }

    // Lo que muestran las tarjetas de Efectivo y Transferencia debajo del monto: cantidad de pagos y su porcentaje,
    // el mismo que el desglose (por cantidad de pagos).
    detalleMedio(tono: 'efectivo' | 'transferencia'): string {
        if (!this.datos) return '';
        const item = this.desglose.find((d) => d.tono === tono);
        const pagos = item?.pagos ?? 0;
        const texto = `${pagos} ${pagos === 1 ? 'pago' : 'pagos'}`;
        return item ? `${texto} · ${item.porcentaje}% de los pagos` : texto;
    }

    onCategoriaChange(categoria: string | number | null): void {
        this.categoriaSeleccionada = categoria == null ? '' : String(categoria);
        this.cargarPeriodo();
    }

    limpiarFiltros(): void {
        this.categoriaSeleccionada = '';
        this.cargarPeriodo();
    }

    // La categoría se puede limpiar solo si hay una elegida.
    get hayFiltros(): boolean {
        return this.categoriaSeleccionada !== '';
    }

    // HU-097: el período (mes y año) siempre está puesto y no es un filtro que se quite; la categoría sí.
    private readonly filtrosCache = new ActiveFiltersCache();
    get filtrosActivos(): ActiveFilter[] {
        if (!this.hayFiltros) return this.filtrosCache.get([]);
        const categoria = this.categorias.find((c) => String(c.value) === this.categoriaSeleccionada);
        return this.filtrosCache.get([{ key: 'categoria', label: `Categoría: ${categoria?.label ?? this.categoriaSeleccionada}` }]);
    }

    quitarFiltro(key: string): void {
        if (key === 'categoria') this.limpiarFiltros();
    }

    get periodoLabel(): string {
        return `${this.mesSeleccionado} ${this.anioSeleccionado}`;
    }

    // Un período puede existir y no tener cobros (mes futuro, o un mes sin
    // movimiento): son cero, no un error.
    get hayCobros(): boolean {
        return this.datos != null && this.datos.pagos > 0;
    }

    formatMonto(valor: number): string {
        return CURRENCY_FULL.format(valor);
    }

    formatFecha(fila: PeriodPaymentModel): string {
        return formatFechaPago(fila);
    }

    // ===== PAGINACIÓN Y ORDEN (HU-107) =====
    // Cambian solo la tabla: las tarjetas y el donut son del período completo y no se vuelven a pedir.

    irAPagina(nuevaPagina: number): void {
        if (nuevaPagina < 1 || nuevaPagina > this.totalPaginas || nuevaPagina === this.pagina) return;
        this.pagina = nuevaPagina;
        this.cargarPagos();
    }

    reintentarPagos(): void {
        if (!this.cargandoPagos) this.cargarPagos();
    }

    // Clic en el mismo encabezado invierte la dirección; en otro, arranca de mayor a menor (lo más reciente o lo
    // más alto primero). Siempre vuelve a la página 1: la página 5 de otro orden son otras filas.
    ordenar(campo: PeriodPaymentsSortBy): void {
        if (campo === this.ordenarPor) {
            this.direccion = this.direccion === 'desc' ? 'asc' : 'desc';
        } else {
            this.ordenarPor = campo;
            this.direccion = 'desc';
        }
        this.pagina = 1;
        this.cargarPagos();
    }

    ariaSort(campo: PeriodPaymentsSortBy): 'ascending' | 'descending' | 'none' {
        if (campo !== this.ordenarPor) return 'none';
        return this.direccion === 'asc' ? 'ascending' : 'descending';
    }

    textoOrden(campo: PeriodPaymentsSortBy): string {
        if (campo !== this.ordenarPor) return `Ordenar por ${campo}`;
        const actual = campo === 'fecha'
            ? (this.direccion === 'desc' ? 'más reciente primero' : 'más antiguo primero')
            : (this.direccion === 'desc' ? 'mayor primero' : 'menor primero');
        return `Ordenado por ${campo}, ${actual}. Cambiar el orden`;
    }

    get conteoPagos(): string {
        return `${this.totalPagos} ${this.totalPagos === 1 ? 'pago' : 'pagos'} en ${this.periodoLabel}`;
    }

    // Exporta el período que se está viendo, con el mismo filtro de categoría
    // aplicado: lo que se descarga coincide con lo que está en pantalla.
    exportarRecaudacion(formato: ExportFormat): void {
        if (this.exportando) return;

        this.exportando = true;

        const mes = MESES.indexOf(this.mesSeleccionado) + 1;
        const idCategoria = this.categoriaSeleccionada === '' ? null : Number(this.categoriaSeleccionada);
        const request =
            formato === 'pdf'
                ? this.recaudacionService.exportarPdf(this.anioSeleccionado, mes, idCategoria)
                : this.recaudacionService.exportarCsv(this.anioSeleccionado, mes, idCategoria);

        request.subscribe({
            next: (response) => {
                this.exportando = false;
                triggerBlobDownload(response, `reporte-recaudacion.${formato}`);
                this.notifications.notify('Reporte de recaudación exportado correctamente.', 'success');
                this.cdr.detectChanges();
            },
            error: () => {
                this.exportando = false;
                this.notifications.notify('No se pudo exportar el reporte. Intentá de nuevo.', 'error');
                this.cdr.detectChanges();
            },
        });
    }

    private cargarCategorias(): void {
        this.pagosService.getCategorias().subscribe({
            next: (categorias) => {
                this.categorias = [
                    TODAS_LAS_CATEGORIAS,
                    ...categorias.map((c) => ({ value: String(c.idCategoria), label: c.nombre })),
                ];
                this.cdr.detectChanges();
            },
            error: () => {},
        });
    }

    // Cambió el período o la categoría: totales nuevos y la tabla desde la página 1. El orden elegido se mantiene.
    private cargarPeriodo(): void {
        const mes = MESES.indexOf(this.mesSeleccionado) + 1;
        const idCategoria = this.idCategoriaSeleccionada;

        this.cargando = true;
        this.huboError = false;
        this.pagina = 1;
        // Los datos anteriores pertenecen a otro filtro: no mostrarlos bajo la nueva etiqueta.
        this.filas = [];
        this.totalPagos = 0;
        this.totalPaginas = 1;
        this.datos = null;
        this.cargarPagos();

        this.consultaPeriodo.run(this.recaudacionService.getDelPeriodo(this.anioSeleccionado, mes, idCategoria), {
            next: (periodo) => {
                this.datos = periodo;
                this.cargando = false;
                this.recalcularDesglose();
                this.cdr.detectChanges();
            },
            error: () => {
                this.datos = null;
                this.desglose = [];
                this.arcoDonut = 0;
                this.cargando = false;
                this.huboError = true;
                this.cancelarAnimacion();
                this.cdr.detectChanges();
            },
        });
    }

    private get idCategoriaSeleccionada(): number | null {
        return this.categoriaSeleccionada === '' ? null : Number(this.categoriaSeleccionada);
    }

    private cargarPagos(): void {
        this.cargandoPagos = true;
        this.errorPagos = false;

        const consulta = this.recaudacionService.getPagos({
            anio: this.anioSeleccionado,
            mes: MESES.indexOf(this.mesSeleccionado) + 1,
            idCategoria: this.idCategoriaSeleccionada,
            page: this.pagina,
            pageSize: this.tamanioPagina,
            sortBy: this.ordenarPor,
            sortDirection: this.direccion,
        });

        this.consultaPagos.run(consulta, {
            next: (pagina) => {
                this.totalPagos = pagina.total;
                this.totalPaginas = pagina.totalPaginas;
                // Si entre una consulta y otra se anularon cobros y la página quedó fuera de rango, se va a la
                // última que existe en vez de mostrar una tabla vacía con pagos en el período.
                if (pagina.items.length === 0 && pagina.total > 0 && this.pagina > pagina.totalPaginas) {
                    this.pagina = pagina.totalPaginas;
                    this.cargarPagos();
                    return;
                }
                this.filas = pagina.items;
                this.cargandoPagos = false;
                this.cdr.detectChanges();
            },
            error: () => {
                this.filas = [];
                this.totalPagos = 0;
                this.totalPaginas = 1;
                this.cargandoPagos = false;
                this.errorPagos = true;
                this.cdr.detectChanges();
            },
        });
    }

    private recalcularDesglose(): void {
        this.cancelarAnimacion();

        if (!this.datos || this.datos.pagos === 0) {
            this.desglose = [];
            this.arcoDonut = 0;
            return;
        }

        const { efectivo, transferencia, pagos, pagosEfectivo, pagosTransferencia } = this.datos;

        // Se redondea un porcentaje solo y el otro es el complemento, para que la
        // barra nunca muestre 39% + 60% = 99%.
        const porcentajeTransferencia = Math.round((pagosTransferencia * 100) / pagos);
        const porcentajeEfectivo = 100 - porcentajeTransferencia;

        this.desglose = [
            {
                nombre: 'Transferencia',
                pagos: pagosTransferencia,
                monto: transferencia,
                porcentaje: porcentajeTransferencia,
                tono: 'transferencia',
            },
            {
                nombre: 'Efectivo',
                pagos: pagosEfectivo,
                monto: efectivo,
                porcentaje: porcentajeEfectivo,
                tono: 'efectivo',
            },
        ];

        this.arcoDonut = (porcentajeTransferencia / 100) * DONUT_CIRCUNFERENCIA;

        this.cancelarAnimacion = animateCountUp(this.zone, [
            { target: pagos, display: this.pagosDisplay },
        ]);
    }
}

