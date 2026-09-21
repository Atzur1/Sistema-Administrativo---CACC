import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { CuotasPagos } from './cuotas-pagos';
import { JugadorResumen, PagoReciente, PagosService, ResumenPagos } from '../../services/pagos';
import { ArancelesService } from '../../services/aranceles';

// Cuotas y Pagos: the "Deuda Global Total" indicator (HU-019) and the single panel of debtors,
// everyone and latest payments (HU-029). The services are test doubles, so no API is needed.

const RESUMEN: ResumenPagos = {
    recaudadoAnioActual: 85000,
    pagosDelMes: 1,
    cantidadPendientes: 3,
    deudaGlobalTotal: 276000,
    jugadoresMorosos: 3,
};

const RECIENTES: PagoReciente[] = [
    { idPago: 1, idJugador: 7, nombreCompleto: 'PRUEBA, MATIAS', metodoPago: 'Transferencia', monto: 85000, fechaPago: '2020-01-01T00:00:00' },
];

const JUGADOR: JugadorResumen = {
    idJugador: 7,
    nombre: 'MATIAS',
    apellido: 'PRUEBA',
    dni: '99000003',
    genero: 'Masculino',
    categoria: 'AFA 20067',
    nombreCompleto: 'PRUEBA, MATIAS',
};

async function create(resumen: ResumenPagos = RESUMEN) {
    const service = {
        getJugadores: vi.fn(() => of([JUGADOR])),
        getPendientes: vi.fn(() => of([])),
        getRecientes: vi.fn(() => of(RECIENTES)),
        getResumen: vi.fn(() => of(resumen)),
        getPlayerAccounts: vi.fn(() => of([])),
        registrarPago: vi.fn(() => of({})),
    };

    await TestBed.configureTestingModule({
        imports: [CuotasPagos],
        providers: [
            provideRouter([]),
            { provide: PagosService, useValue: service },
            { provide: ArancelesService, useValue: { getResumen: () => of({}) } },
        ],
    }).compileComponents();

    const fixture: ComponentFixture<CuotasPagos> = TestBed.createComponent(CuotasPagos);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    return { fixture, component: fixture.componentInstance, service, el: fixture.nativeElement as HTMLElement };
}

const text = (el: Element | null) => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

describe('CuotasPagos', () => {
    describe('Deuda Global Total banner', () => {
        it('shows the total owed and how many players owe it', async () => {
            const { el } = await create();

            const banner = el.querySelector('.deuda-global-banner');
            expect(text(banner)).toContain('Deuda Global Total');
            expect(text(banner)).toContain('$ 276.000');
            expect(text(banner)).toContain('3 jugadores morosos');
            expect(banner?.classList.contains('is-clear')).toBe(false);
        });

        it('uses the singular for a single debtor', async () => {
            const { el } = await create({ ...RESUMEN, jugadoresMorosos: 1 });

            expect(text(el.querySelector('.deuda-global-banner'))).toContain('1 jugador moroso');
        });

        it('turns green when nobody owes anything', async () => {
            const { el } = await create({ ...RESUMEN, deudaGlobalTotal: 0, jugadoresMorosos: 0 });

            expect(el.querySelector('.deuda-global-banner')?.classList.contains('is-clear')).toBe(true);
        });
    });

    describe('single panel', () => {
        it('replaces the old panels: one roster and no separate lists', async () => {
            const { el } = await create();

            expect(el.querySelectorAll('app-player-roster')).toHaveLength(1);
            expect(el.querySelector('.panel-pending')).toBeNull();
            expect(el.querySelector('.panel-payments')).toBeNull();
        });

        it('gives the latest payments to the Últimos pagos tab', async () => {
            const { fixture, el } = await create();

            (el.querySelector('#tab-payments') as HTMLButtonElement).click();
            fixture.detectChanges();
            await fixture.whenStable();
            fixture.detectChanges();

            expect(text(el.querySelector('#tab-payments'))).toBe('Últimos pagos 1');
            expect(text(el.querySelector('tbody tr'))).toContain('PRUEBA, MATIAS');
            expect(text(el.querySelector('tbody tr'))).toContain('$ 85.000');
        });

        it('refreshes the panel after registering a payment', async () => {
            const { fixture, component, service } = await create();
            const before = service.getPlayerAccounts.mock.calls.length;

            component.selectPlayer(JUGADOR);
            component.paymentForm.patchValue({ period: 'Enero', amount: '1000', method: 'Efectivo' });
            component.onSubmit();
            await fixture.whenStable();

            expect(service.registrarPago).toHaveBeenCalledTimes(1);
            expect(service.getPlayerAccounts.mock.calls.length).toBe(before + 2);
        });
    });
});
