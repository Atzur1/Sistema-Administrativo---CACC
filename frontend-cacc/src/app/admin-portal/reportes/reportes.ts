import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

// Drives the bar color in the monthly revenue chart
type BarTone = 'empty' | 'loss' | 'low' | 'best' | 'current' | 'normal';

// One column of the monthly revenue chart
interface MonthlyRevenue {
    month: string;
    // Months of the season that have not been collected yet carry no amount
    amount: number | null;
    label: string;
    heightPercent: number;
    tone: BarTone;
}

// One horizontal bar in the unpaid fees panel
interface UnpaidMonth {
    month: string;
    count: number;
    widthPercent: number;
}

// One card in the average and trend panel
interface TrendCard {
    icon: 'average' | 'best' | 'low';
    value: string;
    label: string;
}

@Component({
    selector: 'app-reportes',
    standalone: true,
    imports: [CommonModule],
    templateUrl: './reportes.html',
    styleUrl: './reportes.css',
})
export class Reportes {

    // Header
    headerMetrics = [
        { value: '$4.2M', label: 'Recaudado 2026' },
        { value: '65', label: 'Cuotas impagas' },
    ];

    // Season in progress: only the first eight months have been collected
    private readonly currentMonth = 'Ago';

    private readonly rawRevenue: { month: string; amount: number | null }[] = [
        { month: 'Ene', amount: 380000 },
        { month: 'Feb', amount: 420000 },
        { month: 'Mar', amount: 510000 },
        { month: 'Abr', amount: 490000 },
        { month: 'May', amount: 530000 },
        { month: 'Jun', amount: 480000 },
        { month: 'Jul', amount: 560000 },
        { month: 'Ago', amount: 389000 },
        { month: 'Sep', amount: null },
        { month: 'Oct', amount: null },
        { month: 'Nov', amount: null },
        { month: 'Dic', amount: null },
    ];

    revenue: MonthlyRevenue[] = [];

    // Where the dashed average line sits, as a percentage of the chart height
    averageLinePercent = 0;

    // Unpaid fees per period
    unpaidTotal = 65;

    // Full chronological view of the season so far, so the trend of unpaid
    // fees building up towards the current month is visible at a glance
    unpaidMonths: UnpaidMonth[] = [
        { month: 'Ene', count: 1, widthPercent: 3 },
        { month: 'Feb', count: 1, widthPercent: 3 },
        { month: 'Mar', count: 2, widthPercent: 5 },
        { month: 'Abr', count: 2, widthPercent: 5 },
        { month: 'May', count: 3, widthPercent: 8 },
        { month: 'Jun', count: 8, widthPercent: 21 },
        { month: 'Jul', count: 9, widthPercent: 23 },
        { month: 'Ago', count: 39, widthPercent: 100 },
    ];

    // Built from the same figures as the chart, so the panel can never
    // disagree with the bars it is summarising
    trendCards: TrendCard[] = [];

    // Short labels are what the chart axis shows; the cards spell them out
    private readonly monthNames: Record<string, string> = {
        Ene: 'Enero', Feb: 'Febrero', Mar: 'Marzo', Abr: 'Abril',
        May: 'Mayo', Jun: 'Junio', Jul: 'Julio', Ago: 'Agosto',
        Sep: 'Septiembre', Oct: 'Octubre', Nov: 'Noviembre', Dic: 'Diciembre',
    };

    constructor() {
        this.buildChart();
    }

    // Derives bar heights, tones and the average line from the raw amounts,
    // so the chart stays correct if a month is added or corrected later
    private buildChart() {
        const collected = this.rawRevenue.filter(entry => entry.amount !== null);
        const amounts = collected.map(entry => entry.amount as number);

        const max = Math.max(...amounts);
        const min = Math.min(...amounts);
        const average = amounts.reduce((sum, value) => sum + value, 0) / amounts.length;

        this.revenue = this.rawRevenue.map(entry => {
            if (entry.amount === null) {
                // Months still to come keep a stub bar so the axis stays even
                return {
                    month: entry.month,
                    amount: null,
                    label: '—',
                    heightPercent: 3,
                    tone: 'empty' as BarTone,
                };
            }

            return {
                month: entry.month,
                amount: entry.amount,
                label: this.toShortAmount(entry.amount),
                heightPercent: (entry.amount / max) * 100,
                tone: this.resolveTone(entry.month, entry.amount, min, max),
            };
        });

        this.averageLinePercent = (average / max) * 100;

        const bestMonth = collected.find(entry => entry.amount === max)!;
        const lowestMonth = collected.find(entry => entry.amount === min)!;

        this.trendCards = [
            {
                icon: 'average',
                value: this.toAmount(Math.round(average)),
                label: `Promedio mensual (${collected.length} meses)`,
            },
            {
                icon: 'best',
                value: `${this.monthNames[bestMonth.month]} — ${this.toAmount(max)}`,
                label: 'Mejor mes del año',
            },
            {
                icon: 'low',
                value: `${this.monthNames[lowestMonth.month]} — ${this.toAmount(min)}`,
                label: 'Mes más bajo del año',
            },
        ];
    }

    // 469875 -> "$469.875"
    private toAmount(amount: number): string {
        return `$${amount.toLocaleString('es-AR')}`;
    }

    // Precedence: January is flagged as a loss on sight regardless of where it
    // ranks, then the single weakest month, then the single best one, then
    // the current month gets its own colour — so a month in progress never
    // reads as a top performer just because it shares the "best" green
    private resolveTone(month: string, amount: number, min: number, max: number): BarTone {
        if (month === 'Ene') {
            return 'loss';
        }
        if (amount === min) {
            return 'low';
        }
        if (amount === max) {
            return 'best';
        }
        if (month === this.currentMonth) {
            return 'current';
        }
        return 'normal';
    }

    // 380000 -> "$380k"
    private toShortAmount(amount: number): string {
        return `$${Math.round(amount / 1000)}k`;
    }
}
