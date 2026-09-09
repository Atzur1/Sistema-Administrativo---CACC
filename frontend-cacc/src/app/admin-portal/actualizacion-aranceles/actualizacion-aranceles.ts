import { Component, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AbstractControl, FormBuilder, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { BaseChartDirective } from 'ng2-charts';
import { Chart, ChartConfiguration, registerables } from 'chart.js';

Chart.register(...registerables);

// One row in the "Historial y aranceles programados" table
interface FeeRow {
    id: number;
    // 'male' renders the "Masculino" pill, 'female' the "Femenino" one
    category: 'male' | 'female';
    categoryLabel: string;
    amount: string;
    // Stored as ISO so the dates compare and sort directly
    validFrom: string;
    // Empty while the fee has no replacement scheduled after it
    validTo: string;
}

@Component({
    selector: 'app-actualizacion-aranceles',
    standalone: true,
    imports: [CommonModule, ReactiveFormsModule, BaseChartDirective],
    templateUrl: './actualizacion-aranceles.html',
    styleUrl: './actualizacion-aranceles.css',
})
export class ActualizacionAranceles implements OnDestroy {

    // Header
    headerMetrics = [
        { value: '$85.000', label: 'Arancel masculino' },
        { value: '$50.000', label: 'Arancel femenino' },
        { value: 'En 15 días', label: 'Próximo cambio' },
    ];

    // Scheduling form
    feeForm: FormGroup;

    categories = ['Masculino', 'Femenino'];

    // Inline confirmation shown after a simulated submit
    successMessage = '';
    successLeaving = false;

    // Every fee ever set, newest first: past periods, the current one and the
    // changes already scheduled ahead
    feeRows: FeeRow[] = [
        { id: 1, category: 'female', categoryLabel: 'Femenino', amount: '$58.000', validFrom: '2026-09-24', validTo: '' },
        { id: 2, category: 'male', categoryLabel: 'Masculino', amount: '$92.000', validFrom: '2026-09-24', validTo: '' },
        { id: 3, category: 'male', categoryLabel: 'Masculino', amount: '$85.000', validFrom: '2026-04-01', validTo: '2026-09-23' },
        { id: 4, category: 'female', categoryLabel: 'Femenino', amount: '$50.000', validFrom: '2026-01-01', validTo: '2026-09-23' },
        { id: 5, category: 'male', categoryLabel: 'Masculino', amount: '$70.000', validFrom: '2026-01-01', validTo: '2026-03-31' },
        { id: 6, category: 'female', categoryLabel: 'Femenino', amount: '$42.000', validFrom: '2025-07-01', validTo: '2025-12-31' },
        { id: 7, category: 'male', categoryLabel: 'Masculino', amount: '$58.000', validFrom: '2025-07-01', validTo: '2025-12-31' },
        { id: 8, category: 'male', categoryLabel: 'Masculino', amount: '$45.000', validFrom: '2025-01-01', validTo: '2025-06-30' },
    ];

    // Bound to the date field so the native picker already blocks the past
    readonly today = new Date().toISOString().slice(0, 10);

    private monthLabels = [
        'Ene', 'Feb', 'Mar', 'Abr', 'May', 'Jun',
        'Jul', 'Ago', 'Sep', 'Oct', 'Nov', 'Dic',
    ];

    // ===== FEE EVOLUTION CHART =====
    // Both series are read off the history above, so the chart can never drift
    // away from the table
    feeChartData: ChartConfiguration<'line'>['data'] = { labels: [], datasets: [] };

    feeChartOptions: ChartConfiguration<'line'>['options'] = {
        responsive: true,
        maintainAspectRatio: false,
        interaction: { mode: 'index', intersect: false },
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
            // Two series here, so unlike the resumen-general chart this one
            // needs a legend to tell them apart
            legend: {
                display: true,
                position: 'bottom',
                labels: {
                    color: '#555a55',
                    font: { size: 11 },
                    boxWidth: 10,
                    boxHeight: 10,
                    usePointStyle: true,
                    pointStyle: 'circle',
                    padding: 14,
                },
            },
            tooltip: {
                backgroundColor: '#000000',
                titleColor: '#ffffff',
                bodyColor: '#ffffff',
                borderWidth: 0,
                callbacks: {
                    label: (context) =>
                        ` ${context.dataset.label}: ${this.formatAmount(Number(context.parsed.y))}`,
                },
            },
        },
        scales: {
            x: {
                grid: { display: false },
                border: { display: false },
                ticks: {
                    color: '#555a55',
                    font: { size: 10 },
                },
            },
            y: {
                grid: { color: '#f1f5f1' },
                border: { display: false },
                ticks: {
                    color: '#555a55',
                    font: { size: 10 },
                    callback: (value) => this.formatAmount(Number(value)),
                },
            },
        },
    };

    // Timers for the confirmation message, cleared on destroy so leaving the
    // dashboard mid-animation never fires a callback on a dead component
    private fadeTimer?: ReturnType<typeof setTimeout>;
    private clearTimer?: ReturnType<typeof setTimeout>;

    constructor(private fb: FormBuilder) {
        this.feeForm = this.fb.group({
            category: ['', [Validators.required]],
            amount: ['', [Validators.required, Validators.min(1)]],
            validFrom: ['', [Validators.required, this.notInThePastValidator]],
        });

        this.feeChartData = this.buildChartData();
    }

    // A fee can start today or later, never in a month already invoiced
    private notInThePastValidator = (control: AbstractControl): ValidationErrors | null => {
        const value = (control.value ?? '').toString();
        if (!value) {
            return null;
        }
        return value >= this.today ? null : { pastDate: true };
    };

    // The error only surfaces once the user has actually been on the field
    get validFromHasPastError(): boolean {
        const control = this.feeForm.controls['validFrom'];
        return control.hasError('pastDate') && (control.touched || control.dirty);
    }

    // Series for both categories, from January up to the current month, priced
    // with the fee that was in force on the first day of each month
    private buildChartData(): ChartConfiguration<'line'>['data'] {
        const now = new Date();
        const year = now.getFullYear();
        const monthCount = now.getMonth() + 1;

        const monthlyAmounts = (category: 'male' | 'female') =>
            Array.from({ length: monthCount }, (_, index) => {
                const reference = `${year}-${String(index + 1).padStart(2, '0')}-01`;
                const row = this.feeRows.find(fee =>
                    fee.category === category &&
                    fee.validFrom <= reference &&
                    (!fee.validTo || reference <= fee.validTo)
                );
                return row ? this.parseAmount(row.amount) : null;
            });

        return {
            labels: this.monthLabels.slice(0, monthCount),
            datasets: [
                {
                    label: 'Masculino',
                    data: monthlyAmounts('male'),
                    borderColor: '#3b82f6',
                    borderWidth: 2.5,
                    fill: false,
                    pointBackgroundColor: '#ffffff',
                    pointBorderColor: '#3b82f6',
                    pointBorderWidth: 2,
                    pointRadius: 4,
                    pointHoverRadius: 6,
                    tension: 0.35,
                },
                {
                    label: 'Femenino',
                    data: monthlyAmounts('female'),
                    borderColor: '#8b5cf6',
                    borderWidth: 2.5,
                    fill: false,
                    pointBackgroundColor: '#ffffff',
                    pointBorderColor: '#8b5cf6',
                    pointBorderWidth: 2,
                    pointRadius: 4,
                    pointHoverRadius: 6,
                    tension: 0.35,
                },
            ],
        };
    }

    // '$85.000' -> 85000
    private parseAmount(amount: string): number {
        return Number(amount.replace(/\D/g, ''));
    }

    // 85000 -> '$85.000'
    private formatAmount(value: number): string {
        return `$${value.toLocaleString('es-AR')}`;
    }

    ngOnDestroy() {
        clearTimeout(this.fadeTimer);
        clearTimeout(this.clearTimer);
    }

    // 'current' while today falls inside the period, 'scheduled' before it
    // starts, 'previous' once another fee has replaced it
    status(row: FeeRow): 'current' | 'scheduled' | 'previous' {
        const today = new Date().toISOString().slice(0, 10);
        if (row.validFrom > today) {
            return 'scheduled';
        }
        return !row.validTo || today <= row.validTo ? 'current' : 'previous';
    }

    statusLabel(row: FeeRow): string {
        switch (this.status(row)) {
            case 'current':
                return 'Vigente';
            case 'scheduled':
                return 'Programado';
            default:
                return 'Anterior';
        }
    }

    // An open-ended period shows its state instead of an end date
    validToLabel(row: FeeRow): string {
        return row.validTo ? this.formatDate(row.validTo) : this.statusLabel(row);
    }

    // ISO dates are shown the way they are read locally
    formatDate(isoDate: string): string {
        const [year, month, day] = isoDate.split('-');
        return `${day}/${month}/${year}`;
    }

    // No backend yet: the submit only simulates a successful scheduling
    onSubmit() {
        if (this.feeForm.invalid) {
            this.feeForm.markAllAsTouched();
            return;
        }

        const category = this.feeForm.value.category;
        this.feeForm.reset({ category: '', amount: '', validFrom: '' });
        this.showConfirmation(`Nuevo arancel ${category} programado correctamente.`);
    }

    // Shows the inline confirmation, fades it out and clears it after 3s
    private showConfirmation(message: string) {
        clearTimeout(this.fadeTimer);
        clearTimeout(this.clearTimer);

        this.successMessage = message;
        this.successLeaving = false;

        // Start the fade before removing the node so it does not blink out
        this.fadeTimer = setTimeout(() => (this.successLeaving = true), 2700);
        this.clearTimer = setTimeout(() => {
            this.successMessage = '';
            this.successLeaving = false;
        }, 3000);
    }
}
