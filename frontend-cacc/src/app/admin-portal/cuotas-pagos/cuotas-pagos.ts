import { Component, OnDestroy, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { AbstractControl, FormBuilder, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Subscription } from 'rxjs';
import { DiscountBadge } from '../../shared/discount-badge/discount-badge';
import { DiscountService } from '../../services/discounts';
import { DiscountModel } from '../../models/DiscountModel';
import { PlayerService } from '../../services/players';
import { PlayerModel } from '../../models/PlayerModel';
import { PaymentService } from '../../services/payments';
import { PaymentModel } from '../../models/PaymentModel';
import { PaymentRequestModel } from '../../models/PaymentRequestModel';
import { TreasuryMetricsModel } from '../../models/TreasuryMetricsModel';
import { normalizeText } from '../../shared/normalize-text';

// One counter on the page header
interface HeaderMetric {
    value: string;
    label: string;
}

// A select option whose stored value differs from the label shown
interface SelectOption<T> {
    value: T;
    label: string;
}

type ToastKind = 'success' | 'error';

@Component({
    selector: 'app-cuotas-pagos',
    standalone: true,
    imports: [CommonModule, ReactiveFormsModule, DiscountBadge],
    templateUrl: './cuotas-pagos.html',
    styleUrl: './cuotas-pagos.css',
})
export class CuotasPagos implements OnInit, OnDestroy {

    private static readonly LATEST_PAYMENTS_COUNT = 10;

    // Minimum length before suggesting: below this, a single letter like "s"
    // matches half the squad and the dropdown is more noise than help.
    private static readonly MIN_SEARCH_LENGTH = 3;

    private static readonly TOAST_DURATION_MS = 3500;
    private static readonly TOAST_FADE_MS = 300;

    // Same length as PAGOS.referencia_pago
    private static readonly REFERENCE_MAX_LENGTH = 50;

    private static readonly TRANSFER_METHOD = 'transferencia';

    // Header counters. They start as dashes so the screen never shows a made-up
    // number while the request is still in flight.
    headerMetrics = signal<HeaderMetric[]>([
        { value: '—', label: 'Recaudado del año' },
        { value: '—', label: 'Pagos del mes' },
        { value: '—', label: 'Pendientes' },
    ]);

    // Payment form
    paymentForm: FormGroup;

    months: SelectOption<number>[] = [
        { value: 1, label: 'Enero' }, { value: 2, label: 'Febrero' }, { value: 3, label: 'Marzo' },
        { value: 4, label: 'Abril' }, { value: 5, label: 'Mayo' }, { value: 6, label: 'Junio' },
        { value: 7, label: 'Julio' }, { value: 8, label: 'Agosto' }, { value: 9, label: 'Septiembre' },
        { value: 10, label: 'Octubre' }, { value: 11, label: 'Noviembre' }, { value: 12, label: 'Diciembre' },
    ];

    // Previous, current and next year: enough to settle arrears or pay ahead
    years: number[];

    // Values are stored lowercase, as the API and the database expect
    methods: SelectOption<string>[] = [
        { value: 'efectivo', label: 'Efectivo' },
        { value: CuotasPagos.TRANSFER_METHOD, label: 'Transferencia' },
    ];

    // Blocks a second submit while the payment is being registered
    submitting = false;

    // Squad the lookup searches, loaded from the API on entry
    private players = signal<PlayerModel[]>([]);

    // Suggestions shown under the search field
    matchingPlayers: PlayerModel[] = [];
    showSuggestions = false;

    // Periods ("yyyy-MM") the selected player already paid. They block collecting
    // the same fee twice before the request is sent; the API rejects it anyway.
    private paidPeriods = new Set<string>();
    private selectedPlayerId: number | null = null;

    // Notification shown after registering a payment
    toastMessage = '';
    toastKind: ToastKind = 'success';
    toastLeaving = false;

    // Fees waiting to be collected and most recent payments, both from the API.
    // Each list tracks whether its request already came back, so the template can
    // tell "still loading" apart from "there is genuinely nothing to show".
    pendingFees = signal<PaymentModel[]>([]);
    pendingLoaded = signal(false);
    latestPayments = signal<PaymentModel[]>([]);
    latestLoaded = signal(false);

    // Cleared on destroy so leaving the dashboard mid-animation never fires a
    // callback on a dead component
    private fadeTimer?: ReturnType<typeof setTimeout>;
    private clearTimer?: ReturnType<typeof setTimeout>;
    private methodSubscription?: Subscription;

    // Active discounts indexed by player. Requested once on entry, and each row
    // resolves its badge with a get on the map, so nothing is recomputed or
    // re-filtered while the grid renders.
    // Held in a signal so the grid repaints itself when the response arrives:
    // assigning a plain field left the view stale.
    private discounts = signal(new Map<number, DiscountModel>());

    constructor(
        private fb: FormBuilder,
        private discountService: DiscountService,
        private playerService: PlayerService,
        private paymentService: PaymentService
    ) {
        const currentYear = new Date().getFullYear();
        this.years = [currentYear - 1, currentYear, currentYear + 1];

        this.paymentForm = this.fb.group(
            {
                player: ['', [Validators.required, this.knownPlayerValidator]],
                periodYear: [currentYear, [Validators.required]],
                periodMonth: ['', [Validators.required]],
                amount: ['', [Validators.required, Validators.min(1)]],
                method: ['', [Validators.required]],
                reference: [
                    { value: '', disabled: true },
                    [Validators.maxLength(CuotasPagos.REFERENCE_MAX_LENGTH)],
                ],
            },
            { validators: this.unpaidPeriodValidator }
        );
    }

    ngOnInit() {
        this.discountService.getDiscountMap().subscribe({
            next: (discountMap) => this.discounts.set(discountMap),
            // The grid is the main feature: if the discounts API fails it still
            // renders, just without badges.
            error: () => this.discounts.set(new Map<number, DiscountModel>()),
        });

        this.playerService.getPlayers().subscribe({
            next: (players) => this.players.set(players),
            error: () => this.players.set([]),
        });

        this.paymentService.getPendingFees().subscribe({
            next: (fees) => {
                this.pendingFees.set(fees);
                this.pendingLoaded.set(true);
            },
            error: () => {
                this.pendingFees.set([]);
                this.pendingLoaded.set(true);
            },
        });

        this.loadLatestPayments();
        this.loadTreasuryMetrics();

        // The bank reference only applies to a transfer
        this.methodSubscription = this.paymentForm.get('method')!.valueChanges
            .subscribe((method: string) => this.toggleReference(method));
    }

    ngOnDestroy() {
        clearTimeout(this.fadeTimer);
        clearTimeout(this.clearTimer);
        this.methodSubscription?.unsubscribe();
    }

    // Returns null when the player has no active benefit and the badge is not
    // drawn: that way it disappears on its own on expiry or deactivation.
    getDiscount(playerId: number): DiscountModel | null {
        return this.discounts().get(playerId) ?? null;
    }

    initialsOf(fullName: string): string {
        const [lastName = '', firstName = ''] = fullName.split(',').map(part => part.trim());
        return ((lastName.charAt(0) || '') + (firstName.charAt(0) || '')).toUpperCase();
    }

    formatAmount(amount: number): string {
        return '$' + amount.toLocaleString('es-AR', { maximumFractionDigits: 0 });
    }

    // The backend sends ISO (yyyy-MM-dd) and the view shows it as read here
    formatDate(isoDate: string | null): string {
        if (isoDate === null) {
            return 'Sin fecha';
        }
        const [year, month, day] = isoDate.split('-');
        return day + '/' + month + '/' + year;
    }

    // The backend sends "yyyy-MM"; the view shows "Julio 2026"
    formatPeriod(period: string | null): string {
        if (period === null) {
            return 'Sin período';
        }
        const [year, month] = period.split('-');
        return `${this.months[Number(month) - 1].label} ${year}`;
    }

    selectedPeriodLabel(): string {
        const { periodYear, periodMonth } = this.paymentForm.getRawValue();
        return this.formatPeriod(this.periodKey(Number(periodYear), Number(periodMonth)));
    }

    methodLabel(method: string): string {
        return this.methods.find(option => option.value === method)?.label ?? method;
    }

    isTransfer(): boolean {
        return this.paymentForm.get('method')!.value === CuotasPagos.TRANSFER_METHOD;
    }

    // One unified field: the same term is matched against name and document
    onPlayerSearch(term: string) {
        const needle = normalizeText(term.trim());

        // Typing the exact name also counts as choosing the player
        const exactPlayer = this.findPlayer(term.trim());
        if (exactPlayer) {
            this.loadPaidPeriods(exactPlayer.id);
        } else {
            this.clearPaidPeriods();
        }

        if (needle.length < CuotasPagos.MIN_SEARCH_LENGTH) {
            this.matchingPlayers = [];
            this.showSuggestions = false;
            return;
        }

        // Digits are compared without dots so "48221" also finds "48.221.107"
        const digits = needle.replace(/\D/g, '');
        this.matchingPlayers = this.players().filter(player =>
            normalizeText(player.fullName).includes(needle) ||
            (digits.length > 0 && player.document.replace(/\D/g, '').includes(digits))
        );
        this.showSuggestions = this.matchingPlayers.length > 0;
    }

    selectPlayer(player: PlayerModel) {
        this.paymentForm.patchValue({ player: player.fullName });
        this.matchingPlayers = [];
        this.showSuggestions = false;
        this.loadPaidPeriods(player.id);
    }

    hideSuggestions() {
        this.showSuggestions = false;
    }

    onSubmit() {
        const player = this.findPlayer((this.paymentForm.value.player ?? '').toString().trim());
        if (this.paymentForm.invalid || this.submitting || !player) {
            this.paymentForm.markAllAsTouched();
            return;
        }

        const formValue = this.paymentForm.getRawValue();
        const reference = (formValue.reference ?? '').toString().trim();
        const request: PaymentRequestModel = {
            playerId: player.id,
            periodYear: Number(formValue.periodYear),
            periodMonth: Number(formValue.periodMonth),
            amount: Number(formValue.amount),
            method: formValue.method,
            reference: this.isTransfer() && reference !== '' ? reference : null,
        };

        this.submitting = true;
        this.paymentService.createPayment(request).subscribe({
            next: (payment) => {
                this.submitting = false;
                this.resetForm();
                this.showToast(`Pago de ${player.fullName} registrado: ${this.formatPeriod(payment.period)}.`, 'success');

                // The grid and the counters show the new payment without reloading the page
                this.loadLatestPayments();
                this.loadTreasuryMetrics();
            },
            error: (error: HttpErrorResponse) => {
                this.submitting = false;
                this.showToast(this.describeError(error), 'error');

                // A rejected duplicate means the local history was stale
                if (error.status === 400) {
                    this.loadPaidPeriods(player.id);
                }
            },
        });
    }

    // The typed text must resolve to a real player, not just be non-empty
    private knownPlayerValidator = (control: AbstractControl): ValidationErrors | null => {
        const value = (control.value ?? '').toString().trim();
        if (!value) {
            return null;
        }
        return this.findPlayer(value) ? null : { unknownPlayer: true };
    };

    // The selected player cannot pay the same period twice
    private unpaidPeriodValidator = (group: AbstractControl): ValidationErrors | null => {
        const year = group.get('periodYear')?.value;
        const month = group.get('periodMonth')?.value;
        if (!year || !month) {
            return null;
        }
        return this.paidPeriods.has(this.periodKey(Number(year), Number(month)))
            ? { periodAlreadyPaid: true }
            : null;
    };

    private findPlayer(value: string): PlayerModel | undefined {
        const needle = normalizeText(value);
        return this.players().find(player => normalizeText(player.fullName) === needle);
    }

    private periodKey(year: number, month: number): string {
        return `${year}-${String(month).padStart(2, '0')}`;
    }

    private loadPaidPeriods(playerId: number) {
        if (this.selectedPlayerId === playerId) {
            return;
        }
        this.selectedPlayerId = playerId;
        this.paidPeriods = new Set<string>();

        this.paymentService.getPaymentsByPlayer(playerId).subscribe({
            next: (payments) => {
                // Ignore a late response for a player that is no longer selected
                if (this.selectedPlayerId !== playerId) {
                    return;
                }
                this.paidPeriods = new Set(
                    payments
                        .filter(payment => payment.period !== null)
                        .map(payment => payment.period as string)
                );
                this.paymentForm.updateValueAndValidity();
            },
            // Without the history the form still submits and the API rejects a duplicate
            error: () => undefined,
        });
    }

    private clearPaidPeriods() {
        this.selectedPlayerId = null;
        this.paidPeriods = new Set<string>();
        this.paymentForm.updateValueAndValidity();
    }

    private toggleReference(method: string) {
        const reference = this.paymentForm.get('reference')!;
        if (method === CuotasPagos.TRANSFER_METHOD) {
            reference.enable();
            return;
        }
        reference.reset({ value: '', disabled: true });
    }

    private resetForm() {
        this.paymentForm.reset({
            player: '',
            periodYear: new Date().getFullYear(),
            periodMonth: '',
            amount: '',
            method: '',
            reference: '',
        });
        this.matchingPlayers = [];
        this.showSuggestions = false;
        this.clearPaidPeriods();
    }

    private describeError(error: HttpErrorResponse): string {
        if (error.status === 0) {
            return 'No se pudo conectar con el servidor. Intentá nuevamente.';
        }
        if (error.status === 401) {
            return 'Tu sesión venció. Iniciá sesión nuevamente para registrar el pago.';
        }
        if (error.status === 400) {
            return 'No se pudo registrar el pago: el período ya está abonado o hay datos inválidos.';
        }
        return 'Ocurrió un error al registrar el pago. Intentá nuevamente.';
    }

    private loadLatestPayments() {
        this.paymentService.getLatestPayments(CuotasPagos.LATEST_PAYMENTS_COUNT).subscribe({
            next: (payments) => {
                this.latestPayments.set(payments);
                this.latestLoaded.set(true);
            },
            error: () => {
                this.latestPayments.set([]);
                this.latestLoaded.set(true);
            },
        });
    }

    private loadTreasuryMetrics() {
        this.paymentService.getTreasuryMetrics().subscribe({
            next: (metrics) => this.headerMetrics.set(this.buildHeaderMetrics(metrics)),
            // The dashes already state that the counters are unavailable
            error: () => undefined,
        });
    }

    private buildHeaderMetrics(metrics: TreasuryMetricsModel): HeaderMetric[] {
        return [
            { value: this.formatAmount(metrics.collectedThisYear), label: 'Recaudado del año' },
            { value: metrics.paymentsThisMonth.toString(), label: 'Pagos del mes' },
            { value: metrics.pendingCount.toString(), label: 'Pendientes' },
        ];
    }

    // Shows the toast, fades it out and removes it
    private showToast(message: string, kind: ToastKind) {
        clearTimeout(this.fadeTimer);
        clearTimeout(this.clearTimer);

        this.toastMessage = message;
        this.toastKind = kind;
        this.toastLeaving = false;

        // Start the fade before removing the node so it does not blink out
        this.fadeTimer = setTimeout(
            () => (this.toastLeaving = true),
            CuotasPagos.TOAST_DURATION_MS - CuotasPagos.TOAST_FADE_MS
        );
        this.clearTimer = setTimeout(() => {
            this.toastMessage = '';
            this.toastLeaving = false;
        }, CuotasPagos.TOAST_DURATION_MS);
    }
}
