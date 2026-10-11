import { Component, DestroyRef, ElementRef, HostListener, NgZone, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CustomSelect } from '../../shared/custom-select/custom-select';
import { NotificationService } from '../../shared/notifications/notification.service';
import { animateCountUp } from '../../shared/count-up';
import { formatCompactCurrency } from '../../shared/format-currency';

// Mismo formateador que usan Cuotas y pagos y Actualización de aranceles, para que
// un monto se lea igual en todo el portal ("$5.000.000", sin centavos).
const CURRENCY_FULL = new Intl.NumberFormat('es-AR', {
    style: 'currency',
    currency: 'ARS',
    maximumFractionDigits: 0,
});

// Lo recaudado en un período, ya separado por medio de pago. Los dos subtotales
// siempre suman el total: es la única invariante que el mock tiene que respetar.
interface RecaudacionPeriodo {
    total: number;
    efectivo: number;
    transferencia: number;
    pagos: number;
}

// Un jugador del período, con lo que pagó y el medio que usó
interface PagoJugador {
    jugador: string;
    dni: string;
    categoria: string;
    monto: number;
    medioPago: 'Efectivo' | 'Transferencia';
}

// Una fila del desglose por medio de pago (donut + leyenda + barra)
interface DesgloseItem {
    nombre: string;
    pagos: number;
    monto: number;
    porcentaje: number;
    tono: 'efectivo' | 'transferencia';
}

// HU-018: datos mockeados hasta que exista el endpoint. Hoy no hay forma de pedirle
// al backend la recaudación agrupada por metodo_pago: ni PagosController ni los DAO
// exponen esa agregación. Los dos períodos que pidió la HU van escritos tal cual; el
// resto se genera (ver generarPeriodo) para que ningún mes del selector quede en
// blanco mientras se prueba la pantalla.
const PERIODOS_FIJOS: Record<string, RecaudacionPeriodo> = {
    '2026-9': { total: 5_000_000, efectivo: 2_000_000, transferencia: 3_000_000, pagos: 150 },
    '2026-8': { total: 4_200_000, efectivo: 1_800_000, transferencia: 2_400_000, pagos: 125 },
};

// Jugadores mockeados con su medio de pago. Se mezclan los dos medios a propósito
// para que el desglose del período no quede de un solo color.
const PAGOS_JUGADORES: PagoJugador[] = [
    { jugador: 'PRUEBA, VALENTINA', dni: '95000014', categoria: 'AFA 20067', monto: 20_000, medioPago: 'Transferencia' },
    { jugador: 'PRUEBA, SOFIA', dni: '95000021', categoria: 'AFA 20068', monto: 10_000, medioPago: 'Efectivo' },
    { jugador: 'PRUEBA, SANTIAGO', dni: '95000005', categoria: 'AFA 20067', monto: 20_000, medioPago: 'Transferencia' },
    { jugador: 'PRUEBA, NICOLAS', dni: '95000011', categoria: 'AFA 20067', monto: 15_000, medioPago: 'Efectivo' },
    { jugador: 'PRUEBA, CAMILA', dni: '95000042', categoria: 'AFA 20068', monto: 10_000, medioPago: 'Transferencia' },
    { jugador: 'PRUEBA, MATIAS', dni: '95000303', categoria: 'AFA 20067', monto: 20_000, medioPago: 'Efectivo' },
    { jugador: 'PRUEBA, LUCIA', dni: '95000078', categoria: 'AFA 20068', monto: 10_000, medioPago: 'Transferencia' },
    { jugador: 'PRUEBA, TOMAS', dni: '95000156', categoria: 'AFA 20067', monto: 7_500, medioPago: 'Efectivo' },
    { jugador: 'PRUEBA, MARTINA', dni: '95000203', categoria: 'AFA 20068', monto: 10_000, medioPago: 'Efectivo' },
    { jugador: 'PRUEBA, FACUNDO', dni: '95000247', categoria: 'AFA 20067', monto: 20_000, medioPago: 'Transferencia' },
    { jugador: 'PRUEBA, JULIETA', dni: '95000318', categoria: 'AFA 20068', monto: 10_000, medioPago: 'Transferencia' },
    { jugador: 'PRUEBA, BENJAMIN', dni: '95000392', categoria: 'AFA 20067', monto: 12_500, medioPago: 'Efectivo' },
];

const MESES = [
    'Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio',
    'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre',
];

