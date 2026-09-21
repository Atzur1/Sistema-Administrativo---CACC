import { ChangeDetectorRef, Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { PlayerAccountModel } from '../../../models/PlayerAccountModel';
import { PagosService } from '../../../services/pagos';
import { normalizeText } from '../../../shared/normalize-text';

const CURRENCY = new Intl.NumberFormat('es-AR', {
    style: 'currency',
    currency: 'ARS',
    maximumFractionDigits: 0,
});

// Roster of every player of the club with what each one owes (HU-029). The "Alumnos Deudores"
// filter is resolved by the API, so the table only ever shows what the server sent back.
@Component({
    selector: 'app-player-roster',
    standalone: true,
    imports: [CommonModule, ReactiveFormsModule],
    templateUrl: './player-roster.html',
    styleUrl: './player-roster.css',
})
export class PlayerRoster implements OnInit, OnDestroy {
    readonly pageSize = 10;

    searchControl = new FormControl('', { nonNullable: true });

    onlyDebtors = false;
    loading = false;
    errorMessage = '';

    // Everything the API returned for the current filter, and the part of it that matches the search
    accounts: PlayerAccountModel[] = [];
    matching: PlayerAccountModel[] = [];
    page = 1;

    private request?: Subscription;
    private searchChanges?: Subscription;

    constructor(
        private pagosService: PagosService,
        private router: Router,
        private cdr: ChangeDetectorRef
    ) {}

    ngOnInit() {
        this.searchChanges = this.searchControl.valueChanges.subscribe(() => {
            this.page = 1;
            this.applySearch();
            this.cdr.detectChanges();
        });
        this.load();
    }

    ngOnDestroy() {
        this.request?.unsubscribe();
        this.searchChanges?.unsubscribe();
    }

    get visibleRows(): PlayerAccountModel[] {
        const start = (this.page - 1) * this.pageSize;
        return this.matching.slice(start, start + this.pageSize);
    }

    get totalPages(): number {
        return Math.max(1, Math.ceil(this.matching.length / this.pageSize));
    }

    get totalOwed(): number {
        return this.matching.reduce((total, account) => total + account.amountOwed, 0);
    }

    get summary(): string {
        const count = this.matching.length;
        const noun = this.onlyDebtors
            ? count === 1 ? 'alumno deudor' : 'alumnos deudores'
            : count === 1 ? 'alumno' : 'alumnos';
        return `${count} ${noun}`;
    }

    get emptyMessage(): string {
        if (this.accounts.length === 0) {
            return this.onlyDebtors
                ? 'No se registran alumnos con cuotas atrasadas en este momento.'
                : 'Todavía no hay alumnos cargados.';
        }
        return 'Ningún alumno coincide con la búsqueda.';
    }

    toggleDebtors() {
        this.onlyDebtors = !this.onlyDebtors;
        this.page = 1;
        this.load();
    }

    // "Ver todos": back to the whole roster, without any filter or search
    showAll() {
        this.onlyDebtors = false;
        this.page = 1;
        this.searchControl.setValue('', { emitEvent: false });
        this.load();
    }

    // Called by the screen after a payment, so the roster never shows a debt that was just paid
    reload() {
        this.load();
    }

    goToPreviousPage() {
        if (this.page > 1) {
            this.page--;
        }
    }

    goToNextPage() {
        if (this.page < this.totalPages) {
            this.page++;
        }
    }

    formatAmount(amount: number): string {
        return CURRENCY.format(amount);
    }

    // A debtor goes straight to the detail of what he or she owes; an up to date player to the profile
    openPlayer(account: PlayerAccountModel) {
        const segments = ['/admin/portal/jugadores', account.playerId];
        if (account.amountOwed > 0) {
            segments.push('deuda');
        }
        this.router.navigate(segments);
    }

    private load() {
        // A newer request replaces the one in flight, so a slow answer can never overwrite a fresh one
        this.request?.unsubscribe();
        this.loading = true;
        this.errorMessage = '';

        this.request = this.pagosService.getPlayerAccounts(this.onlyDebtors).subscribe({
            next: (accounts) => {
                this.accounts = accounts;
                this.applySearch();
                this.loading = false;
                this.cdr.detectChanges();
            },
            error: () => {
                this.accounts = [];
                this.matching = [];
                this.loading = false;
                this.errorMessage = 'No se pudo cargar el listado de alumnos. Verificá la conexión con el servidor e intentá de nuevo.';
                this.cdr.detectChanges();
            },
        });
    }

    private applySearch() {
        const needle = normalizeText(this.searchControl.value.trim());
        const digits = needle.replace(/\D/g, '');

        this.matching = needle
            ? this.accounts.filter(
                  (account) =>
                      normalizeText(`${account.lastName} ${account.firstName}`).includes(needle) ||
                      (digits.length > 0 && account.dni.replace(/\D/g, '').includes(digits))
              )
            : this.accounts;

        this.page = Math.min(this.page, this.totalPages);
    }
}
