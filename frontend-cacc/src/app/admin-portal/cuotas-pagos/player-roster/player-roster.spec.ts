import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Observable, Subject, from } from 'rxjs';

import { PlayerRoster } from './player-roster';
import { PlayerAccountModel } from '../../../models/PlayerAccountModel';
import { RecentPaymentModel } from '../../../models/RecentPaymentModel';
import { PagosService } from '../../../services/pagos';

// HU-029: single panel of Cuotas y Pagos with the tabs Deudores / Todos / Últimos pagos. The HTTP
// service is replaced by a test double, so these tests need neither the API nor the database. Who
// really owes something is decided by the SQL query, which is checked against the database by the
// Postman collection "HU-029 - Filtro de Jugadores Deudores"; here only what the screen does with
// the answer is covered.

function account(id: number, lastName: string, firstName: string, amountOwed: number, extra: Partial<PlayerAccountModel> = {}): PlayerAccountModel {
    return {
        playerId: id,
        firstName,
        lastName,
        dni: `9900000${id}`.slice(-8),
        category: 'AFA 20067',
        amountOwed,
        pendingInstallments: amountOwed > 0 ? 1 : 0,
        ...extra,
    };
}

function payment(id: number, name: string, extra: Partial<RecentPaymentModel> = {}): RecentPaymentModel {
    return { id, idJugador: id * 10, initials: 'XX', name, method: 'Transferencia', amount: '$ 85.000', elapsed: 'Hace 2 días', ...extra };
}

const ABAD = account(1, 'ABAD', 'JUAN', 0);
const SANCHEZ = account(2, 'SÁNCHEZ', 'BAUTISTA', 255000, { pendingInstallments: 3, category: 'AFA 20068' });
const CORREAS = account(3, 'CORREAS', 'JUAN', 85000);

const EVERYONE = [ABAD, SANCHEZ, CORREAS];
const DEBTORS = [SANCHEZ, CORREAS];

const PAYMENTS = [payment(1, 'PRUEBA, MATIAS'), payment(2, 'GUZMÁN, LUCIA', { method: 'Efectivo', amount: '$ 92.000', elapsed: 'Ayer' })];

interface Options {
    answer?: (onlyDebtors: boolean) => PlayerAccountModel[] | 'error' | Observable<PlayerAccountModel[]>;
    payments?: RecentPaymentModel[];
}

// Answers arrive asynchronously, as in the real app: the component calls detectChanges() inside
// its callbacks.
const reply = (value: PlayerAccountModel[] | 'error') =>
    from(value === 'error' ? Promise.reject(new Error('failure')) : Promise.resolve(value));

async function create(options: Options = {}) {
    const answer = options.answer ?? ((onlyDebtors: boolean) => (onlyDebtors ? DEBTORS : EVERYONE));
    const service = {
        getPlayerAccounts: vi.fn((onlyDebtors: boolean) => {
            const result = answer(onlyDebtors);
            return result instanceof Observable ? result : reply(result);
        }),
    };

    await TestBed.configureTestingModule({
        imports: [PlayerRoster],
        providers: [provideRouter([]), { provide: PagosService, useValue: service }],
    }).compileComponents();

    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    const fixture: ComponentFixture<PlayerRoster> = TestBed.createComponent(PlayerRoster);
    fixture.componentRef.setInput('payments', options.payments ?? PAYMENTS);
    fixture.detectChanges();
    await settle(fixture);

    return { fixture, component: fixture.componentInstance, service, navigate, el: fixture.nativeElement as HTMLElement };
}

async function settle(fixture: ComponentFixture<unknown>) {
    await fixture.whenStable();
    fixture.detectChanges();
}

const text = (el: Element | null) => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();
const rows = (el: HTMLElement) => Array.from(el.querySelectorAll('tbody tr'));
const cells = (el: HTMLElement, column: number) => rows(el).map((row) => text(row.querySelectorAll('td')[column]));
const lastNames = (el: HTMLElement) => cells(el, 1);
const tab = (el: HTMLElement, id: string) => el.querySelector(`#tab-${id}`) as HTMLButtonElement;
const button = (el: HTMLElement, selector: string) => el.querySelector(selector) as HTMLButtonElement;
const headerButton = (el: HTMLElement, label: string) =>
    Array.from(el.querySelectorAll('.roster-sort')).find((b) => text(b).startsWith(label)) as HTMLButtonElement;

