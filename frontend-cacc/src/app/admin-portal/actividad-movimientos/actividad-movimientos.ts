import {
    Component,
    OnInit,
    NgZone,
    DestroyRef,
    inject,
    signal,
    WritableSignal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { FormBuilder, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { timeout } from 'rxjs';
import { animateCountUp } from '../../shared/count-up';
import { formatElapsedPrecise } from '../../shared/format-elapsed';
import { normalizeText } from '../../shared/normalize-text';
import { CustomSelect } from '../../shared/custom-select/custom-select';
import { PagoReciente, PagosService } from '../../services/pagos';

// One row in the "Pagos registrados" table
interface PaymentRow {
    id: number;
    idJugador: number;
    player: string;
    category: string;
    period: string;
    amount: string;
    method: string;
    status: 'Pagado' | 'Parcial';
    responsable: string;
    registrado: string;
}

// One entry in the recent activity feed. Only payment/partial: those are the only
// events this screen has a real, timestamped source for (see PagosDao.ObtenerUltimosPagos).
// A player owing money or getting suspended is a computed STATE, not a discrete event with
// a moment it happened, so it does not belong in a feed sorted by recency.
interface ActivityEvent {
    type: 'payment' | 'partial';
    title: string;
    badge: string;
    elapsed: string;
}

// One count-up metric shown on the banner.
// `display` is a signal so the template updates through Angular's normal
// change-detection flow when the animation writes to it.
interface BannerMetric {
    target: number;
    prefix: string;
    suffix: string;
    label: string;
    display: WritableSignal<string>;
}

// One shortcut button in the quick access panel
interface QuickAction {
    label: string;
    icon: 'card' | 'chart';
    link: string;
}

const CURRENCY_FULL = new Intl.NumberFormat('es-AR', {
    style: 'currency',
    currency: 'ARS',
    maximumFractionDigits: 0,
});

@Component({
    selector: 'app-actividad-movimientos',
    standalone: true,
    imports: [CommonModule, RouterLink, ReactiveFormsModule, CustomSelect],
    templateUrl: './actividad-movimientos.html',
    styleUrl: './actividad-movimientos.css',
})
export class ActividadMovimientos implements OnInit {

    private zone = inject(NgZone);
    private destroyRef = inject(DestroyRef);
    private pagosService = inject(PagosService);
    private router = inject(Router);
    private fb = inject(FormBuilder);

    // Cancels the running count-up animation, so it can be cancelled on destroy.
    private cancelCountUp: () => void = () => {};

    // Banner
    currentDate: string = '';

    // Targets start at 0 and get the real "hoy" numbers once the API answers —
    // animateCountUp only runs after that, so it never counts up to a fake number.
    bannerMetrics: BannerMetric[] = [
        { target: 0, prefix: '', suffix: '', label: 'Pagos hoy', display: signal('0') },
        { target: 0, prefix: '$', suffix: '', label: 'Recaudado hoy', display: signal('$0') },
    ];

    // Todos los pagos registrados, sin límite — filtrados/paginados del lado del cliente
    // (mismo criterio que Becados y Descuentos).
    private allPayments: PaymentRow[] = [];
    paymentsLoaded = false;
    paymentsLoadError = false;

    // Recent activity feed: solo los últimos, no la lista completa de arriba.
    activity: ActivityEvent[] = [];
    activityLoaded = false;
    activityLoadError = false;

    filterForm: FormGroup;
    estadoOptions: { value: PaymentRow['status']; label: string }[] = [
        { value: 'Pagado', label: 'Pagado' },
        { value: 'Parcial', label: 'Parcial' },
    ];

    page = 1;
    readonly pageSize = 10;

    // Shortcuts to the related dashboards
    quickActions: QuickAction[] = [
        { label: 'Registrar pago', icon: 'card', link: '/admin/portal/cuotas-pagos' },
        { label: 'Reportes', icon: 'chart', link: '/admin/portal/reportes' },
    ];

    private monthNames = [
        'enero', 'febrero', 'marzo', 'abril', 'mayo', 'junio',
        'julio', 'agosto', 'septiembre', 'octubre', 'noviembre', 'diciembre',
    ];

    private dayNames = [
        'domingo', 'lunes', 'martes', 'miércoles', 'jueves', 'viernes', 'sábado',
    ];

    constructor() {
        this.filterForm = this.fb.group({
            busqueda: [''],
            estado: [null as PaymentRow['status'] | null],
        });

        // Cualquier cambio de filtro vuelve a la página 1, igual que en Becados y Descuentos.
        this.filterForm.valueChanges.subscribe(() => (this.page = 1));
    }

    ngOnInit() {
        this.currentDate = this.formatToday();
        this.destroyRef.onDestroy(() => this.cancelCountUp());

        this.loadResumenHoy();
        this.loadActivity();
        this.loadPagosRegistrados();
    }

    irAJugador(idJugador: number) {
        this.router.navigate(['/admin/portal/jugadores', idJugador]);
    }

    // ===== FILTROS Y PAGINACIÓN (mismo patrón que Becados y Descuentos) =====

    get allPaymentsCount(): number {
        return this.allPayments.length;
    }

    get filteredPayments(): PaymentRow[] {
        const { busqueda, estado } = this.filterForm.value;
        const term = normalizeText((busqueda ?? '').trim());

        return this.allPayments.filter((row) => {
            if (estado && row.status !== estado) {
                return false;
            }
            if (term && !normalizeText(row.player).includes(term)) {
                return false;
            }
            return true;
        });
    }

    get totalPaginas(): number {
        return Math.max(1, Math.ceil(this.filteredPayments.length / this.pageSize));
    }

    get pagedPayments(): PaymentRow[] {
        const start = (this.page - 1) * this.pageSize;
        return this.filteredPayments.slice(start, start + this.pageSize);
    }

    get hayFiltrosActivos(): boolean {
        const { busqueda, estado } = this.filterForm.value;
        return !!(busqueda ?? '').trim() || !!estado;
    }

    limpiarFiltros() {
        this.filterForm.reset({ busqueda: '', estado: null });
    }

    irAPagina(nuevaPagina: number) {
        if (nuevaPagina < 1 || nuevaPagina > this.totalPaginas || nuevaPagina === this.page) {
            return;
        }
        this.page = nuevaPagina;
    }

    // ===== DATA LOADING =====

    private loadResumenHoy() {
        this.pagosService.getResumenHoy().pipe(timeout(ActividadMovimientos.LOAD_TIMEOUT_MS)).subscribe({
            next: (resumen) => {
                this.bannerMetrics[0].target = resumen.pagosHoy;
                this.bannerMetrics[1].target = Math.round(resumen.recaudadoHoy);
                this.startCountUp();
            },
            // Sin datos, el banner se queda en 0 en vez de mostrar algo fabricado.
            error: () => this.startCountUp(),
        });
    }

    private startCountUp() {
        this.cancelCountUp();
        this.cancelCountUp = animateCountUp(
            this.zone,
            this.bannerMetrics.map((metric) => ({
                target: metric.target,
                display: metric.display,
                format: (value: number) =>
                    metric.prefix === '$'
                        ? CURRENCY_FULL.format(value)
                        : `${metric.prefix}${value}${metric.suffix}`,
            }))
        );
    }

    // Todo pedido acá lleva timeout: sin esto, si el servidor se queda esperando un lock
    // (ej. una transacción de cobro a mitad de camino), la pantalla se queda en blanco para
    // siempre en vez de mostrar el estado de error con "Reintentar".
    private static readonly LOAD_TIMEOUT_MS = 15000;

    private loadActivity() {
        this.activityLoadError = false;
        // Solo los más recientes: el feed muestra un puñado de eventos, no la lista entera.
        this.pagosService.getRecientes(9).pipe(timeout(ActividadMovimientos.LOAD_TIMEOUT_MS)).subscribe({
            next: (recientes) => {
                this.activity = recientes.map(toActivityEvent);
                this.activityLoaded = true;
            },
            error: () => {
                this.activity = [];
                this.activityLoaded = true;
                this.activityLoadError = true;
            },
        });
    }

    reintentarActividad() {
        this.loadActivity();
    }

    private loadPagosRegistrados() {
        this.paymentsLoadError = false;
        this.pagosService.getPagosRegistrados().pipe(timeout(ActividadMovimientos.LOAD_TIMEOUT_MS)).subscribe({
            next: (pagos) => {
                this.allPayments = pagos.map(toPaymentRow);
                this.paymentsLoaded = true;
            },
            error: () => {
                this.allPayments = [];
                this.paymentsLoaded = true;
                this.paymentsLoadError = true;
            },
        });
    }

    reintentarPagos() {
        this.loadPagosRegistrados();
    }

    // Builds a Spanish long date without depending on locale registration
    private formatToday(): string {
        const today = new Date();
        const day = this.dayNames[today.getDay()];
        const month = this.monthNames[today.getMonth()];
        return `${day} ${today.getDate()} de ${month} de ${today.getFullYear()}`;
    }
}

function toPaymentRow(p: PagoReciente): PaymentRow {
    return {
        id: p.idPago,
        idJugador: p.idJugador,
        player: p.nombreCompleto,
        category: p.categoria,
        period: p.periodo,
        amount: CURRENCY_FULL.format(p.monto),
        method: p.metodoPago,
        status: p.estado,
        responsable: [p.responsableNombre, p.responsableApellido].filter(Boolean).join(' ') || 'Sin registro histórico',
        registrado: p.fechaHoraRegistro || '',
    };
}

function toActivityEvent(p: PagoReciente): ActivityEvent {
    const esParcial = p.estado === 'Parcial';
    return {
        type: esParcial ? 'partial' : 'payment',
        title: `${esParcial ? 'Pago parcial' : 'Pago registrado'}: ${p.nombreCompleto}`,
        badge: `${CURRENCY_FULL.format(p.monto)} · ${p.metodoPago}`,
        elapsed: formatElapsedPrecise(p.fechaHoraRegistro, p.fechaPago),
    };
}
