import { Component, OnDestroy, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AbstractControl, FormBuilder, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { DiscountBadge } from '../../shared/discount-badge/discount-badge';
import { DiscountService } from '../../services/discounts';
import { DiscountModel } from '../../models/DiscountModel';
import { PlayerService } from '../../services/players';
import { PlayerModel } from '../../models/PlayerModel';
import { normalizeText } from '../../shared/normalize-text';

// One counter on the page header
interface HeaderMetric {
    value: string;
    label: string;
}

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

    // Assignment form
    benefitForm: FormGroup;

    benefitTypes = ['Beca', 'Descuento'];

    // Squad the lookup searches, loaded from the API on entry
    private players = signal<PlayerModel[]>([]);

    // Suggestions shown under the search field
    matchingPlayers: PlayerModel[] = [];
    showSuggestions = false;

    // Inline confirmation shown after a simulated submit
    successMessage = '';
    successLeaving = false;

    // Every benefit ever granted: the table mixes current and expired ones.
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
        this.benefitForm = this.fb.group({
            player: ['', [Validators.required, this.knownPlayerValidator]],
            type: ['', [Validators.required]],
            benefit: ['', [Validators.required]],
            validFrom: ['', [Validators.required]],
            validTo: ['', [Validators.required]],
        });
    }

    ngOnInit() {
        this.discountService.getDiscountMap().subscribe({
            next: (discountMap) => this.discounts.set(discountMap),
            // If the API fails the table still renders, just without badges
            error: () => this.discounts.set(new Map<number, DiscountModel>()),
        });

        this.playerService.getPlayers().subscribe({
            next: (players) => this.players.set(players),
            error: () => this.players.set([]),
        });

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

    private buildHeaderMetrics(benefits: DiscountModel[]): HeaderMetric[] {
        const active = benefits.filter(benefit => benefit.isActive).length;
        return [
            { value: benefits.length.toString(), label: 'Beneficios asignados' },
            { value: active.toString(), label: 'Vigentes' },
            { value: (benefits.length - active).toString(), label: 'No vigentes' },
        ];
    }

    initialsOf(fullName: string): string {
        const [lastName = '', firstName = ''] = fullName.split(',').map(part => part.trim());
        return ((lastName.charAt(0) || '') + (firstName.charAt(0) || '')).toUpperCase();
    }

    // Null cuando el jugador no tiene beneficio vigente: el badge no se dibuja
    getDiscount(playerId: number): DiscountModel | null {
        return this.discounts().get(playerId) ?? null;
    }

    ngOnDestroy() {
        clearTimeout(this.fadeTimer);
        clearTimeout(this.clearTimer);
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
        this.benefitForm.patchValue({ player: player.fullName });
        this.matchingPlayers = [];
        this.showSuggestions = false;
    }

    hideSuggestions() {
        this.showSuggestions = false;
    }

    // Validity is resolved by the backend, with the same criteria as the badge:
    // not deactivated and today inside the date range.
    isActive(row: DiscountModel): boolean {
        return row.isActive;
    }

    // ISO dates are shown the way they are read locally
    formatDate(isoDate: string): string {
        const [year, month, day] = isoDate.split('-');
        return `${day}/${month}/${year}`;
    }

    // No backend yet: the submit only simulates a successful assignment
    onSubmit() {
        if (this.benefitForm.invalid) {
            this.benefitForm.markAllAsTouched();
            return;
        }

        const playerName = this.benefitForm.value.player;
        this.benefitForm.reset({ player: '', type: '', benefit: '', validFrom: '', validTo: '' });
        this.matchingPlayers = [];
        this.showSuggestions = false;
        this.showConfirmation(`Beneficio de ${playerName} asignado correctamente.`);
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