async function openTab(fixture: ComponentFixture<unknown>, el: HTMLElement, id: string) {
    tab(el, id).click();
    await settle(fixture);
}

async function search(el: HTMLElement, term: string, fixture: ComponentFixture<unknown>) {
    const input = el.querySelector('.roster-search') as HTMLInputElement;
    input.value = term;
    input.dispatchEvent(new Event('input'));
    await settle(fixture);
}

// n players, with debt on the first `owing` ones, spread over two categories
function crowd(n: number, owing: number): PlayerAccountModel[] {
    return Array.from({ length: n }, (_, i) =>
        account(i + 1, `APELLIDO${String(i + 1).padStart(2, '0')}`, `NOMBRE${i + 1}`, i < owing ? 92000 * ((i % 3) + 1) : 0, {
            category: i % 2 === 0 ? 'AFA 20067' : 'AFA 20068',
        })
    );
}

describe('PlayerRoster - panel of debtors, everyone and latest payments', () => {
    describe('loading', () => {
        it('asks the API for debtors and for the whole roster when it opens', async () => {
            const { service } = await create();

            expect(service.getPlayerAccounts).toHaveBeenCalledTimes(2);
            expect(service.getPlayerAccounts).toHaveBeenCalledWith(true);
            expect(service.getPlayerAccounts).toHaveBeenCalledWith(false);
        });

        it('opens on the Deudores tab', async () => {
            const { el, component } = await create();

            expect(component.activeTab).toBe('debtors');
            expect(tab(el, 'debtors').getAttribute('aria-selected')).toBe('true');
            expect(tab(el, 'all').getAttribute('aria-selected')).toBe('false');
        });

        it('shows how many there are on each tab', async () => {
            const { el } = await create();

            expect(text(tab(el, 'debtors'))).toBe('Deudores 2');
            expect(text(tab(el, 'all'))).toBe('Todos 3');
            expect(text(tab(el, 'payments'))).toBe('Últimos pagos 2');
        });

        it('shows a loading message until the answer arrives', async () => {
            const pending = new Subject<PlayerAccountModel[]>();
            const { el } = await create({ answer: () => pending });

            expect(text(el.querySelector('.roster-state'))).toBe('Cargando alumnos...');
            expect(rows(el)).toHaveLength(0);
        });
    });

    describe('Deudores tab', () => {
        it('lists only what the API returned for debtors, biggest debt first', async () => {
            const { el } = await create();

            expect(lastNames(el)).toEqual(['SÁNCHEZ', 'CORREAS']);
        });

        it('does not sneak in players the API did not send', async () => {
            const { el } = await create({ answer: (onlyDebtors) => (onlyDebtors ? [CORREAS] : EVERYONE) });

            expect(lastNames(el)).toEqual(['CORREAS']);
        });

        it('shows name, last name, DNI and category', async () => {
            const { el } = await create();

            const first = rows(el)[0].querySelectorAll('td');
            expect(text(first[0])).toBe('BAUTISTA');
            expect(text(first[1])).toBe('SÁNCHEZ');
            expect(text(first[2])).toBe('99000002');
            expect(text(first[3])).toBe('AFA 20068');
        });

        it('shows the amount owed in pesos with the number of installments', async () => {
            const { el } = await create();

            const cell = cells(el, 4)[0];
            expect(cell).toContain('255.000');
            expect(cell).toContain('3 cuotas');
            expect(cells(el, 4)[1]).toContain('1 cuota');
            expect(cells(el, 4)[1]).not.toContain('1 cuotas');
        });

        it('shows the total owed by the listed debtors', async () => {
            const { el } = await create();

            expect(text(el.querySelector('.roster-total'))).toBe('Total adeudado $ 340.000');
            expect(text(el.querySelector('.roster-summary'))).toBe('2 alumnos deudores');
        });

        it('uses the singular for a single debtor', async () => {
            const { el } = await create({ answer: (onlyDebtors) => (onlyDebtors ? [CORREAS] : EVERYONE) });

            expect(text(el.querySelector('.roster-summary'))).toBe('1 alumno deudor');
        });

        it('says so in green when nobody owes anything', async () => {
            const { el } = await create({ answer: (onlyDebtors) => (onlyDebtors ? [] : EVERYONE) });

            const message = el.querySelector('.roster-state') as HTMLElement;
            expect(text(message)).toBe('No se registran alumnos con cuotas atrasadas en este momento.');
            expect(message.classList.contains('roster-empty')).toBe(true);
            expect(rows(el)).toHaveLength(0);
        });
    });

    describe('Todos tab', () => {
        it('lists every player, ordered by last name', async () => {
            const { fixture, el } = await create();

            await openTab(fixture, el, 'all');

            expect(lastNames(el)).toEqual(['ABAD', 'CORREAS', 'SÁNCHEZ']);
        });

        it('does not call the API again to change tab', async () => {
            const { fixture, el, service } = await create();

            await openTab(fixture, el, 'all');
            await openTab(fixture, el, 'debtors');

            expect(service.getPlayerAccounts).toHaveBeenCalledTimes(2);
        });

        it('writes "Al día" for a player that owes nothing', async () => {
            const { fixture, el } = await create();

            await openTab(fixture, el, 'all');

            expect(cells(el, 4)[0]).toBe('Al día');
        });

        it('summarises how many owe and how many are up to date', async () => {
            const { fixture, el } = await create();

            await openTab(fixture, el, 'all');

            expect(text(el.querySelector('.roster-summary'))).toBe('3 alumnos · 2 con deuda · 1 al día');
            expect(el.querySelector('.roster-total')).toBeNull();
        });

        it('tells when there are no players at all', async () => {
            const { fixture, el } = await create({ answer: () => [] });

            await openTab(fixture, el, 'all');

            expect(text(el.querySelector('.roster-state'))).toBe('Todavía no hay alumnos cargados.');
        });
    });

    describe('search', () => {
        it('finds a player by name', async () => {
            const { fixture, el } = await create();
            await openTab(fixture, el, 'all');

            await search(el, 'bautista', fixture);

            expect(lastNames(el)).toEqual(['SÁNCHEZ']);
        });

        it('ignores accents and case', async () => {
            const { fixture, el } = await create();
            await openTab(fixture, el, 'all');

            await search(el, 'sanchez', fixture);

            expect(lastNames(el)).toEqual(['SÁNCHEZ']);
        });

        it('finds a player by DNI', async () => {
            const { fixture, el } = await create();
            await openTab(fixture, el, 'all');

            await search(el, '99000003', fixture);

            expect(lastNames(el)).toEqual(['CORREAS']);
        });

        it('searches inside the debtors only', async () => {
            const { fixture, el } = await create();

            await search(el, 'abad', fixture);

            expect(rows(el)).toHaveLength(0);
            expect(text(el.querySelector('.roster-state'))).toBe('Ningún alumno coincide con la búsqueda.');
        });

        it('is shared by the tabs', async () => {
            const { fixture, el } = await create();

            await search(el, 'correas', fixture);
            await openTab(fixture, el, 'all');

            expect((el.querySelector('.roster-search') as HTMLInputElement).value).toBe('correas');
            expect(lastNames(el)).toEqual(['CORREAS']);
        });

        it('"Limpiar filtros" appears only with a filter and brings everything back', async () => {
            const { fixture, el } = await create();
            expect(el.querySelector('.roster-clear')).toBeNull();

            await search(el, 'correas', fixture);
            expect(text(el.querySelector('.roster-clear'))).toBe('Limpiar filtros');

            button(el, '.roster-clear').click();
            await settle(fixture);

            expect((el.querySelector('.roster-search') as HTMLInputElement).value).toBe('');
            expect(lastNames(el)).toEqual(['SÁNCHEZ', 'CORREAS']);
            expect(el.querySelector('.roster-clear')).toBeNull();
        });
    });

    describe('category filter', () => {
        it('offers "Todas las categorías" and the categories the club really has', async () => {
            const { component } = await create();

            expect(component.categories).toEqual(['Todas las categorías', 'AFA 20067', 'AFA 20068']);
        });

        it('keeps only the players of the chosen category', async () => {
            const { fixture, el, component } = await create();
            await openTab(fixture, el, 'all');

            component.categoryControl.setValue('AFA 20068');
            await settle(fixture);

            expect(lastNames(el)).toEqual(['SÁNCHEZ']);
        });

        it('works together with the search', async () => {
            const { fixture, el, component } = await create();
            await openTab(fixture, el, 'all');

            component.categoryControl.setValue('AFA 20067');
            await search(el, 'abad', fixture);

            expect(lastNames(el)).toEqual(['ABAD']);
        });

        it('recomputes the total owed for the chosen category', async () => {
            const { fixture, el, component } = await create();

            component.categoryControl.setValue('AFA 20068');
            await settle(fixture);

            expect(text(el.querySelector('.roster-total'))).toBe('Total adeudado $ 255.000');
        });

        it('is undone by "Limpiar filtros"', async () => {
            const { fixture, el, component } = await create();

            component.categoryControl.setValue('AFA 20068');
            await settle(fixture);
            button(el, '.roster-clear').click();
            await settle(fixture);

            expect(component.categoryControl.value).toBe('Todas las categorías');
            expect(lastNames(el)).toEqual(['SÁNCHEZ', 'CORREAS']);
        });

        it('is not offered on the payments tab', async () => {
            const { fixture, el } = await create();

            await openTab(fixture, el, 'payments');

            expect(el.querySelector('.roster-category')).toBeNull();
        });
    });

    describe('sorting', () => {
        it('starts by amount owed, biggest first, on Deudores', async () => {
            const { el, component } = await create();

            expect(component.sortKey).toBe('amountOwed');
            expect(component.sortDirection).toBe('desc');
            expect(headerButton(el, 'Monto adeudado').closest('th')?.getAttribute('aria-sort')).toBe('descending');
        });

        it('starts by last name on Todos', async () => {
            const { fixture, el, component } = await create();

            await openTab(fixture, el, 'all');

            expect(component.sortKey).toBe('lastName');
            expect(headerButton(el, 'Apellido').closest('th')?.getAttribute('aria-sort')).toBe('ascending');
        });

        it('reverses the order when the same column is pressed again', async () => {
            const { fixture, el } = await create();
            await openTab(fixture, el, 'all');

            headerButton(el, 'Apellido').click();
            await settle(fixture);

            expect(lastNames(el)).toEqual(['SÁNCHEZ', 'CORREAS', 'ABAD']);
        });

        it('sorts by amount owed, biggest first, the first time', async () => {
            const { fixture, el } = await create();
            await openTab(fixture, el, 'all');

            headerButton(el, 'Monto adeudado').click();
            await settle(fixture);

            expect(lastNames(el)).toEqual(['SÁNCHEZ', 'CORREAS', 'ABAD']);
        });

        it('sorts by category', async () => {
            const { fixture, el } = await create();
            await openTab(fixture, el, 'all');

            headerButton(el, 'Categoría').click();
            await settle(fixture);

            expect(cells(el, 3)).toEqual(['AFA 20067', 'AFA 20067', 'AFA 20068']);
        });

        it('sorts by name', async () => {
            const { fixture, el } = await create();
            await openTab(fixture, el, 'all');

            headerButton(el, 'Nombre').click();
            await settle(fixture);

            expect(cells(el, 0)).toEqual(['BAUTISTA', 'JUAN', 'JUAN']);
        });

        it('shows an arrow on the sorted column and a neutral one on the others', async () => {
            const { fixture, el } = await create();
            await openTab(fixture, el, 'all');

            expect(text(headerButton(el, 'Apellido'))).toBe('Apellido ▲');
            expect(text(headerButton(el, 'Monto adeudado'))).toBe('Monto adeudado ↕');
        });

        it('goes back to the first page after sorting', async () => {
            const { fixture, el, component } = await create({ answer: () => crowd(25, 10) });
            await openTab(fixture, el, 'all');
            button(el, '.roster-pager .roster-page-btn:last-child').click();
            await settle(fixture);
            expect(component.page).toBe(2);

            headerButton(el, 'Categoría').click();
            await settle(fixture);

            expect(component.page).toBe(1);
        });
    });

    describe('paging', () => {
        it('shows no pager while everything fits in a page', async () => {
            const { el } = await create();

            expect(el.querySelector('.roster-pager')).toBeNull();
        });

        it('shows 10 rows at a time', async () => {
            const { fixture, el } = await create({ answer: () => crowd(25, 12) });
            await openTab(fixture, el, 'all');

            expect(rows(el)).toHaveLength(10);
            expect(text(el.querySelector('.roster-page-info'))).toBe('Página 1 de 3');
            expect(text(el.querySelector('.roster-summary'))).toBe('Mostrando 1-10 de 25 alumnos · 12 con deuda · 13 al día');
        });

        it('moves between pages and stops at both ends', async () => {
            const { fixture, el } = await create({ answer: () => crowd(25, 12) });
            await openTab(fixture, el, 'all');
            const [previous, next] = Array.from(el.querySelectorAll('.roster-page-btn')) as HTMLButtonElement[];
            expect(previous.disabled).toBe(true);

            next.click();
            await settle(fixture);
            expect(lastNames(el)[0]).toBe('APELLIDO11');

            next.click();
            await settle(fixture);
            expect(rows(el)).toHaveLength(5);
            expect(next.disabled).toBe(true);

            previous.click();
            await settle(fixture);
            expect(text(el.querySelector('.roster-page-info'))).toBe('Página 2 de 3');
        });

        it('goes back to the first page when the search changes', async () => {
            const { fixture, el, component } = await create({ answer: () => crowd(25, 12) });
            await openTab(fixture, el, 'all');
            button(el, '.roster-pager .roster-page-btn:last-child').click();
            await settle(fixture);
            expect(component.page).toBe(2);

            await search(el, 'apellido2', fixture);

            expect(component.page).toBe(1);
        });

        it('keeps the pager and the total together on Deudores', async () => {
            const { el } = await create({ answer: (onlyDebtors) => (onlyDebtors ? crowd(12, 12) : crowd(30, 12)) });

            expect(rows(el)).toHaveLength(10);
            expect(text(el.querySelector('.roster-page-info'))).toBe('Página 1 de 2');
            expect(text(el.querySelector('.roster-summary'))).toBe('Mostrando 1-10 de 12 alumnos deudores');
            expect(el.querySelector('.roster-total')).not.toBeNull();
        });
    });

    describe('Últimos pagos tab', () => {
        it('lists the payments the screen passed in', async () => {
            const { fixture, el } = await create();

            await openTab(fixture, el, 'payments');

            expect(rows(el)).toHaveLength(2);
            const first = rows(el)[0].querySelectorAll('td');
            expect(text(first[0])).toBe('PRUEBA, MATIAS');
            expect(text(first[1])).toBe('Transferencia');
            expect(text(first[2])).toBe('Hace 2 días');
            expect(text(first[3])).toBe('$ 85.000');
        });

        it('searches by name, ignoring accents', async () => {
            const { fixture, el } = await create();
            await openTab(fixture, el, 'payments');

            await search(el, 'guzman', fixture);

            expect(rows(el)).toHaveLength(1);
            expect(text(rows(el)[0].querySelectorAll('td')[0])).toBe('GUZMÁN, LUCIA');
        });

        it('asks for a name only, because a payment row has no DNI', async () => {
            const { fixture, el } = await create();
            await openTab(fixture, el, 'payments');

            expect((el.querySelector('.roster-search') as HTMLInputElement).placeholder).toBe('Buscar por nombre...');
        });

        it('says so when no payment matches', async () => {
            const { fixture, el } = await create();
            await openTab(fixture, el, 'payments');

            await search(el, 'zzz', fixture);

            expect(text(el.querySelector('.roster-state'))).toBe('Ningún pago coincide con la búsqueda.');
        });

        it('says so when there are no payments yet', async () => {
            const { fixture, el } = await create({ payments: [] });

            await openTab(fixture, el, 'payments');

            expect(text(el.querySelector('.roster-state'))).toBe('Todavía no se registraron pagos.');
        });

        it('follows the list when the screen refreshes it', async () => {
            const { fixture, el } = await create();
            await openTab(fixture, el, 'payments');

            fixture.componentRef.setInput('payments', [...PAYMENTS, payment(3, 'ROJAS, ANA')]);
            await settle(fixture);

            expect(rows(el)).toHaveLength(3);
            expect(text(tab(el, 'payments'))).toBe('Últimos pagos 3');
        });

        it('does not depend on the players request', async () => {
            const { fixture, el } = await create({ answer: () => 'error' });

            await openTab(fixture, el, 'payments');

            expect(rows(el)).toHaveLength(2);
            expect(el.querySelector('.roster-error')).toBeNull();
        });
    });

    describe('navigation', () => {
        it('a debtor opens the detail of what he or she owes', async () => {
            const { el, navigate } = await create();

            button(el, '.roster-link').click();

            expect(navigate).toHaveBeenCalledWith(['/admin/portal/jugadores', 2, 'deuda']);
        });

        it('a player up to date opens the profile', async () => {
            const { fixture, el, navigate } = await create();
            await openTab(fixture, el, 'all');

            button(el, '.roster-link').click();

            expect(navigate).toHaveBeenCalledWith(['/admin/portal/jugadores', 1]);
        });

        it('a payment opens the profile of who paid', async () => {
            const { fixture, el, navigate } = await create();
            await openTab(fixture, el, 'payments');

            button(el, '.roster-link').click();

            expect(navigate).toHaveBeenCalledWith(['/admin/portal/jugadores', 10]);
        });
    });

    describe('when things go wrong', () => {
        it('shows an error with a retry button', async () => {
            const { el } = await create({ answer: () => 'error' });

            expect(text(el.querySelector('.roster-error'))).toContain('No se pudo cargar el listado de alumnos.');
            expect(text(button(el, '.roster-retry'))).toBe('Reintentar');
            expect(rows(el)).toHaveLength(0);
        });

        it('asks again when "Reintentar" is pressed', async () => {
            let fail = true;
            const { fixture, el, service } = await create({ answer: (onlyDebtors) => (fail ? 'error' : onlyDebtors ? DEBTORS : EVERYONE) });
            fail = false;

            button(el, '.roster-retry').click();
            await settle(fixture);

            expect(service.getPlayerAccounts).toHaveBeenCalledTimes(4);
            expect(el.querySelector('.roster-error')).toBeNull();
            expect(lastNames(el)).toEqual(['SÁNCHEZ', 'CORREAS']);
        });

        it('shows the error on Todos too', async () => {
            const { fixture, el } = await create({ answer: () => 'error' });

            await openTab(fixture, el, 'all');

            expect(el.querySelector('.roster-error')).not.toBeNull();
        });
    });

    describe('after a payment', () => {
        it('reload() asks both lists again so a paid debt disappears', async () => {
            let paid = false;
            const { fixture, el, component, service } = await create({
                answer: (onlyDebtors) => (onlyDebtors ? (paid ? [CORREAS] : DEBTORS) : EVERYONE),
            });
            expect(lastNames(el)).toEqual(['SÁNCHEZ', 'CORREAS']);

            paid = true;
            component.reload();
            await settle(fixture);

            expect(service.getPlayerAccounts).toHaveBeenCalledTimes(4);
            expect(lastNames(el)).toEqual(['CORREAS']);
            expect(text(tab(el, 'debtors'))).toBe('Deudores 1');
        });

        it('keeps the tab and the search it had', async () => {
            const { fixture, el, component } = await create();
            await openTab(fixture, el, 'all');
            await search(el, 'juan', fixture);

            component.reload();
            await settle(fixture);

            expect(component.activeTab).toBe('all');
            expect(lastNames(el)).toEqual(['ABAD', 'CORREAS']);
        });

        it('forgets a category that no longer exists', async () => {
            let list = EVERYONE;
            const { fixture, component } = await create({ answer: (onlyDebtors) => (onlyDebtors ? DEBTORS : list) });
            component.categoryControl.setValue('AFA 20068');

            list = [ABAD, CORREAS];
            component.reload();
            await settle(fixture);

            expect(component.categoryControl.value).toBe('Todas las categorías');
        });

        it('discards a slow answer that arrives after a newer one', async () => {
            const slow = new Subject<PlayerAccountModel[]>();
            const fast = new Subject<PlayerAccountModel[]>();
            let calls = 0;
            const { fixture, el, component } = await create({
                answer: () => {
                    calls++;
                    return calls <= 2 ? slow : fast;
                },
            });

            component.reload();
            fast.next([CORREAS]);
            fast.complete();
            await settle(fixture);
            slow.next([SANCHEZ]);
            slow.complete();
            await settle(fixture);

            expect(lastNames(el)).toEqual(['CORREAS']);
        });
    });
});
