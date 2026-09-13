import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AbstractControl, FormBuilder, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { BaseChartDirective } from 'ng2-charts';
import { Chart, ChartConfiguration, registerables } from 'chart.js';
import { TariffService } from '../../services/tariffs';
import { TariffModel } from '../../models/TariffModel';

Chart.register(...registerables);

type Branch = 'M' | 'F';

@Component({
    selector: 'app-actualizacion-aranceles',
    standalone: true,
    imports: [CommonModule, ReactiveFormsModule, BaseChartDirective],
    templateUrl: './actualizacion-aranceles.html',
    styleUrl: './actualizacion-aranceles.css',
})
export class ActualizacionAranceles implements OnInit, OnDestroy {

    // Header: filled once the current tariffs are loaded
    headerMetrics: { value: string; label: string }[] = [];

    // Scheduling form
    feeForm: FormGroup;

    branchOptions: { value: Branch; label: string }[] = [
        { value: 'M', label: 'Masculino' },
        { value: 'F', label: 'Femenino' },
    ];

    // Inline confirmation shown after a successful POST
    successMessage = '';
    successLeaving = false;

    // Inline error shown when the API rejects the scheduling (e.g. a date that
    // does not come after the branch's current tariff)
    submitError = '';

    // Every tariff ever scheduled, both branches, newest first
    feeRows: TariffModel[] = [];

    loadError = '';

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

    constructor(private fb: FormBuilder, private tariffService: TariffService) {
        this.feeForm = this.fb.group({
            branch: ['', [Validators.required]],
            amount: ['', [Validators.required, Validators.min(1)]],
            validFrom: ['', [Validators.required, this.notInThePastValidator]],
        });
    }

    ngOnInit() {
        this.loadTariffs();
    }

    // Pulls both the full history (table + chart) and the current tariffs
    // (header) fresh from the API. Called on init and again after a
    // successful schedule, so the screen never shows stale data.
    private loadTariffs() {
        this.loadError = '';

        this.tariffService.getTariffHistory().subscribe({
            next: (tariffs) => {
                this.feeRows = tariffs;
                this.feeChartData = this.buildChartData();
                this.headerMetrics = this.buildHeaderMetrics(tariffs);
            },
            error: () => {
                this.loadError = 'No se pudo cargar el historial de aranceles.';
            },
        });
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

    // Series for both branches, from January up to the current month, priced
    // with the tariff that was in force on the first day of each month
    private buildChartData(): ChartConfiguration<'line'>['data'] {
        const now = new Date();
        const year = now.getFullYear();
        const monthCount = now.getMonth() + 1;

        const monthlyAmounts = (branch: Branch) =>
            Array.from({ length: monthCount }, (_, index) => {
                const reference = `${year}-${String(index + 1).padStart(2, '0')}-01`;
                const row = this.feeRows.find(tariff =>
                    tariff.branch === branch &&
                    tariff.validFrom <= reference &&
                    (!tariff.validTo || reference <= tariff.validTo)
                );
                return row ? row.amount : null;
            });

        return {
            labels: this.monthLabels.slice(0, monthCount),
            datasets: [
                {
                    label: 'Masculino',
                    data: monthlyAmounts('M'),
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
                    data: monthlyAmounts('F'),
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

    // Header shows each branch's tariff in force today, plus the soonest
    // upcoming change across both (if any is scheduled ahead)
    private buildHeaderMetrics(tariffs: TariffModel[]): { value: string; label: string }[] {
        const currentAmount = (branch: Branch) => {
            const current = tariffs.find(tariff => tariff.branch === branch && tariff.validFrom <= this.today && tariff.isActive);
            return current ? this.formatAmount(current.amount) : 'Sin arancel vigente';
        };

        const upcoming = tariffs
            .filter(tariff => tariff.validFrom > this.today)
            .sort((a, b) => a.validFrom.localeCompare(b.validFrom))[0];

        const nextChangeValue = upcoming
            ? this.daysUntil(upcoming.validFrom)
            : 'Sin cambios programados';

        return [
            { value: currentAmount('M'), label: 'Arancel masculino' },
            { value: currentAmount('F'), label: 'Arancel femenino' },
            { value: nextChangeValue, label: 'Próximo cambio' },
        ];
    }

    private daysUntil(isoDate: string): string {
        const msPerDay = 24 * 60 * 60 * 1000;
        const days = Math.round((new Date(isoDate).getTime() - new Date(this.today).getTime()) / msPerDay);
        if (days <= 0) {
            return 'Hoy';
        }
        return days === 1 ? 'Mañana' : `En ${days} días`;
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
    // starts, 'previous' once another tariff has replaced it
    status(row: TariffModel): 'current' | 'scheduled' | 'previous' {
        if (row.validFrom > this.today) {
            return 'scheduled';
        }
        return !row.validTo || this.today <= row.validTo ? 'current' : 'previous';
    }

    statusLabel(row: TariffModel): string {
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
    validToLabel(row: TariffModel): string {
        return row.validTo ? this.formatDate(row.validTo) : this.statusLabel(row);
    }

    // 'M' -> 'male' / 'F' -> 'female', to reuse the existing category-* pill styles
    branchClass(branch: Branch): 'male' | 'female' {
        return branch === 'M' ? 'male' : 'female';
    }

    branchLabel(branch: Branch): string {
        return branch === 'M' ? 'Masculino' : 'Femenino';
    }

    formatRowAmount(row: TariffModel): string {
        return this.formatAmount(row.amount);
    }

    // ISO dates are shown the way they are read locally
    formatDate(isoDate: string): string {
        const [year, month, day] = isoDate.split('-');
        return `${day}/${month}/${year}`;
    }

    onSubmit() {
        if (this.feeForm.invalid) {
            this.feeForm.markAllAsTouched();
            return;
        }

        this.submitError = '';

        const branch: Branch = this.feeForm.value.branch;
        const amount = Number(this.feeForm.value.amount);
        const validFrom: string = this.feeForm.value.validFrom;

        this.tariffService.scheduleTariff({ branch, amount, validFrom }).subscribe({
            next: () => {
                this.feeForm.reset({ branch: '', amount: '', validFrom: '' });
                this.showConfirmation(`Nuevo arancel ${this.branchLabel(branch)} programado correctamente.`);
                this.loadTariffs();
            },
            error: (response) => {
                this.submitError = this.extractErrorMessage(response);
            },
        });
    }

    // The API returns the validation message as a plain string body on 400
    private extractErrorMessage(response: unknown): string {
        const error = (response as { error?: unknown })?.error;
        if (typeof error === 'string' && error.trim()) {
            return error;
        }
        return 'No se pudo programar el nuevo arancel. Intentá nuevamente.';
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
