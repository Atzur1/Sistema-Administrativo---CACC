import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

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

// One player who crossed the two-instalment threshold this month
interface NewlySuspended {
    initials: string;
    player: string;
    detail: string;
    amount: string;
}

// One bar in the debt-age distribution panel
interface DistributionBar {
    label: string;
    players: number;
    width: number;
    color: string;
}

@Component({
    selector: 'app-deudas-morosidad',
    standalone: true,
    imports: [CommonModule],
    templateUrl: './deudas-morosidad.html',
    styleUrl: './deudas-morosidad.css',
})
export class DeudasMorosidad {

    // Banner
    bannerMetrics = [
        { value: '$1.2M', label: 'Deuda total' },
        { value: '65', label: 'Morosos' },
        { value: '24', label: 'Inhabilitados' },
    ];

    // Debtors ranked by outstanding amount
    debtors: DebtorRow[] = [
        {
            rank: 1,
            player: 'Sánchez, Bautista D.',
            category: 'Pt preAFA 2015',
            instalments: '3 cuotas',
            debt: '$255.000',
            status: 'suspended',
            statusLabel: 'Inhabilitado',
        },
        {
            rank: 2,
            player: 'Correas, Juan V.',
            category: 'Pt preAFA 2015',
            instalments: '3 cuotas',
            debt: '$255.000',
            status: 'suspended',
            statusLabel: 'Inhabilitado',
        },
        {
            rank: 3,
            player: 'Romero Moreira, D.',
            category: 'Pt preAFA 2015',
            instalments: '2 cuotas',
            debt: '$170.000',
            status: 'suspended',
            statusLabel: 'Inhabilitado',
        },
        {
            rank: 4,
            player: 'Pérez, Nehemías J.',
            category: 'Pt preAFA 2015',
            instalments: '2 cuotas',
            debt: '$170.000',
            status: 'suspended',
            statusLabel: 'Inhabilitado',
        },
        {
            rank: 5,
            player: 'Guzmán, Tomás V.',
            category: 'Pt AFA 2012',
            instalments: '1 cuota',
            debt: '$85.000',
            status: 'partial',
            statusLabel: 'Parcial',
        },
        {
            rank: 6,
            player: 'Aliendro, Brian E.',
            category: 'Pt AFA 2011',
            instalments: '1 cuota',
            debt: '$85.000',
            status: 'partial',
            statusLabel: 'Parcial',
        },
        {
            rank: 7,
            player: 'Baigorrí, Ángel A.',
            category: 'Pt AFA 2010',
            instalments: '1 cuota',
            debt: '$85.000',
            status: 'partial',
            statusLabel: 'Parcial',
        },
    ];

    // Players suspended during the current month
    newlySuspended: NewlySuspended[] = [
        {
            initials: 'SB',
            player: 'Sánchez, Bautista D.',
            detail: 'Pt preAFA 2015 · 3 cuotas',
            amount: '$255k',
        },
        {
            initials: 'CJ',
            player: 'Correas, Juan V.',
            detail: 'Pt preAFA 2015 · 3 cuotas',
            amount: '$255k',
        },
        {
            initials: 'RD',
            player: 'Romero Moreira, D.',
            detail: 'Pt preAFA 2015 · 2 cuotas',
            amount: '$170k',
        },
    ];

    // Debt age distribution — width is the share of the panel each bar fills
    distribution: DistributionBar[] = [
        { label: '1 cuota', players: 41, width: 63, color: '#8dd49d' },
        { label: '2 cuotas', players: 18, width: 28, color: '#eba83a' },
        { label: '3+ cuotas', players: 6, width: 12, color: '#c0392b' },
    ];

    // The first three ranks are highlighted in the table
    isTopRank(rank: number): boolean {
        return rank <= 3;
    }
}
