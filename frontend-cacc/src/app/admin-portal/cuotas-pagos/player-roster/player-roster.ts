import { ChangeDetectorRef, Component, Input, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subscription, forkJoin } from 'rxjs';
import { PlayerAccountModel } from '../../../models/PlayerAccountModel';
import { RecentPaymentModel } from '../../../models/RecentPaymentModel';
import { PagosService } from '../../../services/pagos';
import { CustomSelect } from '../../../shared/custom-select/custom-select';
import { normalizeText } from '../../../shared/normalize-text';

const CURRENCY = new Intl.NumberFormat('es-AR', {
    style: 'currency',
    currency: 'ARS',
    maximumFractionDigits: 0,
});

const ALL_CATEGORIES = 'Todas las categorías';

export type RosterTab = 'debtors' | 'all' | 'payments';
export type SortKey = 'firstName' | 'lastName' | 'category' | 'amountOwed';
export type SortDirection = 'asc' | 'desc';

// Single panel of Cuotas y Pagos (HU-029): the debtors, the whole roster and the latest payments,
// one tab each. Who owes something is decided by the API (one call per list), so the screen only
// searches, sorts and pages what the server sent back.
@Component({
    selector: 'app-player-roster',
    standalone: true,
    imports: [CommonModule, ReactiveFormsModule, CustomSelect],
    templateUrl: './player-roster.html',
    styleUrl: './player-roster.css',
})
export class PlayerRoster implements OnInit, OnDestroy {
    // The latest payments belong to the screen that registers them, which refreshes them itself
    @Input() public payments: RecentPaymentModel[] = [];

    public readonly pageSize: number = 10;

    public searchControl = new FormControl('', { nonNullable: true });
    public categoryControl = new FormControl<string | number | null>(ALL_CATEGORIES);

    public activeTab: RosterTab = 'debtors';
    public sortKey: SortKey = 'amountOwed';
    public sortDirection: SortDirection = 'desc';

    public loading: boolean = false;
    public errorMessage: string = '';

    // Both lists as the API returned them, and what is left of the active one after search,
    // category and sort
    public debtors: PlayerAccountModel[] = [];
    public everyone: PlayerAccountModel[] = [];
    public categories: string[] = [ALL_CATEGORIES];
    public matching: PlayerAccountModel[] = [];
    public page: number = 1;

    private request?: Subscription;
    private filterChanges = new Subscription();

    constructor(
        private pagosService: PagosService,
        private router: Router,
        private cdr: ChangeDetectorRef
    ) {}

    public ngOnInit(): void {
        this.filterChanges.add(this.searchControl.valueChanges.subscribe(() => this.onFilterChange()));
        this.filterChanges.add(this.categoryControl.valueChanges.subscribe(() => this.onFilterChange()));
        this.load();
    }

    public ngOnDestroy(): void {
        this.request?.unsubscribe();
        this.filterChanges.unsubscribe();
    }

    public get isPaymentsTab(): boolean {
        return this.activeTab === 'payments';
    }

    // The payments tab is filtered here every time it is read, because the list arrives from outside
    public get matchingPayments(): RecentPaymentModel[] {
        const needle = normalizeText(this.searchControl.value.trim());
        if (!needle) {
            return this.payments;
        }
        return this.payments.filter((payment) => normalizeText(payment.name).includes(needle));
    }

    public get visibleRows(): PlayerAccountModel[] {
        const start = (this.page - 1) * this.pageSize;
        return this.matching.slice(start, start + this.pageSize);
    }

    public get totalPages(): number {
        return Math.max(1, Math.ceil(this.matching.length / this.pageSize));
    }

    public get totalOwed(): number {
        return this.matching.reduce((total, account) => total + account.amountOwed, 0);
    }

    public get hasActiveFilters(): boolean {
        return this.searchControl.value !== '' || this.selectedCategory !== ALL_CATEGORIES;
    }

    public get searchPlaceholder(): string {
        return this.isPaymentsTab ? 'Buscar por nombre...' : 'Buscar por nombre o DNI...';
    }

    // "Mostrando 1-10 de 47 alumnos · 18 con deuda · 29 al día"
    public get summary(): string {
        const count = this.matching.length;
        const noun = this.activeTab === 'debtors'
            ? (count === 1 ? 'alumno deudor' : 'alumnos deudores')
            : (count === 1 ? 'alumno' : 'alumnos');

        let text = `${count} ${noun}`;
        if (this.totalPages > 1) {
            const from = (this.page - 1) * this.pageSize + 1;
            const to = Math.min(this.page * this.pageSize, count);
            text = `Mostrando ${from}-${to} de ${count} ${noun}`;
        }

        if (this.activeTab === 'all') {
            const withDebt = this.matching.filter((account) => account.amountOwed > 0).length;
            text += ` · ${withDebt} con deuda · ${count - withDebt} al día`;
        }
        return text;
    }

    public get emptyMessage(): string {
        if (this.isPaymentsTab) {
            return this.payments.length === 0
                ? 'Todavía no se registraron pagos.'
                : 'Ningún pago coincide con la búsqueda.';
        }

        const source = this.activeTab === 'debtors' ? this.debtors : this.everyone;
        if (source.length === 0) {
            return this.activeTab === 'debtors'
                ? 'No se registran alumnos con cuotas atrasadas en este momento.'
                : 'Todavía no hay alumnos cargados.';
        }
        return 'Ningún alumno coincide con la búsqueda.';
    }

