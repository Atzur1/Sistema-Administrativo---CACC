import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { forkJoin } from 'rxjs';
import { PagosService, PendienteJugador, ResumenPagos } from '../../services/pagos';
import { formatCompactCurrency } from '../../shared/format-currency';

// One row in the debtors ranking table
interface DebtorRow {
    rank: number;
    player: string;
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

// One bar in the "distribución por cuotas adeudadas" panel
interface DistributionBar {
    label: string;
    players: number;
    width: number;
    color: string;
}

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
    imports: [CommonModule],
    templateUrl: './deudas-morosidad.html',
    styleUrl: './deudas-morosidad.css',
})
export class DeudasMorosidad implements OnInit {
    cargando = true;

    // Banner
    bannerMetrics: BannerMetric[] = [
        { value: '—', label: 'Deuda total' },
        { value: '—', label: 'Morosos' },
        { value: '—', label: 'Inhabilitados' },
    ];

    // Debtors ranked by outstanding amount (ya viene ordenado desc. por monto desde el backend)
    debtors: DebtorRow[] = [];

    // Jugadores inhabilitados (2+ cuotas), los de mayor deuda primero
    topInhabilitados: DebtorHighlight[] = [];

    // Debt age distribution
    distribution: DistributionBar[] = [];

    constructor(private pagosService: PagosService, private cdr: ChangeDetectorRef) {}

    ngOnInit(): void {
        forkJoin({
            pendientes: this.pagosService.getPendientes(),
            resumen: this.pagosService.getResumen(),
        }).subscribe({
            next: ({ pendientes, resumen }) => {
                this.debtors = pendientes.map(mapDebtorRow);
                this.topInhabilitados = pendientes
                    .filter((p) => p.cantidadCuotas >= CUOTAS_PARA_INHABILITAR)
                    .slice(0, 5)
                    .map(mapHighlight);
                this.distribution = buildDistribution(pendientes);
                this.cargando = false;
                this.animateBannerMetrics(buildBannerTargets(resumen, pendientes));
            },
            error: () => {
                this.cargando = false;
                this.cdr.detectChanges();
            },
        });
    }

    // The first three ranks are highlighted in the table
    isTopRank(rank: number): boolean {
        return rank <= 3;
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

// Agrupa a los morosos por cantidad de cuotas impagas, en los mismos 3 baldes que ya usaba
// el mock: 1 cuota / 2 cuotas / 3+. El ancho de cada barra es la proporción de morosos que
// cae en ese balde (no un valor fijo hardcodeado).
function buildDistribution(pendientes: PendienteJugador[]): DistributionBar[] {
    let unaCuota = 0;
    let dosCuotas = 0;
    let tresOMas = 0;

    for (const p of pendientes) {
        if (p.cantidadCuotas <= 1) unaCuota++;
        else if (p.cantidadCuotas === 2) dosCuotas++;
        else tresOMas++;
    }

    const total = pendientes.length || 1;
    return [
        { label: '1 cuota', players: unaCuota, width: (unaCuota / total) * 100, color: '#8dd49d' },
        { label: '2 cuotas', players: dosCuotas, width: (dosCuotas / total) * 100, color: '#eba83a' },
        { label: '3+ cuotas', players: tresOMas, width: (tresOMas / total) * 100, color: 'var(--color-danger)' },
    ];
}

function buildBannerTargets(resumen: ResumenPagos, pendientes: PendienteJugador[]): BannerMetricTarget[] {
    const inhabilitados = pendientes.filter((p) => p.cantidadCuotas >= CUOTAS_PARA_INHABILITAR).length;
    return [
        { label: 'Deuda total', raw: resumen.deudaGlobalTotal, format: formatCompactCurrency },
        { label: 'Morosos', raw: resumen.jugadoresMorosos, format: (n) => String(n) },
        { label: 'Inhabilitados', raw: inhabilitados, format: (n) => String(n) },
    ];
}
