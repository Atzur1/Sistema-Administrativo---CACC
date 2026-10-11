import {
    ChangeDetectorRef, Component, DestroyRef, ElementRef, HostListener, NgZone, inject, signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CustomSelect, CustomSelectOption } from '../../shared/custom-select/custom-select';
import { NotificationService } from '../../shared/notifications/notification.service';
import { RecaudacionService, RecaudacionPeriodo, PagoDelPeriodo } from '../../services/recaudacion';
import { PagosService } from '../../services/pagos';
import { triggerBlobDownload } from '../../shared/download-file';
import { animateCountUp } from '../../shared/count-up';
import { formatCompactCurrency } from '../../shared/format-currency';

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

// Radio del donut de la distribución. La circunferencia se deriva de acá para que
// el arco se calcule solo si algún día se cambia el tamaño del gráfico.
const DONUT_RADIO = 54;
const DONUT_CIRCUNFERENCIA = 2 * Math.PI * DONUT_RADIO;

@Component({
    selector: 'app-recaudacion-mensual',
    standalone: true,
    imports: [CommonModule, FormsModule, CustomSelect],
    templateUrl: './recaudacion-mensual.html',
    styleUrl: './recaudacion-mensual.css',
})
export class RecaudacionMensual {

    private zone = inject(NgZone);
    private destroyRef = inject(DestroyRef);
    private elementRef = inject(ElementRef);
    private cdr = inject(ChangeDetectorRef);
    private notifications = inject(NotificationService);
    private recaudacionService = inject(RecaudacionService);
    private pagosService = inject(PagosService);

    // Menú "Exportar Reporte": el archivo lo arma el servidor con los mismos
    // datos del período que se están viendo, filtro de categoría incluido.
    exportMenuOpen = false;
    exportando = false;

    // Opciones de los filtros. Los años se recalculan con la fecha de hoy, igual que
    // en Cuotas y pagos, así la ventana se corre sola con los años.
    meses = MESES;
    anios = buildYearOptions();
    // Las 13 del club, vengan o no con pagos en el período (HU-020 ya expone el
    // catálogo completo). Filtrar por una categoría sin cobros muestra la pantalla
    // en cero, que es la respuesta correcta y no un error.
    categorias: CustomSelectOption[] = [TODAS_LAS_CATEGORIAS];

    // El período se aplica al instante (es el eje de la pantalla, y la HU pide que
    // al cambiar el mes cambien los datos). La categoría se escribe en el borrador
    // y recién impacta con "Aplicar filtros".
    mesSeleccionado: string;
    anioSeleccionado: number;

    categoriaBorrador: string = '';
    private categoriaAplicada: string = '';

    datos: RecaudacionPeriodo | null = null;
    cargando = false;
    huboError = false;

    // Tabla
    filas: PagoDelPeriodo[] = [];

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
        this.destroyRef.onDestroy(() => this.cancelarAnimacion());
    }

    onMesChange(mes: string | number | null): void {
        this.mesSeleccionado = String(mes);
        this.cargarPeriodo();
    }

    onAnioChange(anio: string | number | null): void {
        this.anioSeleccionado = Number(anio);
        this.cargarPeriodo();
    }

    onCategoriaChange(categoria: string | number | null): void {
        this.categoriaBorrador = categoria == null ? '' : String(categoria);
    }

    aplicarFiltros(): void {
        this.categoriaAplicada = this.categoriaBorrador;
        this.cargarPeriodo();
    }

    limpiarFiltros(): void {
        this.categoriaBorrador = '';
        this.aplicarFiltros();
    }

    // true mientras el borrador no coincida con lo aplicado: sirve para avisar en la
    // vista que hay cambios sin aplicar.
    get hayCambiosSinAplicar(): boolean {
        return this.categoriaBorrador !== this.categoriaAplicada;
    }

    get periodoLabel(): string {
        return `${this.mesSeleccionado} ${this.anioSeleccionado}`;
    }

    get totalCompacto(): string {
        return this.datos ? formatCompactCurrency(this.datos.total) : '—';
    }

    // Un período puede existir y no tener cobros (mes futuro, o un mes sin
    // movimiento): son cero, no un error.
    get hayCobros(): boolean {
        return this.datos != null && this.datos.pagos > 0;
    }

    formatMonto(valor: number): string {
        return CURRENCY_FULL.format(valor);
    }

    toggleExportMenu(): void {
        this.zone.run(() => (this.exportMenuOpen = !this.exportMenuOpen));
    }

    // Exporta el período que se está viendo, con el mismo filtro de categoría
    // aplicado: lo que se descarga coincide con lo que está en pantalla.
    exportarRecaudacion(formato: 'pdf' | 'csv'): void {
        if (this.exportando) return;

        this.exportMenuOpen = false;
        this.exportando = true;

        const mes = MESES.indexOf(this.mesSeleccionado) + 1;
        const idCategoria = this.categoriaAplicada === '' ? null : Number(this.categoriaAplicada);
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

    // Cierra el menú Exportar si el click fue afuera. Busca ".export-wrap"
    // puntualmente (no todo el host del componente, que es el dashboard entero)
    // porque si no, cualquier click en la pantalla quedaría "adentro".
    @HostListener('document:click', ['$event'])
    onDocumentClick(event: MouseEvent): void {
        if (!this.exportMenuOpen) return;

        const wrap = this.elementRef.nativeElement.querySelector('.export-wrap');
        if (wrap && !wrap.contains(event.target as Node)) {
            this.zone.run(() => (this.exportMenuOpen = false));
        }
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

    private cargarPeriodo(): void {
        const mes = MESES.indexOf(this.mesSeleccionado) + 1;
        const idCategoria = this.categoriaAplicada === '' ? null : Number(this.categoriaAplicada);

        this.cargando = true;
        this.huboError = false;

        this.recaudacionService.getDelPeriodo(this.anioSeleccionado, mes, idCategoria).subscribe({
            next: (periodo) => {
                this.datos = periodo;
                this.filas = periodo.detalle;
                this.cargando = false;
                this.recalcularDesglose();
                this.cdr.detectChanges();
            },
            error: () => {
                this.datos = null;
                this.filas = [];
                this.desglose = [];
                this.arcoDonut = 0;
                this.cargando = false;
                this.huboError = true;
                this.cancelarAnimacion();
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

// Año actual + los 2 anteriores, igual que en Cuotas y pagos
function buildYearOptions(): number[] {
    const actual = new Date().getFullYear();
    return [actual, actual - 1, actual - 2];
}
