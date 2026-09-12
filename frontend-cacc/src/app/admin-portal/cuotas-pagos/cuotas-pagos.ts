import { Component, OnDestroy, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AbstractControl, FormBuilder, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { DiscountBadge } from '../../shared/discount-badge/discount-badge';
import { DiscountService } from '../../services/discounts';
import { DiscountModel } from '../../models/DiscountModel';
import { PlayerService } from '../../services/players';
import { PlayerModel } from '../../models/PlayerModel';
import { PaymentService } from '../../services/payments';
import { PaymentModel } from '../../models/PaymentModel';
import { TreasuryMetricsModel } from '../../models/TreasuryMetricsModel';
import { normalizeText } from '../../shared/normalize-text';

// One counter on the page header
interface HeaderMetric {
    value: string;
    label: string;
}

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

    // Header counters. They start as dashes so the screen never shows a made-up
    // number while the request is still in flight.
    headerMetrics = signal<HeaderMetric[]>([
        { value: '—', label: 'Recaudado del año' },
        { value: '—', label: 'Pagos del mes' },
        { value: '—', label: 'Pendientes' },
    ]);

    // Payment form
    paymentForm: FormGroup;

    periods = [
        'Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio',
        'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre',
    ];

    methods = ['Transferencia', 'Efectivo'];

    // Squad the lookup searches, loaded from the API on entry
    private players = signal<PlayerModel[]>([]);

    // Suggestions shown under the search field
    matchingPlayers: PlayerModel[] = [];
    showSuggestions = false;

    // Inline confirmation shown after a simulated submit
    successMessage = '';
    successLeaving = false;

    // Fees waiting to be collected and most recent payments, both from the API.
    // Each list tracks whether its request already came back, so the template can
    // tell "still loading" apart from "there is genuinely nothing to show".
    pendingFees = signal<PaymentModel[]>([]);
    pendingLoaded = signal(false);
    latestPayments = signal<PaymentModel[]>([]);
    latestLoaded = signal(false);

    // Timers for the confirmation message, cleared on destroy so leaving the
    // dashboard mid-animation never fires a callback on a dead component
    private fadeTimer?: ReturnType<typeof setTimeout>;
    private clearTimer?: ReturnType<typeof setTimeout>;

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
        this.paymentForm = this.fb.group({
            player: ['', [Validators.required, this.knownPlayerValidator]],
            period: ['', [Validators.required]],
            amount: ['', [Validators.required, Validators.min(1)]],
            method: ['', [Validators.required]],
        });
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

        this.paymentService.getTreasuryMetrics().subscribe({
            next: (metrics) => this.headerMetrics.set(this.buildHeaderMetrics(metrics)),
            // The dashes already state that the counters are unavailable
            error: () => undefined,
        });
    }

    // Returns null when the player has no active benefit and the badge is not
    // drawn: that way it disappears on its own on expiry or deactivation.
    getDiscount(playerId: number): DiscountModel | null {
        return this.discounts().get(playerId) ?? null;
    }

    ngOnDestroy() {
        clearTimeout(this.fadeTimer);
        clearTimeout(this.clearTimer);
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

    // The typed text must resolve to a real player, not just be non-empty
    private knownPlayerValidator = (control: AbstractControl): ValidationErrors | null => {
        const value = (control.value ?? '').toString().trim();
        if (!value) {
            return null;
        }
        return this.findPlayer(value) ? null : { unknownPlayer: true };
    };

    private findPlayer(value: string): PlayerModel | undefined {
        const needle = normalizeText(value);
        return this.players().find(player => normalizeText(player.fullName) === needle);
    }

    // One unified field: the same term is matched against name and document
    onPlayerSearch(term: string) {
        const needle = normalizeText(term.trim());
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
    }

    hideSuggestions() {
        this.showSuggestions = false;
    }

    // No backend yet: the submit only simulates a successful registration
    onSubmit() {
        if (this.paymentForm.invalid) {
            this.paymentForm.markAllAsTouched();
            return;
        }

        const playerName = this.paymentForm.value.player;
        this.paymentForm.reset({ player: '', period: '', amount: '', method: '' });
        this.matchingPlayers = [];
        this.showSuggestions = false;
        this.showConfirmation('Pago de ' + playerName + ' registrado correctamente.');
    }

    private buildHeaderMetrics(metrics: TreasuryMetricsModel): HeaderMetric[] {
        return [
            { value: this.formatAmount(metrics.collectedThisYear), label: 'Recaudado del año' },
            { value: metrics.paymentsThisMonth.toString(), label: 'Pagos del mes' },
            { value: metrics.pendingCount.toString(), label: 'Pendientes' },
        ];
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
