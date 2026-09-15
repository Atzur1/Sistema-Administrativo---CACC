import { Component, OnDestroy, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AbstractControl, FormBuilder, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { DiscountBadge } from '../../shared/discount-badge/discount-badge';
import { DiscountService } from '../../services/discounts';
import { BenefitValueType, DiscountModel, DiscountRequest, formatBenefitValue } from '../../models/DiscountModel';
import { PlayerService } from '../../services/players';
import { PlayerModel } from '../../models/PlayerModel';
import { normalizeText } from '../../shared/normalize-text';

// One counter on the page header
interface HeaderMetric {
    value: string;
    label: string;
}

// What the popup is showing at any moment. Keeping it in one field instead of a
// handful of booleans means the dialog can never be in two states at once.
type DialogView = 'loading' | 'form' | 'active' | 'confirmCancel';

@Component({
    selector: 'app-becados-descuentos',
    standalone: true,
    imports: [CommonModule, ReactiveFormsModule, DiscountBadge],
    templateUrl: './becados-descuentos.html',
    styleUrl: './becados-descuentos.css',
})
export class BecadosDescuentos implements OnInit, OnDestroy {

    // Header counters, derived from what the API returns. They start as dashes
    // so the screen never shows a made-up number while the request is in flight.
    headerMetrics = signal<HeaderMetric[]>([
        { value: '—', label: 'Beneficios asignados' },
        { value: '—', label: 'Vigentes' },
        { value: '—', label: 'No vigentes' },
    ]);

    // Player lookup on the page, the entry point to the popup
    lookupForm: FormGroup;

    // Assignment form, lives inside the popup
    benefitForm: FormGroup;

    // Filled from the catalogue endpoint, never hardcoded: the API validates
    // against the same table, so an option offered here is always accepted.
    benefitReasons = signal<string[]>([]);

    valueTypes: { value: BenefitValueType; label: string }[] = [
        { value: '%', label: 'Porcentaje' },
        { value: '$', label: 'Monto fijo' },
    ];

    // Squad the lookup searches, loaded from the API on entry
    private players = signal<PlayerModel[]>([]);

    // Suggestions shown under the search field
    matchingPlayers: PlayerModel[] = [];
    showSuggestions = false;
    selectedPlayer: PlayerModel | null = null;

    // ===== POPUP STATE =====
    dialogOpen = signal(false);
    dialogView = signal<DialogView>('loading');
    dialogPlayer = signal<PlayerModel | null>(null);
    currentDiscount = signal<DiscountModel | null>(null);
    dialogError = signal('');
    saving = signal(false);

    // Inline confirmation shown on the page after a successful operation
    successMessage = '';
    successLeaving = false;

    // Every benefit ever granted: the table mixes current and cancelled ones.
    // benefitsLoaded tells "still loading" apart from "there is nothing to show".
    benefitRows = signal<DiscountModel[]>([]);
    benefitsLoaded = signal(false);

    // Timers for the confirmation message, cleared on destroy so leaving the
    // dashboard mid-animation never fires a callback on a dead component
    private fadeTimer?: ReturnType<typeof setTimeout>;
    private clearTimer?: ReturnType<typeof setTimeout>;

    // Active discounts indexed by player, resolved by the backend.
    // Held in a signal so the table repaints when the response arrives.
    private discounts = signal(new Map<number, DiscountModel>());

    constructor(
        private fb: FormBuilder,
        private discountService: DiscountService,
        private playerService: PlayerService
    ) {
        this.lookupForm = this.fb.group({
            player: ['', [Validators.required, this.knownPlayerValidator]],
        });

        this.benefitForm = this.fb.group({
            reason: ['', [Validators.required]],
            valueType: ['', [Validators.required]],
            percentage: [null as number | null],
            fixedAmount: [null as number | null],
            startDate: [''],
            endDate: [''],
        }, { validators: [this.benefitValueValidator, this.dateRangeValidator] });
    }

    ngOnInit() {
        this.loadDiscountMap();
        this.loadBenefitRows();

        this.playerService.getPlayers().subscribe({
            next: (players) => this.players.set(players),
            error: () => this.players.set([]),
        });

        this.discountService.getDiscountTypes().subscribe({
            next: (reasons) => this.benefitReasons.set(reasons),
            // Without the catalogue the form cannot offer a valid reason, so the
            // popup says so instead of showing an empty dropdown.
            error: () => this.benefitReasons.set([]),
        });
    }

    ngOnDestroy() {
        clearTimeout(this.fadeTimer);
        clearTimeout(this.clearTimer);
    }

    // ===== DATA LOADING =====

    private loadDiscountMap() {
        this.discountService.getDiscountMap().subscribe({
            next: (discountMap) => this.discounts.set(discountMap),
            // If the API fails the table still renders, just without badges
            error: () => this.discounts.set(new Map<number, DiscountModel>()),
        });
    }

    private loadBenefitRows() {
        this.discountService.getAllDiscounts().subscribe({
            next: (benefits) => {
                this.benefitRows.set(benefits);
                this.benefitsLoaded.set(true);
                this.headerMetrics.set(this.buildHeaderMetrics(benefits));
            },
            error: () => {
                this.benefitRows.set([]);
                this.benefitsLoaded.set(true);
            },
        });
    }

    // Re-reads everything the screen shows from the API. Called after every
    // successful write so the view renders persisted data and never a local
    // guess of what was saved.
    private refreshFromServer() {
        this.loadDiscountMap();
        this.loadBenefitRows();
    }

    private buildHeaderMetrics(benefits: DiscountModel[]): HeaderMetric[] {
        const active = benefits.filter(benefit => benefit.isActive).length;
        return [
            { value: benefits.length.toString(), label: 'Beneficios asignados' },
            { value: active.toString(), label: 'Vigentes' },
            { value: (benefits.length - active).toString(), label: 'No vigentes' },
        ];
    }

    // ===== PLAYER LOOKUP =====

    initialsOf(fullName: string): string {
        const [lastName = '', firstName = ''] = fullName.split(',').map(part => part.trim());
        return ((lastName.charAt(0) || '') + (firstName.charAt(0) || '')).toUpperCase();
    }

    // Null cuando el jugador no tiene beneficio vigente: el badge no se dibuja
    getDiscount(playerId: number): DiscountModel | null {
        return this.discounts().get(playerId) ?? null;
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

    // Minimum length before suggesting: below this, a single letter like "s"
    // matches half the squad and the dropdown is more noise than help.
    private static readonly MIN_SEARCH_LENGTH = 3;

    // One unified field: the same term is matched against name and document
    onPlayerSearch(term: string) {
        this.selectedPlayer = this.findPlayer(term.trim()) ?? null;

        const needle = normalizeText(term.trim());
        if (needle.length < BecadosDescuentos.MIN_SEARCH_LENGTH) {
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
        this.lookupForm.patchValue({ player: player.fullName });
        this.selectedPlayer = player;
        this.matchingPlayers = [];
        this.showSuggestions = false;
    }

    hideSuggestions() {
        this.showSuggestions = false;
    }

    // ===== POPUP =====

    // Entry point from the lookup panel
    openDialogForSelectedPlayer() {
        if (this.selectedPlayer === null) {
            return;
        }
        this.openDialog(this.selectedPlayer);
    }

    // Entry point from a row of the table
    openDialogForRow(row: DiscountModel) {
        const player = this.players().find(candidate => candidate.id === row.playerId);

        // The table row already carries the name, so the popup can open even if
        // the squad list has not arrived yet
        this.openDialog(player ?? {
            id: row.playerId,
            fullName: row.playerName,
            document: '',
            category: row.category,
        } as PlayerModel);
    }

    private openDialog(player: PlayerModel) {
        this.dialogPlayer.set(player);
        this.dialogError.set('');
        this.currentDiscount.set(null);
        this.dialogView.set('loading');
        this.dialogOpen.set(true);

        // The popup always asks the API what the player has right now: the grid
        // may be stale and a benefit assigned from another screen has to show up.
        // It asks for the open assignment, expired included, because that is what
        // decides whether a new one can be granted.
        this.discountService.getAssignedDiscount(player.id).subscribe({
            next: (discount) => {
                this.currentDiscount.set(discount);
                this.dialogView.set('active');
            },
            error: (error: HttpErrorResponse) => {
                if (error.status === 404) {
                    // No benefit yet: straight to the assignment form
                    this.resetBenefitForm();
                    this.dialogView.set('form');
                    return;
                }

                this.dialogError.set('No se pudo cargar la bonificación del jugador. Intentá de nuevo.');
                this.dialogView.set('form');
            },
        });
    }

    closeDialog() {
        this.dialogOpen.set(false);
        this.dialogError.set('');
        this.saving.set(false);
    }

    // Switches the popup from showing the active benefit to editing it
    startEditing() {
        const discount = this.currentDiscount();
        if (discount === null) {
            return;
        }

        this.benefitForm.reset({
            reason: discount.type,
            valueType: discount.valueType,
            percentage: discount.percentage,
            fixedAmount: discount.fixedAmount,
            startDate: discount.startDate,
            endDate: discount.endDate,
        });

        this.dialogError.set('');
        this.dialogView.set('form');
    }

    askCancelConfirmation() {
        this.dialogError.set('');
        this.dialogView.set('confirmCancel');
    }

    backToActive() {
        this.dialogError.set('');
        this.dialogView.set('active');
    }

    private resetBenefitForm() {
        this.benefitForm.reset({
            reason: '',
            valueType: '',
            percentage: null,
            fixedAmount: null,
            startDate: this.today(),
            endDate: '',
        });
    }

    private today(): string {
        const now = new Date();
        const month = `${now.getMonth() + 1}`.padStart(2, '0');
        const day = `${now.getDate()}`.padStart(2, '0');
        return `${now.getFullYear()}-${month}-${day}`;
    }

    // ===== FORM =====

    get isEditing(): boolean {
        return this.currentDiscount() !== null;
    }

    get selectedValueType(): BenefitValueType | '' {
        return this.benefitForm.get('valueType')?.value ?? '';
    }

    // Switching between percentage and fixed amount clears the field that no
    // longer applies, so a value left over from the other mode can never travel
    // in the payload.
    onValueTypeChange() {
        if (this.selectedValueType === '%') {
            this.benefitForm.patchValue({ fixedAmount: null });
        } else {
            this.benefitForm.patchValue({ percentage: null });
        }
    }

    // Mirrors the DTO rules: the value that matches the type is required and has
    // to be in range, and the other one must stay empty.
    private benefitValueValidator = (group: AbstractControl): ValidationErrors | null => {
        const valueType = group.get('valueType')?.value;
        const percentage = group.get('percentage')?.value;
        const fixedAmount = group.get('fixedAmount')?.value;

        if (valueType === '%') {
            if (percentage === null || percentage === '' || percentage === undefined) {
                return { percentageRequired: true };
            }
            const value = Number(percentage);
            if (Number.isNaN(value) || value <= 0 || value > 100) {
                return { percentageOutOfRange: true };
            }
            return null;
        }

        if (valueType === '$') {
            if (fixedAmount === null || fixedAmount === '' || fixedAmount === undefined) {
                return { fixedAmountRequired: true };
            }
            const value = Number(fixedAmount);
            if (Number.isNaN(value) || value <= 0) {
                return { fixedAmountNotPositive: true };
            }
            return null;
        }

        return null;
    };

    private dateRangeValidator = (group: AbstractControl): ValidationErrors | null => {
        const start = group.get('startDate')?.value;
        const end = group.get('endDate')?.value;

        if (!start || !end) {
            return null;
        }

        return end < start ? { endBeforeStart: true } : null;
    };

    // Single message for the whole form, shown under the fields
    get formErrorMessage(): string {
        if (!this.benefitForm.touched && !this.benefitForm.dirty) {
            return '';
        }

        const errors = this.benefitForm.errors ?? {};

        if (this.benefitForm.get('reason')?.invalid) {
            return 'Seleccioná el motivo de la bonificación.';
        }
        if (this.benefitForm.get('valueType')?.invalid) {
            return 'Seleccioná si el beneficio es porcentual o de monto fijo.';
        }
        if (errors['percentageRequired']) {
            return 'Ingresá el porcentaje de descuento.';
        }
        if (errors['percentageOutOfRange']) {
            return 'El porcentaje debe ser mayor a 0 y no superar 100.';
        }
        if (errors['fixedAmountRequired']) {
            return 'Ingresá el monto del descuento.';
        }
        if (errors['fixedAmountNotPositive']) {
            return 'El monto debe ser mayor a cero.';
        }
        if (errors['endBeforeStart']) {
            return 'La fecha de fin no puede ser anterior a la de inicio.';
        }

        return '';
    }

    // ===== WRITES =====

    onSubmit() {
        const player = this.dialogPlayer();
        if (player === null || this.benefitForm.invalid || this.saving()) {
            this.benefitForm.markAllAsTouched();
            return;
        }

        const request = this.buildRequest();
        const editing = this.isEditing;

        this.saving.set(true);
        this.dialogError.set('');

        const call = editing
            ? this.discountService.updateDiscount(player.id, request)
            : this.discountService.assignDiscount(player.id, request);

        call.subscribe({
            next: () => {
                this.saving.set(false);
                this.closeDialog();
                // Never trust the payload just sent: the grid is rebuilt from the
                // API so what the screen shows is what the database stored.
                this.refreshFromServer();
                this.showConfirmation(editing
                    ? `Bonificación de ${player.fullName} actualizada correctamente.`
                    : `Bonificación de ${player.fullName} asignada correctamente.`);
            },
            error: (error: HttpErrorResponse) => {
                this.saving.set(false);
                this.dialogError.set(this.messageFor(error, editing ? 'editar' : 'guardar'));
            },
        });
    }

    confirmCancelBenefit() {
        const player = this.dialogPlayer();
        if (player === null || this.saving()) {
            return;
        }

        this.saving.set(true);
        this.dialogError.set('');

        this.discountService.cancelDiscount(player.id).subscribe({
            next: () => {
                this.saving.set(false);
                this.closeDialog();
                this.refreshFromServer();
                this.showConfirmation(`Bonificación de ${player.fullName} cancelada.`);
            },
            error: (error: HttpErrorResponse) => {
                this.saving.set(false);
                this.dialogView.set('active');
                this.dialogError.set(this.messageFor(error, 'cancelar'));
            },
        });
    }

    private buildRequest(): DiscountRequest {
        const raw = this.benefitForm.value;
        const valueType: BenefitValueType = raw.valueType;

        return {
            reason: raw.reason,
            valueType,
            // The side that does not apply travels as null, the same exclusivity
            // the API and the table constraint demand.
            percentage: valueType === '%' ? Number(raw.percentage) : null,
            fixedAmount: valueType === '$' ? Number(raw.fixedAmount) : null,
            startDate: raw.startDate || null,
            endDate: raw.endDate || null,
        };
    }

    // Turns an API failure into something the administrator can act on. The
    // server answers in English; what reaches the screen is always in Spanish.
    private messageFor(error: HttpErrorResponse, action: string): string {
        if (error.status === 409) {
            return 'El jugador ya posee una bonificación activa. Editala o cancelala antes de asignar otra.';
        }
        if (error.status === 400) {
            return 'Los datos de la bonificación no son válidos. Revisá el motivo y el valor ingresado.';
        }
        if (error.status === 401 || error.status === 403) {
            return 'Tu sesión no tiene permiso para esta acción. Volvé a iniciar sesión.';
        }
        if (error.status === 404) {
            return 'El jugador no tiene una bonificación activa.';
        }
        if (error.status === 0) {
            return 'No se pudo conectar con el servidor. Verificá que la API esté levantada.';
        }

        return `No se pudo ${action} la bonificación. Intentá nuevamente.`;
    }

    // ===== TABLE RENDERING =====

    // Validity is resolved by the backend, with the same criteria as the badge
    isActive(row: DiscountModel): boolean {
        return row.isActive;
    }

    benefitValueOf(row: DiscountModel): string {
        return formatBenefitValue(row);
    }

    // ISO dates are shown the way they are read locally. An empty value means
    // the benefit has no end date.
    formatDate(isoDate: string): string {
        if (!isoDate) {
            return '—';
        }
        const [year, month, day] = isoDate.split('-');
        return `${day}/${month}/${year}`;
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