    // Debtors is the good-news screen when it is empty, so it gets the green message
    public get emptyIsGoodNews(): boolean {
        return this.activeTab === 'debtors' && this.debtors.length === 0;
    }

    public selectTab(tab: RosterTab): void {
        if (tab === this.activeTab) {
            return;
        }
        this.activeTab = tab;
        this.page = 1;
        this.resetSort();
        this.applyFilters();
    }

    public sortBy(key: SortKey): void {
        if (this.sortKey === key) {
            this.sortDirection = this.sortDirection === 'asc' ? 'desc' : 'asc';
        } else {
            this.sortKey = key;
            this.sortDirection = key === 'amountOwed' ? 'desc' : 'asc';
        }
        this.page = 1;
        this.applyFilters();
    }

    public sortIndicator(key: SortKey): string {
        if (this.sortKey !== key) {
            return '↕';
        }
        return this.sortDirection === 'asc' ? '▲' : '▼';
    }

    public ariaSort(key: SortKey): string {
        if (this.sortKey !== key) {
            return 'none';
        }
        return this.sortDirection === 'asc' ? 'ascending' : 'descending';
    }

    public clearFilters(): void {
        this.searchControl.setValue('', { emitEvent: false });
        this.categoryControl.setValue(ALL_CATEGORIES, { emitEvent: false });
        this.page = 1;
        this.applyFilters();
    }

    // Called by the screen after a payment, so the panel never shows a debt that was just paid
    public reload(): void {
        this.load();
    }

    public goToPreviousPage(): void {
        if (this.page > 1) {
            this.page--;
        }
    }

    public goToNextPage(): void {
        if (this.page < this.totalPages) {
            this.page++;
        }
    }

    public formatAmount(amount: number): string {
        return CURRENCY.format(amount);
    }

    // A debtor goes straight to the detail of what he or she owes; an up to date player to the profile
    public openPlayer(account: PlayerAccountModel): void {
        const segments: (string | number)[] = ['/admin/portal/jugadores', account.playerId];
        if (account.amountOwed > 0) {
            segments.push('deuda');
        }
        this.router.navigate(segments);
    }

    public openPaymentPlayer(payment: RecentPaymentModel): void {
        this.router.navigate(['/admin/portal/jugadores', payment.idJugador]);
    }

    private get selectedCategory(): string {
        return String(this.categoryControl.value ?? ALL_CATEGORIES);
    }

    private onFilterChange(): void {
        this.page = 1;
        this.applyFilters();
        this.cdr.detectChanges();
    }

    private load(): void {
        // A newer request replaces the one in flight, so a slow answer can never overwrite a fresh one
        this.request?.unsubscribe();
        this.loading = true;
        this.errorMessage = '';

        this.request = forkJoin({
            debtors: this.pagosService.getPlayerAccounts(true),
            everyone: this.pagosService.getPlayerAccounts(false),
        }).subscribe({
            next: ({ debtors, everyone }) => {
                this.debtors = debtors;
                this.everyone = everyone;
                this.buildCategories();
                this.applyFilters();
                this.loading = false;
                this.cdr.detectChanges();
            },
            error: () => {
                this.debtors = [];
                this.everyone = [];
                this.matching = [];
                this.loading = false;
                this.errorMessage = 'No se pudo cargar el listado de alumnos. Verificá la conexión con el servidor e intentá de nuevo.';
                this.cdr.detectChanges();
            },
        });
    }

    // The categories offered are the ones the club really has, so the selector never lists an empty one
    private buildCategories(): void {
        const names = Array.from(new Set(this.everyone.map((account) => account.category)))
            .filter((name) => name !== '')
            .sort((a, b) => a.localeCompare(b, 'es'));
        this.categories = [ALL_CATEGORIES, ...names];

        if (!this.categories.includes(this.selectedCategory)) {
            this.categoryControl.setValue(ALL_CATEGORIES, { emitEvent: false });
        }
    }

    private resetSort(): void {
        if (this.activeTab === 'debtors') {
            this.sortKey = 'amountOwed';
            this.sortDirection = 'desc';
        } else {
            this.sortKey = 'lastName';
            this.sortDirection = 'asc';
        }
    }

    private applyFilters(): void {
        if (this.isPaymentsTab) {
            this.matching = [];
            return;
        }

        const needle = normalizeText(this.searchControl.value.trim());
        const digits = needle.replace(/\D/g, '');
        const category = this.selectedCategory;
        const source = this.activeTab === 'debtors' ? this.debtors : this.everyone;

        const filtered = source.filter((account) => {
            const matchesCategory = category === ALL_CATEGORIES || account.category === category;
            const matchesSearch = !needle
                || normalizeText(`${account.lastName} ${account.firstName}`).includes(needle)
                || (digits.length > 0 && account.dni.replace(/\D/g, '').includes(digits));
            return matchesCategory && matchesSearch;
        });

        this.matching = this.sortAccounts(filtered);
        this.page = Math.min(this.page, this.totalPages);
    }

    private sortAccounts(accounts: PlayerAccountModel[]): PlayerAccountModel[] {
        const factor = this.sortDirection === 'asc' ? 1 : -1;
        const key = this.sortKey;

        return [...accounts].sort((a, b) => {
            const result = key === 'amountOwed'
                ? a.amountOwed - b.amountOwed
                : a[key].localeCompare(b[key], 'es');

            if (result !== 0) {
                return result * factor;
            }
            // Ties keep a stable, readable order
            return a.lastName.localeCompare(b.lastName, 'es') || a.firstName.localeCompare(b.firstName, 'es');
        });
    }
}
