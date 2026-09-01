import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { BaseChartDirective } from 'ng2-charts';
import { Chart, ChartConfiguration, registerables } from 'chart.js';

Chart.register(...registerables);

// One metric card in the KPI row
interface SummaryCard {
    value: string;
    label: string;
    detail: string;
    badge: string;
    badgeTone: 'positive' | 'negative' | 'neutral';
    progress: number;
    progressColor: string;
}

// One bar in the monthly revenue chart
interface MonthlyBar {
    month: string;
    amount: string;
    height: number;
    current: boolean;
}

// One row in the eligibility panel
interface EligibilityItem {
    tone: 'success' | 'warning' | 'danger';
    title: string;
    description: string;
    variation: string;
    variationTone: 'positive' | 'negative';
}

// One player in the recently suspended list
interface SuspendedPlayer {
    name: string;
    category: string;
    debtLabel: string;
}

@Component({
    selector: 'app-resumen-general',
    standalone: true,
    imports: [CommonModule, BaseChartDirective],
    templateUrl: './resumen-general.html',
    styleUrl: './resumen-general.css',
})
export class ResumenGeneral implements OnInit {

    // Banner
    currentDate: string = '';

    bannerStats = [
        { value: '126', label: 'Pagos del mes' },
        { value: '$4.2M', label: 'Recaudado 2026' },
        { value: '78%', label: 'Cuotas al día' },
    ];

    // KPI cards
    cards: SummaryCard[] = [
        {
            value: '577',
            label: 'Jugadores activos',
            detail: '12 dados de alta este mes',
            badge: '+5%',
            badgeTone: 'positive',
            progress: 78,
            progressColor: '#00a651',
        },
        {
            value: '512',
            label: 'Jugadores habilitados',
            detail: '65 con restricciones',
            badge: '89%',
            badgeTone: 'positive',
            progress: 89,
            progressColor: '#4cb863',
        },
        {
            value: '$388.500',
            label: 'Ingresado este mes',
            detail: '126 pagos registrados',
            badge: '-3%',
            badgeTone: 'negative',
            progress: 62,
            progressColor: '#8dd49d',
        },
        {
            value: '$1.200.000',
            label: 'Deuda acumulada',
            detail: '65 jugadores con cuotas impagas',
            badge: '65',
            badgeTone: 'neutral',
            progress: 28,
            progressColor: '#c0392b',
        },
    ];

    // Monthly revenue chart — heights are percentages of the tallest month
    monthlyRevenue: MonthlyBar[] = [
        { month: 'Ene', amount: '$380k', height: 68, current: false },
        { month: 'Feb', amount: '$420k', height: 75, current: false },
        { month: 'Mar', amount: '$510k', height: 91, current: false },
        { month: 'Abr', amount: '$490k', height: 87, current: false },
        { month: 'May', amount: '$530k', height: 95, current: false },
        { month: 'Jun', amount: '$480k', height: 86, current: false },
        { month: 'Jul', amount: '$560k', height: 100, current: false },
        { month: 'Ago', amount: '$389k', height: 69, current: true },
    ];

    // Squad eligibility breakdown
    eligibility: EligibilityItem[] = [
        {
            tone: 'success',
            title: '512 habilitados',
            description: 'Al día con las cuotas',
            variation: '+3%',
            variationTone: 'positive',
        },
        {
            tone: 'warning',
            title: '41 habilitación parcial',
            description: '1 cuota impaga — no juegan',
            variation: '-1%',
            variationTone: 'negative',
        },
        {
            tone: 'danger',
            title: '24 inhabilitados',
            description: '2 o más cuotas impagas',
            variation: '-2%',
            variationTone: 'negative',
        },
    ];

    // ===== ELIGIBILITY TREND CHART =====
    // Highlight the last point (current month) with a bigger solid dot
    private readonly trendPointColors = [
        '#ffffff', '#ffffff', '#ffffff', '#ffffff', '#ffffff', '#ffffff', '#00a651',
    ];
    private readonly trendPointRadius = [4, 4, 4, 4, 4, 4, 6];

    trendData: ChartConfiguration<'line'>['data'] = {
        labels: ['Ene', 'Feb', 'Mar', 'Abr', 'May', 'Jun', 'Ago'],
        datasets: [
            {
                data: [74, 77, 79, 81, 83, 86, 89],
                borderColor: '#00a651',
                borderWidth: 2.5,
                fill: true,
                backgroundColor: 'rgba(231, 244, 234, 0.4)',
                pointBackgroundColor: this.trendPointColors,
                pointBorderColor: '#00a651',
                pointBorderWidth: 2,
                pointRadius: this.trendPointRadius,
                pointHoverRadius: 6,
                tension: 0.35,
            },
        ],
    };

    trendOptions: ChartConfiguration<'line'>['options'] = {
        responsive: true,
        maintainAspectRatio: false,
        // Points reveal from left to right as each one is delayed by its index
        animation: {
            duration: 800,
            easing: 'easeOutQuart',
            delay: (context) =>
                context.type === 'data' && context.mode === 'default'
                    ? context.dataIndex * 90
                    : 0,
        },
        layout: {
            padding: { top: 4, right: 4 },
        },
        plugins: {
            legend: { display: false },
            tooltip: {
                backgroundColor: '#000000',
                titleColor: '#ffffff',
                bodyColor: '#ffffff',
                borderWidth: 0,
                displayColors: false,
                callbacks: {
                    label: (context) => `${context.parsed.y}%`,
                },
            },
        },
        scales: {
            x: {
                grid: { display: false },
                border: { display: false },
                ticks: {
                    color: '#555a55',
                    font: (context) => ({
                        size: 10,
                        // Current month label stands out
                        weight: context.index === 6 ? 'bold' : 'normal',
                    }),
                },
            },
            y: {
                min: 60,
                max: 100,
                grid: { color: '#f1f5f1' },
                border: { display: false },
                ticks: {
                    color: '#555a55',
                    font: { size: 10 },
                    stepSize: 10,
                    callback: (value) => `${value}%`,
                },
            },
        },
    };

    // Mock data until the endpoint is available
    suspendedPlayers: SuspendedPlayer[] = [
        { name: 'Sánchez, Bautista D.', category: 'Pt preAFA 2015', debtLabel: '3 cuotas' },
        { name: 'Correas, Juan V.', category: 'Pt preAFA 2015', debtLabel: '3 cuotas' },
        { name: 'Romero Moreira, D.', category: 'Pt preAFA 2015', debtLabel: '2 cuotas' },
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
    }

    // Builds a Spanish long date without depending on locale registration
    private formatToday(): string {
        const today = new Date();
        const day = this.dayNames[today.getDay()];
        const month = this.monthNames[today.getMonth()];
        return `${day} ${today.getDate()} de ${month} de ${today.getFullYear()}`;
    }
}
