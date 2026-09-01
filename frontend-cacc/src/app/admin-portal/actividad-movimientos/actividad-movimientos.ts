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
import { RouterLink } from '@angular/router';

// One row in the latest payments table
interface PaymentRow {
    player: string;
    category: string;
    period: string;
    amount: string;
    method: string;
    status: 'paid' | 'partial';
    statusLabel: string;
}

// One entry in the recent activity feed
interface ActivityEvent {
    type: 'payment' | 'partial' | 'debt' | 'suspended';
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

@Component({
    selector: 'app-actividad-movimientos',
    standalone: true,
    imports: [CommonModule, RouterLink],
    templateUrl: './actividad-movimientos.html',
    styleUrl: './actividad-movimientos.css',
})
export class ActividadMovimientos implements OnInit {

    private zone = inject(NgZone);
    private destroyRef = inject(DestroyRef);

    // Pending animation frame id, so it can be cancelled on destroy.
    private rafId = 0;

    // Banner
    currentDate: string = '';

    // display is animated from 0 to target in ngOnInit
    bannerMetrics: BannerMetric[] = [
        { target: 12, prefix: '', suffix: '', label: 'Pagos hoy', display: signal('0') },
        { target: 892, prefix: '$', suffix: 'k', label: 'Recaudado hoy', display: signal('$0k') },
    ];

    // Latest registered payments
    payments: PaymentRow[] = [
        {
            player: 'Acosta, Ciro F.',
            category: 'Pt preAFA 2015',
            period: 'Agosto 2026',
            amount: '$85.000',
            method: 'Transferencia',
            status: 'paid',
            statusLabel: 'Pagado',
        },
        {
            player: 'Baigorrí, Ángel A.',
            category: 'Pt preAFA 2015',
            period: 'Agosto 2026',
            amount: '$85.000',
            method: 'Efectivo',
            status: 'paid',
            statusLabel: 'Pagado',
        },
        {
            player: 'Guzmán, Tomás V.',
            category: 'Pt AFA 2012',
            period: 'Julio 2026',
            amount: '$42.500',
            method: 'Transferencia',
            status: 'partial',
            statusLabel: 'Parcial',
        },
        {
            player: 'Aliendro, Brian E.',
            category: 'Pt AFA 2011',
            period: 'Agosto 2026',
            amount: '$85.000',
            method: 'Transferencia',
            status: 'paid',
            statusLabel: 'Pagado',
        },
        {
            player: 'López, Tobías A.',
            category: 'Pt AFA 2010',
            period: 'Agosto 2026',
            amount: '$85.000',
            method: 'Efectivo',
            status: 'paid',
            statusLabel: 'Pagado',
        },
        {
            player: 'Cuqueio, Juan Cruz',
            category: 'Pt preAFA 2015',
            period: 'Agosto 2026',
            amount: '$85.000',
            method: 'Transferencia',
            status: 'paid',
            statusLabel: 'Pagado',
        },
        {
            player: 'Battistín Argañarás, Julián A.',
            category: 'Pt preAFA 2015',
            period: 'Agosto 2026',
            amount: '$85.000',
            method: 'Efectivo',
            status: 'paid',
            statusLabel: 'Pagado',
        },
        {
            player: 'Juárez, Iker Jesús',
            category: 'Pt AFA 2009',
            period: 'Julio 2026',
            amount: '$72.858',
            method: 'Transferencia',
            status: 'partial',
            statusLabel: 'Parcial',
        },
        {
            player: 'Pereyra, Tomás Benjamín',
            category: 'Pt AFA 2008',
            period: 'Agosto 2026',
            amount: '$85.000',
            method: 'Transferencia',
            status: 'paid',
            statusLabel: 'Pagado',
        },
        {
            player: 'Nuñez Arroyo, Noha Elías',
            category: 'Pt preAFA 2015',
            period: 'Agosto 2026',
            amount: '$85.000',
            method: 'Efectivo',
            status: 'paid',
            statusLabel: 'Pagado',
        },
    ];

    // Recent activity feed
    activity: ActivityEvent[] = [
        {
            type: 'payment',
            title: 'Pago registrado: Acosta, Ciro F.',
            badge: '$85.000 · Transferencia',
            elapsed: 'Hace 5 minutos',
        },
        {
            type: 'payment',
            title: 'Pago registrado: Baigorrí, Ángel A.',
            badge: '$85.000 · Efectivo',
            elapsed: 'Hace 12 minutos',
        },
        {
            type: 'partial',
            title: 'Pago parcial: Guzmán, Tomás V.',
            badge: '$42.500 · 1 cuota pendiente',
            elapsed: 'Hace 38 minutos',
        },
        {
            type: 'debt',
            title: 'Deuda detectada: Pérez, Nehemías J.',
            badge: '1 cuota impaga · Habilitación parcial',
            elapsed: 'Hace 1 hora',
        },
        {
            type: 'suspended',
            title: 'Inhabilitado: Sánchez, Bautista D.',
            badge: '3 cuotas impagas',
            elapsed: 'Hace 1 hora 20 min',
        },
        {
            type: 'payment',
            title: 'Pago registrado: Cuqueio, Juan Cruz',
            badge: '$85.000 · Transferencia',
            elapsed: 'Hace 2 horas',
        },
        {
            type: 'payment',
            title: 'Pago registrado: Battistín Argañarás, Julián A.',
            badge: '$85.000 · Efectivo',
            elapsed: 'Hace 2 horas 15 min',
        },
        {
            type: 'debt',
            title: 'Deuda detectada: Juárez, Iker Jesús',
            badge: '1 cuota impaga · Habilitación parcial',
            elapsed: 'Hace 3 horas',
        },
        {
            type: 'payment',
            title: 'Pago registrado: Pereyra, Tomás Benjamín',
            badge: '$85.000 · Transferencia',
            elapsed: 'Hace 3 horas 30 min',
        },
    ];

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

    ngOnInit() {
        this.currentDate = this.formatToday();
        // Stop the running frame the moment the component is torn down.
        this.destroyRef.onDestroy(() => cancelAnimationFrame(this.rafId));
        this.animateBannerMetrics();
    }

    // Counts every banner metric up from 0 to its target over ~800ms with an
    // ease-out cubic curve. The loop runs outside the zone to keep zone.js
    // from tracking every animation frame; each frame writes to a signal, so
    // change detection still runs through Angular's normal flow.
    private animateBannerMetrics(): void {
        const duration = 800;
        const startTime = performance.now();

        this.zone.runOutsideAngular(() => {
            const tick = (now: number) => {
                const progress = Math.min((now - startTime) / duration, 1);
                const eased = 1 - Math.pow(1 - progress, 3);

                for (const metric of this.bannerMetrics) {
                    const current = Math.round(metric.target * eased);
                    metric.display.set(`${metric.prefix}${current}${metric.suffix}`);
                }

                if (progress < 1) {
                    this.rafId = requestAnimationFrame(tick);
                }
            };

            this.rafId = requestAnimationFrame(tick);
        });
    }

    // Builds a Spanish long date without depending on locale registration
    private formatToday(): string {
        const today = new Date();
        const day = this.dayNames[today.getDay()];
        const month = this.monthNames[today.getMonth()];
        return `${day} ${today.getDate()} de ${month} de ${today.getFullYear()}`;
    }
}
