import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AbstractControl, FormBuilder, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { DiscountBadge } from '../../shared/discount-badge/discount-badge';
import { NotificationService } from '../../shared/notifications/notification.service';
import { DiscountService } from '../../services/discounts';
import {
    BenefitValueType,
    DiscountModel,
    DiscountRequest,
    formatBenefitValue,
    formatIsoDate,
    formatValidity,
    statusLabel,
    statusToneClass,
} from '../../models/DiscountModel';
import { PagosService, JugadorResumen } from '../../services/pagos';
import { normalizeText } from '../../shared/normalize-text';

// Player shape this screen was originally built against (repo de Atzur1).
// Se mantiene local en vez de traer PlayerModel/PlayerService: Laura ya tiene
// su propio JugadoresService/JugadorResumen, así que solo se mapea una vez acá
// y el resto del archivo (y el HTML) sigue usando los mismos nombres de
// siempre (fullName, document, category, id) sin tocar nada más.
interface PlayerModel {
    id: number;
    fullName: string;
    document: string;
    category: string;
}

function toPlayerModel(jugador: JugadorResumen): PlayerModel {
    return {
        id: jugador.idJugador,
        fullName: jugador.nombreCompleto,
        document: jugador.dni,
        category: jugador.categoria,
    };
}

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
export class BecadosDescuentos implements OnInit {

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

    // Every benefit of the player the popup is about: what expired, what runs
    // today and what is scheduled. Since HU-012 a player can hold several.
    playerDiscounts = signal<DiscountModel[]>([]);

    // Every benefit ever granted: the table mixes current and cancelled ones.
    // benefitsLoaded tells "still loading" apart from "there is nothing to show".
    benefitRows = signal<DiscountModel[]>([]);
    benefitsLoaded = signal(false);

    // Active discounts indexed by player, resolved by the backend.
    // Held in a signal so the table repaints when the response arrives.
    private discounts = signal(new Map<number, DiscountModel>());

    constructor(
        private fb: FormBuilder,
        private discountService: DiscountService,
        private pagosService: PagosService,
        private notifications: NotificationService
    ) {
        this.lookupForm = this.fb.group({
            player: ['', [Validators.required, this.knownPlayerValidator]],
        });

        this.benefitForm = this.fb.group({
            reason: ['', [Validators.required]],
            valueType: ['', [Validators.required]],
            percentage: [null as number | null],
            fixedAmount: [null as number | null],
            // Mandatory since HU-012: no benefit without an authorised period
            startDate: ['', [Validators.required]],
            endDate: ['', [Validators.required]],
        }, { validators: [this.benefitValueValidator, this.dateRangeValidator] });
    }

