import { Component, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AbstractControl, FormBuilder, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';

// A player that can be picked in the payment form
interface Player {
    id: number;
    name: string;
    document: string;
    category: string;
}

// One row in the "Pendientes de cobro" panel
interface PendingRow {
    initials: string;
    name: string;
    category: string;
    amount: string;
    installments: string;
}

// One row in the "Últimos pagos" panel
interface PaymentRow {
    initials: string;
    name: string;
    method: string;
    amount: string;
    elapsed: string;
}

@Component({
    selector: 'app-cuotas-pagos',
    standalone: true,
    imports: [CommonModule, ReactiveFormsModule],
    templateUrl: './cuotas-pagos.html',
    styleUrl: './cuotas-pagos.css',
})
export class CuotasPagos implements OnDestroy {

    // Header
    headerMetrics = [
        { value: '$4.2M', label: 'Recaudado 2026' },
        { value: '126', label: 'Pagos del mes' },
        { value: '65', label: 'Pendientes' },
    ];

    // Payment form
    paymentForm: FormGroup;

    periods = [
        'Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio',
        'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre',
    ];

    methods = ['Transferencia', 'Efectivo'];

    // Squad the search field looks up — fictional demo data
    private players: Player[] = [
        { id: 1, name: 'Sánchez, Bautista D.', document: '48.221.107', category: 'Pt preAFA 2015' },
        { id: 2, name: 'Correas, Juan V.', document: '48.310.564', category: 'Pt preAFA 2015' },
        { id: 3, name: 'Guzmán, Tomás V.', document: '46.902.338', category: 'Pt AFA 2012' },
        { id: 4, name: 'Aliendro, Brian E.', document: '46.115.720', category: 'Pt AFA 2011' },
        { id: 5, name: 'Acosta, Ciro F.', document: '47.508.291', category: 'Pt AFA 2013' },
        { id: 6, name: 'Baigorrí, Ángel A.', document: '47.664.183', category: 'Pt AFA 2013' },
        { id: 7, name: 'López, Tobías A.', document: '46.740.955', category: 'Pt AFA 2012' },
        { id: 8, name: 'Cuqueio, Juan C.', document: '45.988.402', category: 'Pt AFA 2010' },
    ];

    // Suggestions shown under the search field
    matchingPlayers: Player[] = [];
    showSuggestions = false;

    // Inline confirmation shown after a simulated submit
    successMessage = '';
    successLeaving = false;

    // Pending fees, waiting to be collected
    pendingRows: PendingRow[] = [
        { initials: 'SB', name: 'Sánchez, Bautista D.', category: 'Pt preAFA 2015', amount: '$255.000', installments: '3 cuotas' },
        { initials: 'CJ', name: 'Correas, Juan V.', category: 'Pt preAFA 2015', amount: '$255.000', installments: '3 cuotas' },
        { initials: 'GT', name: 'Guzmán, Tomás V.', category: 'Pt AFA 2012', amount: '$85.000', installments: '1 cuota' },
        { initials: 'AB', name: 'Aliendro, Brian E.', category: 'Pt AFA 2011', amount: '$85.000', installments: '1 cuota' },
    ];

    // Most recent payments registered in the system
    paymentRows: PaymentRow[] = [
        { initials: 'AC', name: 'Acosta, Ciro F.', method: 'Transferencia', amount: '$85.000', elapsed: 'Hace 5 min' },
        { initials: 'BA', name: 'Baigorrí, Ángel A.', method: 'Efectivo', amount: '$85.000', elapsed: 'Hace 12 min' },
        { initials: 'LT', name: 'López, Tobías A.', method: 'Efectivo', amount: '$85.000', elapsed: 'Hoy, 09:40' },
        { initials: 'CJ', name: 'Cuqueio, Juan C.', method: 'Transferencia', amount: '$85.000', elapsed: 'Ayer, 18:20' },
    ];

    // Timers for the confirmation message, cleared on destroy so leaving the
    // dashboard mid-animation never fires a callback on a dead component
    private fadeTimer?: ReturnType<typeof setTimeout>;
    private clearTimer?: ReturnType<typeof setTimeout>;

    constructor(private fb: FormBuilder) {
        this.paymentForm = this.fb.group({
            player: ['', [Validators.required, this.knownPlayerValidator]],
            period: ['', [Validators.required]],
            amount: ['', [Validators.required, Validators.min(1)]],
            method: ['', [Validators.required]],
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
        this.paymentForm.patchValue({ player: player.name });
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
        this.showConfirmation(`Pago de ${playerName} registrado correctamente.`);
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
