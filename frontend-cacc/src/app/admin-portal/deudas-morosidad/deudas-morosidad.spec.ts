import { ComponentFixture, TestBed } from '@angular/core/testing';
import { from } from 'rxjs';

import { DeudasMorosidad } from './deudas-morosidad';
import { PagosService, ResumenPagos } from '../../services/pagos';
import { ReportesService } from '../../services/reportes';
import { NotificationService } from '../../shared/notifications/notification.service';
import { PlayerAccountModel } from '../../models/PlayerAccountModel';

// HU-029: the debtors list in Deudas y Morosidad. The HTTP services are replaced by test
// doubles, so these tests need neither the API nor the database. The real query (balance
// greater than zero only) is covered by the Postman collection "HU-029 - Filtro de Jugadores Deudores".

const EMPTY_SUMMARY: ResumenPagos = {
    recaudadoAnioActual: 0,
    pagosDelMes: 0,
    cantidadPendientes: 0,
    deudaGlobalTotal: 0,
    jugadoresMorosos: 0,
};

function debtor(id: number, firstName: string, installments: number, amount: number): PlayerAccountModel {
    return {
        playerId: id,
        firstName: firstName,
        lastName: 'PRUEBA',
        dni: `9900000${id}`,
        category: 'AFA 20067',
        amountOwed: amount,
        pendingInstallments: installments,
    };
}

// Responses arrive asynchronously, as in the real app.
const response = <T>(value: T) => from(Promise.resolve(value));

async function create(debtors: PlayerAccountModel[]): Promise<ComponentFixture<DeudasMorosidad>> {
    const pagosService = {
        getPendientes: vi.fn(() => response([])),
        getResumen: vi.fn(() => response(EMPTY_SUMMARY)),
        getPlayerAccounts: vi.fn(() => response(debtors)),
        getDeudaPorCategoria: vi.fn(() => response([])),
    };

    await TestBed.configureTestingModule({
        imports: [DeudasMorosidad],
        providers: [
            { provide: PagosService, useValue: pagosService },
            { provide: ReportesService, useValue: {} },
            { provide: NotificationService, useValue: { notify: vi.fn() } },
        ],
    }).compileComponents();

    const fixture: ComponentFixture<DeudasMorosidad> = TestBed.createComponent(DeudasMorosidad);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
}

function textOf(fixture: ComponentFixture<DeudasMorosidad>): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
}

function tablePlayers(fixture: ComponentFixture<DeudasMorosidad>): string[] {
    const cells: NodeListOf<HTMLElement> = (fixture.nativeElement as HTMLElement).querySelectorAll('.cell-player');
    return Array.from(cells).map((cell) => cell.textContent?.trim() ?? '');
}

describe('DeudasMorosidad - debtors list (HU-029)', () => {
    it('shows the empty-state message when no student owes anything', async () => {
        const fixture = await create([]);

        expect(textOf(fixture)).toContain('No se registran alumnos con cuotas atrasadas en este momento.');
        expect(tablePlayers(fixture)).toEqual([]);
    });

    it('sorts by installments owed, most first, and by amount when tied', async () => {
        const fixture = await create([
            debtor(1, 'SANTIAGO', 1, 92000),
            debtor(2, 'NICOLAS', 3, 276000),
            debtor(3, 'MATIAS', 1, 120000),
            debtor(4, 'ALEXIS', 2, 184000),
        ]);

        expect(tablePlayers(fixture)).toEqual([
            'PRUEBA, NICOLAS',
            'PRUEBA, ALEXIS',
            'PRUEBA, MATIAS',
            'PRUEBA, SANTIAGO',
        ]);
    });

    it('tells a search with no matches apart from a club with no debtors', async () => {
        const fixture = await create([debtor(1, 'SANTIAGO', 1, 92000)]);
        const component: DeudasMorosidad = fixture.componentInstance;

        component.busqueda = 'zzz';
        component.onFiltrosChange();
        fixture.detectChanges();

        expect(textOf(fixture)).toContain('Ningún deudor coincide con la búsqueda o los filtros aplicados.');
        expect(textOf(fixture)).not.toContain('No se registran alumnos con cuotas atrasadas');
    });

    it('shows every debtor again after clearing the filters', async () => {
        const fixture = await create([debtor(1, 'SANTIAGO', 1, 92000), debtor(2, 'NICOLAS', 2, 184000)]);
        const component: DeudasMorosidad = fixture.componentInstance;

        component.busqueda = 'zzz';
        component.onFiltrosChange();
        component.limpiarFiltros();
        fixture.detectChanges();

        expect(tablePlayers(fixture)).toEqual(['PRUEBA, NICOLAS', 'PRUEBA, SANTIAGO']);
    });
});
