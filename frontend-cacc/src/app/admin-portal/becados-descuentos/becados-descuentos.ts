import { Component, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AbstractControl, FormBuilder, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';

// A player that can be picked in the benefit form
interface Player {
    id: number;
    name: string;
    document: string;
    category: string;
}

// One row in the "Beneficios asignados" table
interface BenefitRow {
    id: number;
    initials: string;
    name: string;
    category: string;
    // 'scholarship' renders the "Beca" pill, 'discount' the "Descuento" one
    type: 'scholarship' | 'discount';
    typeLabel: string;
    // Free text: the administrator decides whether it is a percentage or a fixed amount
    benefit: string;
    // Stored as ISO so the dates compare and sort directly
    validFrom: string;
    validTo: string;
}

@Component({
    selector: 'app-becados-descuentos',
    standalone: true,
    imports: [CommonModule, ReactiveFormsModule],
    templateUrl: './becados-descuentos.html',
    styleUrl: './becados-descuentos.css',
})
export class BecadosDescuentos implements OnDestroy {

    // Header
    headerMetrics = [
        { value: '18', label: 'Beneficios activos' },
        { value: '7', label: 'Becas activas' },
        { value: '11', label: 'Descuentos activos' },
    ];

    // Assignment form
    benefitForm: FormGroup;

    benefitTypes = ['Beca', 'Descuento'];

    // Squad the search field looks up — fictional demo data
    private players: Player[] = [
        { id: 1, name: 'Sánchez, Bautista D.', document: '48.221.107', category: 'Pt preAFA 2015' },
        { id: 2, name: 'Sánchez, Thiago N.', document: '47.903.612', category: 'Pt AFA 2013' },
        { id: 3, name: 'Correas, Juan V.', document: '48.310.564', category: 'Pt preAFA 2015' },
        { id: 4, name: 'Guzmán, Tomás V.', document: '46.902.338', category: 'Pt AFA 2012' },
        { id: 5, name: 'Guzmán, Lautaro I.', document: '48.455.019', category: 'Pt preAFA 2015' },
        { id: 6, name: 'Aliendro, Brian E.', document: '46.115.720', category: 'Pt AFA 2011' },
        { id: 7, name: 'Acosta, Ciro F.', document: '47.508.291', category: 'Pt AFA 2013' },
        { id: 8, name: 'Baigorrí, Ángel A.', document: '47.664.183', category: 'Pt AFA 2013' },
        { id: 9, name: 'López, Tobías A.', document: '46.740.955', category: 'Pt AFA 2012' },
        { id: 10, name: 'López, Valentín R.', document: '47.221.884', category: 'Pt AFA 2013' },
        { id: 11, name: 'Cuqueio, Juan C.', document: '45.988.402', category: 'Pt AFA 2010' },
        { id: 12, name: 'Peralta, Ignacio M.', document: '46.377.145', category: 'Pt AFA 2011' },
    ];

    // Suggestions shown under the search field
    matchingPlayers: Player[] = [];
    showSuggestions = false;

    // Inline confirmation shown after a simulated submit
    successMessage = '';
    successLeaving = false;

    // Every benefit ever granted: the table mixes current and expired ones
    benefitRows: BenefitRow[] = [
        { id: 1, initials: 'SB', name: 'Sánchez, Bautista D.', category: 'Pt preAFA 2015', type: 'discount', typeLabel: 'Descuento', benefit: '30%', validFrom: '2026-03-01', validTo: '2026-12-31' },
        { id: 2, initials: 'ST', name: 'Sánchez, Thiago N.', category: 'Pt AFA 2013', type: 'discount', typeLabel: 'Descuento', benefit: '30%', validFrom: '2026-03-01', validTo: '2026-12-31' },
        { id: 3, initials: 'GT', name: 'Guzmán, Tomás V.', category: 'Pt AFA 2012', type: 'scholarship', typeLabel: 'Beca', benefit: '100%', validFrom: '2026-02-01', validTo: '2026-12-31' },
        { id: 4, initials: 'GL', name: 'Guzmán, Lautaro I.', category: 'Pt preAFA 2015', type: 'discount', typeLabel: 'Descuento', benefit: '$25.000', validFrom: '2026-02-01', validTo: '2026-12-31' },
        { id: 5, initials: 'AB', name: 'Aliendro, Brian E.', category: 'Pt AFA 2011', type: 'scholarship', typeLabel: 'Beca', benefit: '50%', validFrom: '2026-04-01', validTo: '2026-11-30' },
        { id: 6, initials: 'AC', name: 'Acosta, Ciro F.', category: 'Pt AFA 2013', type: 'discount', typeLabel: 'Descuento', benefit: '$40.000', validFrom: '2025-03-01', validTo: '2025-12-31' },
        { id: 7, initials: 'BA', name: 'Baigorrí, Ángel A.', category: 'Pt AFA 2013', type: 'scholarship', typeLabel: 'Beca', benefit: '100%', validFrom: '2025-02-15', validTo: '2025-12-15' },
        { id: 8, initials: 'LT', name: 'López, Tobías A.', category: 'Pt AFA 2012', type: 'discount', typeLabel: 'Descuento', benefit: '20%', validFrom: '2026-01-15', validTo: '2026-12-31' },
        { id: 9, initials: 'LV', name: 'López, Valentín R.', category: 'Pt AFA 2013', type: 'discount', typeLabel: 'Descuento', benefit: '20%', validFrom: '2026-01-15', validTo: '2026-12-31' },
        { id: 10, initials: 'CJ', name: 'Cuqueio, Juan C.', category: 'Pt AFA 2010', type: 'scholarship', typeLabel: 'Beca', benefit: '75%', validFrom: '2025-06-01', validTo: '2026-05-31' },
        { id: 11, initials: 'PI', name: 'Peralta, Ignacio M.', category: 'Pt AFA 2011', type: 'discount', typeLabel: 'Descuento', benefit: '$30.000', validFrom: '2026-05-01', validTo: '2027-04-30' },
        { id: 12, initials: 'CJ', name: 'Correas, Juan V.', category: 'Pt preAFA 2015', type: 'scholarship', typeLabel: 'Beca', benefit: '100%', validFrom: '2024-03-01', validTo: '2024-12-31' },
    ];

    // Timers for the confirmation message, cleared on destroy so leaving the
    // dashboard mid-animation never fires a callback on a dead component
    private fadeTimer?: ReturnType<typeof setTimeout>;
    private clearTimer?: ReturnType<typeof setTimeout>;

    constructor(private fb: FormBuilder) {
        this.benefitForm = this.fb.group({
            player: ['', [Validators.required, this.knownPlayerValidator]],
            type: ['', [Validators.required]],
            benefit: ['', [Validators.required]],
            validFrom: ['', [Validators.required]],
            validTo: ['', [Validators.required]],
        });
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

    private findPlayer(value: string): Player | undefined {
        const needle = value.toLowerCase();
        return this.players.find(player => player.name.toLowerCase() === needle);
    }

    // One unified field: the same term is matched against name and document
    onPlayerSearch(term: string) {
        const needle = term.trim().toLowerCase();
        if (!needle) {
            this.matchingPlayers = [];
            this.showSuggestions = false;
            return;
        }

        // Digits are compared without dots so "48221" also finds "48.221.107"
        const digits = needle.replace(/\D/g, '');
        this.matchingPlayers = this.players.filter(player =>
            player.name.toLowerCase().includes(needle) ||
            (digits.length > 0 && player.document.replace(/\D/g, '').includes(digits))
        );
        this.showSuggestions = this.matchingPlayers.length > 0;
    }

    selectPlayer(player: Player) {
        this.benefitForm.patchValue({ player: player.name });
        this.matchingPlayers = [];
        this.showSuggestions = false;
    }

    hideSuggestions() {
        this.showSuggestions = false;
    }

    // A benefit is current while today still falls inside its validity range
    isActive(row: BenefitRow): boolean {
        const today = new Date().toISOString().slice(0, 10);
        return row.validFrom <= today && today <= row.validTo;
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
