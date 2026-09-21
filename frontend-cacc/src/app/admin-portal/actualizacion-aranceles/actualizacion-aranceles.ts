import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { BaseChartDirective } from 'ng2-charts';
import { Chart, ChartConfiguration, registerables } from 'chart.js';
import { ArancelesService, ArancelHistorialItem } from '../../services/aranceles';
import { CustomSelect } from '../../shared/custom-select/custom-select';
import { NotificationService } from '../../shared/notifications/notification.service';

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
    estado: 'Vigente' | 'Programado' | 'Anterior';
}

const CURRENCY_FULL = new Intl.NumberFormat('es-AR', {
    style: 'currency',
    currency: 'ARS',
    maximumFractionDigits: 0,
});

@Component({
    selector: 'app-actualizacion-aranceles',
    standalone: true,
    imports: [CommonModule, ReactiveFormsModule, BaseChartDirective, CustomSelect],
    templateUrl: './actualizacion-aranceles.html',
    styleUrl: './actualizacion-aranceles.css',
})
export class ActualizacionAranceles implements OnInit {

    headerMetrics = [
        { value: '—', label: 'Arancel masculino' },
        { value: '—', label: 'Arancel femenino' },
        { value: '—', label: 'Próximo cambio' },
    ];

    feeForm: FormGroup;

    categories = ['Masculino', 'Femenino'];

    enviando = false;

    // Every fee ever set, newest first: past periods, the current one and the
    // changes already scheduled ahead
    feeRows: FeeRow[] = [];

    // Lo que se ve en el input de Nuevo monto ("1.000"). El control del form (feeForm.get('amount'))
    // guarda el número limpio sin puntos ("1000"), que es lo que se valida y se manda al backend.
    montoDisplay = '';

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

    constructor(
        private fb: FormBuilder,
        private arancelesService: ArancelesService,
        private cdr: ChangeDetectorRef,
        private notifications: NotificationService
    ) {
        this.feeForm = this.fb.group({
            category: ['', [Validators.required]],
            amount: ['', [Validators.required, Validators.min(1)]],
            validFrom: ['', [Validators.required]],
        });
    }

    ngOnInit() {
        this.cargarDatos();
    }

    // Reformatea el Nuevo monto con puntos de miles a medida que se escribe, preservando la
    // posición del cursor por cantidad de dígitos (no por índice de caracter, que cambia cada
    // vez que se agrega o saca un punto) — así corregir un número a mitad de la escritura no
    // hace saltar el cursor al final.
    onMontoInput(target: HTMLInputElement) {
        const cursorPos = target.selectionStart ?? target.value.length;
        const digitsBeforeCursor = target.value.slice(0, cursorPos).replace(/\D/g, '').length;

        const digitsOnly = target.value.replace(/\D/g, '').slice(0, 12);
        const formatted = digitsOnly ? Number(digitsOnly).toLocaleString('es-AR') : '';

        // Se escribe el DOM a mano, no solo vía el binding [value]: si el texto recalculado da
        // igual al montoDisplay anterior (ej. al borrar un punto de formato, que reaparece solo),
        // Angular no vuelve a tocar el input porque "no cambió" desde su óptica — pero el navegador
        // ya había mutado el value nativamente al borrar, y sin esto queda esa edición sin corregir.
        target.value = formatted;
        this.montoDisplay = formatted;
        this.feeForm.patchValue({ amount: digitsOnly });

        queueMicrotask(() => {
            let newPos = digitsBeforeCursor === 0 ? 0 : formatted.length;
            let digitsSeen = 0;
            for (let i = 0; i < formatted.length && digitsBeforeCursor > 0; i++) {
                if (/\d/.test(formatted[i])) {
                    digitsSeen++;
                }
                if (digitsSeen === digitsBeforeCursor) {
                    newPos = i + 1;
                    break;
                }
            }
            target.setSelectionRange(newPos, newPos);
        });
    }

    private cargarDatos() {
        this.arancelesService.getResumen().subscribe({
            next: (resumen) => {
                this.headerMetrics = [
                    { value: resumen.arancelMasculinoVigente != null ? CURRENCY_FULL.format(resumen.arancelMasculinoVigente) : '—', label: 'Arancel masculino' },
                    { value: resumen.arancelFemeninoVigente != null ? CURRENCY_FULL.format(resumen.arancelFemeninoVigente) : '—', label: 'Arancel femenino' },
                    { value: resumen.proximoCambioFecha ? this.formatDate(resumen.proximoCambioFecha) : 'Sin cambios', label: 'Próximo cambio' },
                ];
                this.cdr.detectChanges();
            },
            error: () => {},
        });

        this.arancelesService.getHistorial().subscribe({
            next: (historial) => {
                this.feeRows = historial.map(mapHistorialItem);
                this.feeChartData = this.buildChartData();
                this.cdr.detectChanges();
            },
            error: () => {},
        });
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

    // El estado ya viene calculado desde el backend (Vigente/Programado/Anterior).
    status(row: FeeRow): 'current' | 'scheduled' | 'previous' {
        switch (row.estado) {
            case 'Vigente': return 'current';
            case 'Programado': return 'scheduled';
            default: return 'previous';
        }
    }

    statusLabel(row: FeeRow): string {
        return row.estado;
    }

    // An open-ended period shows its state instead of an end date
    validToLabel(row: FeeRow): string {
        return row.validTo ? this.formatDate(row.validTo) : this.statusLabel(row);
    }

    // ISO dates are shown the way they are read locally
    formatDate(isoDate: string): string {
        const [year, month, day] = isoDate.slice(0, 10).split('-');
        return `${day}/${month}/${year}`;
    }

    onSubmit() {
        if (this.feeForm.invalid) {
            this.feeForm.markAllAsTouched();
            return;
        }

        const { category, amount, validFrom } = this.feeForm.value;

        this.enviando = true;

        this.arancelesService.programar(category, Number(amount), validFrom).subscribe({
            next: () => {
                this.enviando = false;
                this.feeForm.reset({ category: '', amount: '', validFrom: '' });
                this.montoDisplay = '';
                this.notifications.notify(`Nuevo arancel ${category} programado correctamente.`, 'success');
                this.cargarDatos();
                this.cdr.detectChanges();
            },
            error: (err: HttpErrorResponse) => {
                this.enviando = false;
                const mensaje = err.error?.mensaje ?? 'No se pudo programar el arancel. Intentá de nuevo.';
                this.notifications.notify(mensaje, 'error');
                this.cdr.detectChanges();
            },
        });
    }
}

function mapHistorialItem(item: ArancelHistorialItem): FeeRow {
    return {
        id: item.idArancel,
        category: item.genero === 'Femenino' ? 'female' : 'male',
        categoryLabel: item.genero,
        amount: `$${item.monto.toLocaleString('es-AR')}`,
        validFrom: item.vigenteDesde.slice(0, 10),
        validTo: item.vigenteHasta ? item.vigenteHasta.slice(0, 10) : '',
        estado: item.estado,
    };
}
