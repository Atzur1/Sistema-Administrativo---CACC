import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Observable, Subject, from } from 'rxjs';

import { PlayerRoster } from './player-roster';
import { PlayerAccountModel } from '../../../models/PlayerAccountModel';
import { PagosService } from '../../../services/pagos';

// HU-029: roster of players with the "Alumnos Deudores" filter. The HTTP service is replaced by a
// test double, so these tests need neither the API nor the database. Who really owes something is
// decided by the SQL query, which is checked against the database by the Postman collection
// "HU-029 - Filtro de Jugadores Deudores"; here only what the screen does with the answer is covered.

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

const ABAD = account(1, 'ABAD', 'JUAN', 0);
const SANCHEZ = account(2, 'SÁNCHEZ', 'BAUTISTA', 255000, { pendingInstallments: 3 });
const CORREAS = account(3, 'CORREAS', 'JUAN', 85000);

const EVERYONE = [ABAD, SANCHEZ, CORREAS];
const DEBTORS = [SANCHEZ, CORREAS];

interface Options {
    answer?: (onlyDebtors: boolean) => PlayerAccountModel[] | 'error' | Observable<PlayerAccountModel[]>;
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
const lastNames = (el: HTMLElement) => rows(el).map((row) => text(row.querySelectorAll('td')[1]));
const button = (el: HTMLElement, selector: string) => el.querySelector(selector) as HTMLButtonElement;

function search(el: HTMLElement, term: string, fixture: ComponentFixture<unknown>) {
    const input = el.querySelector('.roster-search') as HTMLInputElement;
    input.value = term;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
}

describe('PlayerRoster - Alumnos Deudores filter', () => {
    describe('loading', () => {
        it('asks for the whole roster when it opens', async () => {
            const { service } = await create();

            expect(service.getPlayerAccounts).toHaveBeenCalledTimes(1);
            expect(service.getPlayerAccounts).toHaveBeenCalledWith(false);
        });

        it('lists every player with name, last name, DNI and category', async () => {
            const { el } = await create();

            expect(rows(el)).toHaveLength(3);
            const first = rows(el)[1].querySelectorAll('td');
            expect(text(first[0])).toBe('BAUTISTA');
            expect(text(first[1])).toBe('SÁNCHEZ');
            expect(text(first[2])).toBe('99000002');
            expect(text(first[3])).toBe('AFA 20067');
        });

        it('shows the amount owed in pesos with the number of installments', async () => {
            const { el } = await create();

            const cell = rows(el)[1].querySelectorAll('td')[4];
            expect(text(cell)).toContain('255.000');
            expect(text(cell)).toContain('3 cuotas');
        });

        it('writes "Al día" for a player that owes nothing', async () => {
            const { el } = await create();

            expect(text(rows(el)[0].querySelectorAll('td')[4])).toBe('Al día');
        });

        it('shows a loading message until the answer arrives', () => {
            const pending = new Subject<PlayerAccountModel[]>();
            return create({ answer: () => pending }).then(({ el }) => {
                expect(text(el.querySelector('.roster-state'))).toBe('Cargando alumnos...');
                expect(rows(el)).toHaveLength(0);
            });
        });
    });

    describe('the filter', () => {
        it('is off at first and the toggle is labelled "Alumnos Deudores"', async () => {
            const { el } = await create();

            const toggle = button(el, '.roster-toggle');
            expect(text(toggle)).toBe('Alumnos Deudores');
            expect(toggle.getAttribute('aria-pressed')).toBe('false');
            expect(el.querySelector('.roster-clear')).toBeNull();
        });

        it('asks the API for debtors only when it is turned on', async () => {
            const { fixture, el, service } = await create();

            button(el, '.roster-toggle').click();
            await settle(fixture);

            expect(service.getPlayerAccounts).toHaveBeenLastCalledWith(true);
            expect(button(el, '.roster-toggle').getAttribute('aria-pressed')).toBe('true');
        });

        it('shows only what the API returned for debtors', async () => {
            const { fixture, el } = await create();

            button(el, '.roster-toggle').click();
            await settle(fixture);

            expect(lastNames(el)).toEqual(['SÁNCHEZ', 'CORREAS']);
        });

        it('does not sneak in players the API did not send', async () => {
            const { fixture, el } = await create({ answer: (onlyDebtors) => (onlyDebtors ? [CORREAS] : EVERYONE) });

            button(el, '.roster-toggle').click();
            await settle(fixture);

            expect(lastNames(el)).toEqual(['CORREAS']);
        });

        it('shows the total owed by the listed debtors', async () => {
            const { fixture, el } = await create();

            button(el, '.roster-toggle').click();
            await settle(fixture);

            expect(text(el.querySelector('.roster-summary'))).toBe('2 alumnos deudores · Total adeudado $ 340.000');
        });

        it('turns off when the toggle is pressed again', async () => {
            const { fixture, el, service } = await create();

            button(el, '.roster-toggle').click();
            await settle(fixture);
            button(el, '.roster-toggle').click();
            await settle(fixture);

            expect(service.getPlayerAccounts).toHaveBeenLastCalledWith(false);
            expect(rows(el)).toHaveLength(3);
        });
    });

    describe('clearing the filter ("Ver todos")', () => {
        it('appears only while the filter is on', async () => {
            const { fixture, el } = await create();
            expect(el.querySelector('.roster-clear')).toBeNull();

            button(el, '.roster-toggle').click();
            await settle(fixture);

            expect(text(el.querySelector('.roster-clear'))).toBe('Ver todos');
        });

        it('brings back the whole roster', async () => {
            const { fixture, el, service } = await create();
            button(el, '.roster-toggle').click();
            await settle(fixture);

            button(el, '.roster-clear').click();
            await settle(fixture);

            expect(service.getPlayerAccounts).toHaveBeenLastCalledWith(false);
            expect(button(el, '.roster-toggle').getAttribute('aria-pressed')).toBe('false');
            expect(rows(el)).toHaveLength(3);
            expect(el.querySelector('.roster-clear')).toBeNull();
        });

        it('also empties the search box', async () => {
            const { fixture, el } = await create();
            search(el, 'correas', fixture);
            expect(rows(el)).toHaveLength(1);

            button(el, '.roster-clear').click();
            await settle(fixture);

            expect((el.querySelector('.roster-search') as HTMLInputElement).value).toBe('');
            expect(rows(el)).toHaveLength(3);
        });
    });

    describe('empty states', () => {
        it('says there are no overdue students when the filter finds nobody', async () => {
            const { fixture, el } = await create({ answer: (onlyDebtors) => (onlyDebtors ? [] : EVERYONE) });

            button(el, '.roster-toggle').click();
            await settle(fixture);

            expect(text(el.querySelector('.roster-empty'))).toBe('No se registran alumnos con cuotas atrasadas en este momento.');
            expect(rows(el)).toHaveLength(0);
        });

        it('does not call an empty roster "no debtors"', async () => {
            const { el } = await create({ answer: () => [] });

            expect(text(el.querySelector('.roster-empty'))).toBe('Todavía no hay alumnos cargados.');
        });

        it('offers "Ver todos" from the empty state of the filter', async () => {
            const { fixture, el } = await create({ answer: (onlyDebtors) => (onlyDebtors ? [] : EVERYONE) });

            button(el, '.roster-toggle').click();
            await settle(fixture);

            expect(el.querySelector('.roster-clear')).not.toBeNull();
        });
    });

    describe('search', () => {
        it('finds a player by name ignoring accents and case', async () => {
            const { fixture, el } = await create();

            search(el, 'sanchez', fixture);

            expect(lastNames(el)).toEqual(['SÁNCHEZ']);
        });

        it('finds a player by DNI', async () => {
            const { fixture, el } = await create();

            search(el, '99000003', fixture);

            expect(lastNames(el)).toEqual(['CORREAS']);
        });

        it('says so when nobody matches', async () => {
            const { fixture, el } = await create();

            search(el, 'zzz', fixture);

            expect(text(el.querySelector('.roster-empty'))).toBe('Ningún alumno coincide con la búsqueda.');
        });

        it('searches inside the debtors when the filter is on', async () => {
            const { fixture, el } = await create();
            button(el, '.roster-toggle').click();
            await settle(fixture);

            search(el, 'juan', fixture);

            expect(lastNames(el)).toEqual(['CORREAS']);
        });
    });

    describe('paging', () => {
        const MANY = Array.from({ length: 12 }, (_, index) => account(index + 1, `APELLIDO${index + 1}`, 'X', 1000));

        it('shows 10 players per page', async () => {
            const { el } = await create({ answer: () => MANY });

            expect(rows(el)).toHaveLength(10);
            expect(text(el.querySelector('.roster-page-info'))).toBe('Página 1 de 2');
            expect(button(el, '.roster-page-btn').disabled).toBe(true);
        });

        it('moves to the second page with the rest of the players', async () => {
            const { fixture, el } = await create({ answer: () => MANY });

            (el.querySelectorAll('.roster-page-btn')[1] as HTMLButtonElement).click();
            fixture.detectChanges();

            expect(lastNames(el)).toEqual(['APELLIDO11', 'APELLIDO12']);
            expect(text(el.querySelector('.roster-page-info'))).toBe('Página 2 de 2');
        });

        it('goes back to the first page when the filter changes', async () => {
            const { fixture, el, component } = await create({ answer: () => MANY });
            (el.querySelectorAll('.roster-page-btn')[1] as HTMLButtonElement).click();
            fixture.detectChanges();

            button(el, '.roster-toggle').click();
            await settle(fixture);

            expect(component.page).toBe(1);
        });

        it('hides the pager when everything fits in one page', async () => {
            const { el } = await create();

            expect(el.querySelector('.roster-pager')).toBeNull();
        });
    });

    describe('errors', () => {
        it('shows a clear message and a retry button when the API fails', async () => {
            const { el } = await create({ answer: () => 'error' });

            expect(text(el.querySelector('.roster-error'))).toContain('No se pudo cargar el listado de alumnos.');
            expect(el.querySelector('.roster-error')?.getAttribute('role')).toBe('alert');
            expect(text(el.querySelector('.roster-retry'))).toBe('Reintentar');
            expect(rows(el)).toHaveLength(0);
        });

        it('asks again, with the same filter, when "Reintentar" is pressed', async () => {
            let failing = true;
            const { fixture, el, service } = await create({
                answer: (onlyDebtors) => (failing ? 'error' : onlyDebtors ? DEBTORS : EVERYONE),
            });
            failing = false;

            button(el, '.roster-retry').click();
            await settle(fixture);

            expect(service.getPlayerAccounts).toHaveBeenCalledTimes(2);
            expect(el.querySelector('.roster-error')).toBeNull();
            expect(rows(el)).toHaveLength(3);
        });

        it('never leaves the previous list on screen after a failed filter change', async () => {
            const { fixture, el } = await create({ answer: (onlyDebtors) => (onlyDebtors ? 'error' : EVERYONE) });

            button(el, '.roster-toggle').click();
            await settle(fixture);

            expect(rows(el)).toHaveLength(0);
            expect(el.querySelector('.roster-error')).not.toBeNull();
        });
    });

    describe('freshness', () => {
        it('reloads with the current filter when the screen asks for it (after a payment)', async () => {
            const { fixture, component, el, service } = await create();
            button(el, '.roster-toggle').click();
            await settle(fixture);

            component.reload();
            await settle(fixture);

            expect(service.getPlayerAccounts).toHaveBeenCalledTimes(3);
            expect(service.getPlayerAccounts).toHaveBeenLastCalledWith(true);
        });

        it('ignores a slow answer that arrives after a newer request', async () => {
            const slow = new Subject<PlayerAccountModel[]>();
            const fast = new Subject<PlayerAccountModel[]>();
            const { fixture, el, service } = await create({ answer: (onlyDebtors) => (onlyDebtors ? fast : slow) });

            // The first request (whole roster) is still in flight when the filter is turned on
            button(el, '.roster-toggle').click();
            fast.next(DEBTORS);
            slow.next(EVERYONE);
            await settle(fixture);

            expect(service.getPlayerAccounts).toHaveBeenCalledTimes(2);
            expect(lastNames(el)).toEqual(['SÁNCHEZ', 'CORREAS']);
        });
    });

    describe('navigation', () => {
        it('opens the debt detail of a player that owes', async () => {
            const { el, navigate } = await create();

            (rows(el)[1].querySelector('.roster-link') as HTMLButtonElement).click();

            expect(navigate).toHaveBeenCalledWith(['/admin/portal/jugadores', 2, 'deuda']);
        });

        it('opens the profile of a player that is up to date', async () => {
            const { el, navigate } = await create();

            (rows(el)[0].querySelector('.roster-link') as HTMLButtonElement).click();

            expect(navigate).toHaveBeenCalledWith(['/admin/portal/jugadores', 1]);
        });
    });
});