const TODAS_LAS_CATEGORIAS = 'Todas';

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
    private notifications = inject(NotificationService);

    // Menú "Exportar Reporte". Por ahora es solo la maqueta visual: el reporte
    // real necesita un endpoint en ReportesController que todavía no existe, y
    // armarlo con los datos mockeados daría un PDF que miente.
    exportMenuOpen = false;

    // Opciones de los filtros. Los años se recalculan con la fecha de hoy, igual que
    // en Cuotas y pagos, así la ventana se corre sola con los años.
    meses = MESES;
    anios = buildYearOptions();
    categorias = [TODAS_LAS_CATEGORIAS, ...categoriasDeLosJugadores()];

    // El período se aplica al instante (es el eje principal de la pantalla, y la HU
    // pide que al cambiar el mes cambien los datos). La categoría se escribe en el
    // borrador y recién impacta con "Aplicar filtros".
    mesSeleccionado: string;
    anioSeleccionado: number;

    categoriaBorrador: string = TODAS_LAS_CATEGORIAS;
    private categoriaAplicada: string = TODAS_LAS_CATEGORIAS;

    // Período resuelto. null = todavía no hay recaudación para ese mes (mes futuro).
    datos: RecaudacionPeriodo | null = null;

    // Tabla
    filas: PagoJugador[] = [];
    totalJugadores = PAGOS_JUGADORES.length;

    // Desglose por medio de pago
    desglose: DesgloseItem[] = [];
    pagosDisplay = signal('0');
    pagosDelDonut = 0;

    // Geometría y color del donut
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
        this.categoriaBorrador = String(categoria);
    }

    aplicarFiltros(): void {
        this.categoriaAplicada = this.categoriaBorrador;
        this.recalcular();
    }

    limpiarFiltros(): void {
        this.categoriaBorrador = TODAS_LAS_CATEGORIAS;
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

    formatMonto(valor: number): string {
        return CURRENCY_FULL.format(valor);
    }

    toggleExportMenu(): void {
        this.zone.run(() => (this.exportMenuOpen = !this.exportMenuOpen));
    }

    // Maqueta: avisa que todavía no se puede en vez de descargar un archivo
    // vacío o con los números inventados del mock.
    exportarRecaudacion(formato: 'pdf' | 'csv'): void {
        this.exportMenuOpen = false;
        this.notifications.notify(
            `La exportación a ${formato.toUpperCase()} va a estar disponible cuando el reporte se genere desde el servidor.`,
            'cancelled',
        );
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

    // Resuelve el período contra el mock y recalcula todo lo derivado. Es lo único
    // que hay que reemplazar por una llamada HTTP el día que exista el endpoint.
    private cargarPeriodo(): void {
        const mes = MESES.indexOf(this.mesSeleccionado) + 1;
        this.datos = obtenerPeriodo(this.anioSeleccionado, mes);
        this.recalcular();
    }

    private recalcular(): void {
        this.filas = filtrarJugadores(this.categoriaAplicada);
        this.cancelarAnimacion();

        if (!this.datos) {
            this.desglose = [];
            this.pagosDelDonut = 0;
            this.arcoDonut = 0;
            return;
        }

        const { total, efectivo, transferencia, pagos } = this.datos;

        // La cantidad de pagos se reparte en proporción a la plata de cada medio, y
        // el resto va al otro: así los dos números siempre suman el total de pagos
        // exacto, sin que un redondeo deje 149 de 150.
        const pagosEfectivo = total > 0 ? Math.round((pagos * efectivo) / total) : 0;
        const pagosTransferencia = pagos - pagosEfectivo;

        // Mismo criterio con los porcentajes: se redondea uno solo y el otro es el
        // complemento, para que la barra nunca muestre 39% + 60% = 99%.
        const porcentajeEfectivo = pagos > 0 ? Math.round((pagosEfectivo * 100) / pagos) : 0;
        const porcentajeTransferencia = 100 - porcentajeEfectivo;

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
        this.pagosDelDonut = pagos;

        this.cancelarAnimacion = animateCountUp(this.zone, [
            { target: this.pagosDelDonut, display: this.pagosDisplay },
        ]);
    }
}

// Año actual + los 2 anteriores, igual que en Cuotas y pagos
function buildYearOptions(): number[] {
    const actual = new Date().getFullYear();
    return [actual, actual - 1, actual - 2];
}

function categoriasDeLosJugadores(): string[] {
    return [...new Set(PAGOS_JUGADORES.map((j) => j.categoria))].sort();
}

function filtrarJugadores(categoria: string): PagoJugador[] {
    return PAGOS_JUGADORES.filter(
        (jugador) => categoria === TODAS_LAS_CATEGORIAS || jugador.categoria === categoria,
    );
}

// Un mes que todavía no llegó no tiene recaudación: mostrar un número inventado ahí
// haría que la pantalla mienta sobre el futuro.
function esFuturo(anio: number, mes: number): boolean {
    const hoy = new Date();
    return anio > hoy.getFullYear() || (anio === hoy.getFullYear() && mes > hoy.getMonth() + 1);
}

function obtenerPeriodo(anio: number, mes: number): RecaudacionPeriodo | null {
    const fijo = PERIODOS_FIJOS[`${anio}-${mes}`];
    if (fijo) {
        return fijo;
    }
    return esFuturo(anio, mes) ? null : generarPeriodo(anio, mes);
}

// Relleno determinístico para los meses que la HU no detalla: el mismo período
// siempre devuelve los mismos números, así la pantalla no "baila" al ir y volver
// entre meses mientras se la prueba.
function generarPeriodo(anio: number, mes: number): RecaudacionPeriodo {
    const semilla = anio * 12 + mes;

    const total = 3_200_000 + ((semilla * 137) % 24) * 100_000;
    const porcentajeEfectivo = 34 + ((semilla * 61) % 13);
    const efectivo = Math.round((total * porcentajeEfectivo) / 100 / 10_000) * 10_000;

    return {
        total,
        efectivo,
        transferencia: total - efectivo,
        pagos: 95 + ((semilla * 29) % 60),
    };
}