    ngOnInit() {
        this.loadDiscountMap();
        this.loadBenefitRows();

        this.pagosService.getJugadores().subscribe({
            next: (jugadores) => this.players.set(jugadores.map(toPlayerModel)),
            error: () => this.players.set([]),
        });

        this.discountService.getDiscountTypes().subscribe({
            next: (reasons) => this.benefitReasons.set(reasons),
            // Without the catalogue the form cannot offer a valid reason, so the
            // popup says so instead of showing an empty dropdown.
            error: () => this.benefitReasons.set([]),
        });
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
        });
    }

    private openDialog(player: PlayerModel) {
        this.dialogPlayer.set(player);
        this.dialogError.set('');
        this.currentDiscount.set(null);
        this.playerDiscounts.set([]);
        this.dialogView.set('loading');
        this.dialogOpen.set(true);

        // The popup always asks the API what the player holds right now: the grid
        // may be stale and a benefit assigned from another screen has to show up.
        // Since HU-012 that is a list, not one record, and each item carries the
        // state the server resolved.
        this.discountService.getDiscountsByPlayer(player.id).subscribe({
            next: (discounts) => {
                this.playerDiscounts.set(discounts);

                // The one that applies today, or failing that the next scheduled
                // one: that is what the card shows and what editing acts on.
                const current = discounts.find(benefit => benefit.status === 'Active')
                    ?? discounts.find(benefit => benefit.status === 'Scheduled')
                    ?? null;

                this.currentDiscount.set(current);

                if (current === null) {
                    // Nothing in force or coming: straight to the assignment form
                    this.resetBenefitForm();
                    this.dialogView.set('form');
                    return;
                }

                this.dialogView.set('active');
            },
            error: () => {
                this.dialogError.set('No se pudo cargar la bonificación del jugador. Intentá de nuevo.');
                this.resetBenefitForm();
                this.dialogView.set('form');
            },
        });
    }

    closeDialog() {
        this.dialogOpen.set(false);
        this.dialogError.set('');
        this.saving.set(false);
    }

    // Switches the popup from showing a benefit to editing it. Without an
    // argument it edits the one on the card; the history list passes the one
    // that was clicked.
    startEditing(discount: DiscountModel | null = null) {
        const target = discount ?? this.currentDiscount();
        if (target === null) {
            return;
        }

        this.currentDiscount.set(target);

        this.benefitForm.reset({
            reason: target.type,
            valueType: target.valueType,
            percentage: target.percentage,
            fixedAmount: target.fixedAmount,
            startDate: target.startDate,
            endDate: target.endDate,
        });

        this.dialogError.set('');
        this.dialogView.set('form');
    }

    // Grants an additional benefit for a period the player does not have covered.
    // Possible since HU-012: what the API refuses is an overlapping range, not a
    // second benefit.
    startNewBenefit() {
        this.currentDiscount.set(null);
        this.resetBenefitForm();
        this.dialogError.set('');
        this.dialogView.set('form');
    }

    askCancelConfirmation() {
        this.dialogError.set('');
        this.dialogView.set('confirmCancel');
    }

    // Back out of the form. With benefits already loaded it returns to the card;
    // with none there is nothing to go back to, so the popup closes.
    backToActive() {
        this.dialogError.set('');

        if (this.playerDiscounts().length === 0) {
            this.closeDialog();
            return;
        }

        this.dialogView.set('active');
    }

    // True when the form is editing something that already exists
    get hasBenefits(): boolean {
        return this.playerDiscounts().length > 0;
    }

    // Opens the form with a sensible period already filled: from today to the
    // end of the year, which is the cycle the club grants benefits for. Both are
    // editable; they are a starting point, not a decision.
    private resetBenefitForm() {
        this.benefitForm.reset({
            reason: '',
            valueType: '',
            percentage: null,
            fixedAmount: null,
            startDate: this.today(),
            endDate: this.endOfYear(),
        });
    }

    // Local date of the browser, used only to prefill the field. What the state
    // of a benefit is gets decided by the server, never here.
    private today(): string {
        const now = new Date();
        const month = `${now.getMonth() + 1}`.padStart(2, '0');
        const day = `${now.getDate()}`.padStart(2, '0');
        return `${now.getFullYear()}-${month}-${day}`;
    }

    private endOfYear(): string {
        return `${new Date().getFullYear()}-12-31`;
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

    // The end has to be strictly later than the start: a range that opens and
    // closes the same day is not a period the club grants. Lives on the group,
    // not on a single field, so it re-runs when either date changes and the
    // error shows the moment the second one is picked.
    //
    // ISO strings compare correctly as text (yyyy-MM-dd sorts chronologically),
    // so there is no Date involved and no time zone to get wrong.
    private dateRangeValidator = (group: AbstractControl): ValidationErrors | null => {
        const start = group.get('startDate')?.value;
        const end = group.get('endDate')?.value;

        if (!start || !end) {
            return null;
        }

        return end <= start ? { endNotAfterStart: true } : null;
    };

    // The date range message, sitting next to the two date fields.
    //
    // It does not wait for the form to be touched or submitted: as soon as both
    // dates carry a value and the range is wrong, the administrator sees why.
    // That is the point of HU-012, catching it while the dates are being picked
    // and not after pressing a button that was never going to work.
    get dateRangeErrorMessage(): string {
        const errors = this.benefitForm.errors ?? {};
        return errors['endNotAfterStart']
            ? 'La fecha de finalización debe ser posterior a la fecha de inicio.'
            : '';
    }

    // Single message for the rest of the form, shown above the actions
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
        if (this.benefitForm.get('startDate')?.invalid) {
            return 'Indicá la fecha desde la que rige la bonificación.';
        }
        if (this.benefitForm.get('endDate')?.invalid) {
            return 'Indicá la fecha hasta la que rige la bonificación.';
        }

        // The range message has its own place, next to the dates
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

        const editingId = this.currentDiscount()?.id;

        const call = editing
            ? this.discountService.updateDiscount(player.id, request, editingId)
            : this.discountService.assignDiscount(player.id, request);

        call.subscribe({
            next: () => {
                this.saving.set(false);
                this.closeDialog();
                // Never trust the payload just sent: the grid is rebuilt from the
                // API so what the screen shows is what the database stored.
                this.refreshFromServer();
                // The toast fires here and nowhere else: only a response that
                // actually arrived counts as a success.
                this.notify(editing
                    ? 'Bonificación actualizada correctamente.'
                    : 'Bonificación asignada correctamente.');
            },
            error: (error: HttpErrorResponse) => {
                this.saving.set(false);
                const mensaje = this.messageFor(error, editing ? 'editar' : 'guardar');
                this.dialogError.set(mensaje);
                this.notifications.notify(mensaje, 'error');
            },
        });
    }

    // El único punto donde el admin descarta a propósito lo que estaba
    // completando: por eso es el único lugar de esta pantalla que dispara la
    // notificación de "cambios no guardados", a diferencia de cerrar el popup
    // desde la tarjeta activa (ahí no había nada que perder).
    cancelForm() {
        this.notifications.notify('Cambios no guardados: se canceló la operación.', 'cancelled');

        if (this.hasBenefits) {
            this.backToActive();
        } else {
            this.closeDialog();
        }
    }

    confirmCancelBenefit() {
        const player = this.dialogPlayer();
        if (player === null || this.saving()) {
            return;
        }

        this.saving.set(true);
        this.dialogError.set('');

        this.discountService.cancelDiscount(player.id, this.currentDiscount()?.id).subscribe({
            next: () => {
                this.saving.set(false);
                this.closeDialog();
                this.refreshFromServer();
                this.notify('Bonificación cancelada correctamente.');
            },
            error: (error: HttpErrorResponse) => {
                this.saving.set(false);
                this.dialogView.set('active');
                const mensaje = this.messageFor(error, 'cancelar');
                this.dialogError.set(mensaje);
                this.notifications.notify(mensaje, 'error');
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
            // Both are required by the form, so by this point they carry a value
            startDate: raw.startDate,
            endDate: raw.endDate,
        };
    }

    // Turns an API failure into something the administrator can act on. The
    // server answers in English; what reaches the screen is always in Spanish.
    private messageFor(error: HttpErrorResponse, action: string): string {
        if (error.status === 409) {
            // The API names the benefit in the way and its period, which is what
            // the administrator needs to fix the dates. It answers in English,
            // so only the period is lifted out of it.
            const period = this.periodFromConflict(error);
            return period
                ? `El jugador ya tiene una bonificación para ese período (${period}). Ajustá las fechas o cancelá la existente.`
                : 'El jugador ya tiene una bonificación que se superpone con ese período. Ajustá las fechas o cancelá la existente.';
        }
        if (error.status === 400) {
            return 'Los datos de la bonificación no son válidos. Revisá el motivo, el valor y las fechas de vigencia.';
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

    // Pulls the two ISO dates out of the conflict message and reads them the way
    // the screen does. If the wording ever changes, this returns empty and the
    // caller falls back to the generic message instead of showing garbage.
    private periodFromConflict(error: HttpErrorResponse): string {
        const body = typeof error.error === 'string' ? error.error : '';
        const dates = body.match(/\d{4}-\d{2}-\d{2}/g);

        return dates && dates.length >= 2
            ? `${formatIsoDate(dates[0])} - ${formatIsoDate(dates[1])}`
            : '';
    }

    // ===== TABLE RENDERING =====

    // Everything below reads the state the server resolved. The browser never
    // works out whether a benefit is in force: its clock is not the club's.

    statusOf(row: DiscountModel): string {
        return statusLabel(row.status);
    }

    statusClassOf(row: DiscountModel): string {
        return statusToneClass(row.status);
    }

    benefitValueOf(row: DiscountModel): string {
        return formatBenefitValue(row);
    }

    // "01/10/2026 - 31/12/2026"
    validityOf(row: DiscountModel): string {
        return formatValidity(row);
    }

    formatDate(isoDate: string): string {
        return formatIsoDate(isoDate) || '—';
    }

    // Only called after the API confirmed the write
    private notify(message: string) {
        this.notifications.notify(message, 'success');
    }
}
