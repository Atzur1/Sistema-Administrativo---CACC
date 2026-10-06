import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { NEVER, Observable, from, throwError } from 'rxjs';

import { CuotasPagos } from './cuotas-pagos';
import { PagosService, ResumenPagos } from '../../services/pagos';
import { ArancelesService, ArancelResumen } from '../../services/aranceles';
import { NotificationService } from '../../shared/notifications/notification.service';
import { FINANCIAL_ACCESS_DENIED } from '../../services/auth';

// HU-019: "Deuda Global Total" — un único indicador destacado (no dos métricas
// separadas) con el monto adeudado y la cantidad de jugadores morosos juntos, en el
// formato exacto que pide el criterio de aceptación: "$1.480.000 (32 jugadores morosos)".

const EMPTY_ARANCEL_RESUMEN: ArancelResumen = {
  arancelMasculinoVigente: null,
  arancelFemeninoVigente: null,
  proximoCambioFecha: null,
};

// Responses arrive asynchronously, as in the real app.
const response = <T>(value: T) => from(Promise.resolve(value));

async function create(resumen: ResumenPagos): Promise<ComponentFixture<CuotasPagos>> {
  const pagosService = {
    getJugadores: vi.fn(() => response([])),
    getPendientes: vi.fn(() => response([])),
    getRecientes: vi.fn(() => response([])),
    getResumen: vi.fn(() => response(resumen)),
    getCategorias: vi.fn(() => response([])),
  };
  const arancelesService = {
    getResumen: vi.fn(() => response(EMPTY_ARANCEL_RESUMEN)),
  };

  await TestBed.configureTestingModule({
    imports: [CuotasPagos],
    providers: [
      provideRouter([]),
      { provide: PagosService, useValue: pagosService },
      { provide: ArancelesService, useValue: arancelesService },
      { provide: NotificationService, useValue: { notify: vi.fn() } },
    ],
  }).compileComponents();

  const fixture: ComponentFixture<CuotasPagos> = TestBed.createComponent(CuotasPagos);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return fixture;
}

function textOf(fixture: ComponentFixture<CuotasPagos>): string {
  return (fixture.nativeElement as HTMLElement).textContent ?? '';
}

describe('CuotasPagos - Deuda Global Total (HU-019)', () => {
  it('shows the combined amount and debtor count in a single highlighted indicator', async () => {
    const fixture = await create({
      recaudadoAnioActual: 0,
      pagosDelMes: 0,
      cantidadPendientes: 0,
      deudaGlobalTotal: 1_480_000,
      jugadoresMorosos: 32,
    });

    const texto = textOf(fixture);
    expect(texto).toContain('Deuda Global Total:');
    expect(texto).toMatch(/\$\s*1\.480\.000/);
    expect(texto).toContain('(32 jugadores morosos)');
  });

  it('uses the singular form when exactly one player owes money', async () => {
    const fixture = await create({
      recaudadoAnioActual: 0,
      pagosDelMes: 0,
      cantidadPendientes: 0,
      deudaGlobalTotal: 92_000,
      jugadoresMorosos: 1,
    });

    expect(textOf(fixture)).toContain('(1 jugador moroso)');
  });
});

// HU-022: estados de carga y error del panel de Cuotas.
describe('CuotasPagos - carga y errores (HU-022)', () => {
  const RESUMEN: ResumenPagos = {
    recaudadoAnioActual: 0,
    pagosDelMes: 0,
    cantidadPendientes: 0,
    deudaGlobalTotal: 0,
    jugadoresMorosos: 0,
  };

  async function createWith(pendientes: () => Observable<unknown>) {
    const pagosService = {
      getJugadores: vi.fn(() => response([])),
      getPendientes: vi.fn(pendientes),
      getResumen: vi.fn(() => response(RESUMEN)),
      getCategorias: vi.fn(() => response([])),
    };

    await TestBed.configureTestingModule({
      imports: [CuotasPagos],
      providers: [
        provideRouter([]),
        { provide: PagosService, useValue: pagosService },
        { provide: ArancelesService, useValue: { getResumen: vi.fn(() => response(EMPTY_ARANCEL_RESUMEN)) } },
        { provide: NotificationService, useValue: { notify: vi.fn() } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(CuotasPagos);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, pagosService };
  }

  const failWith = (status: number) => () => throwError(() => new HttpErrorResponse({ status }));

  it('shows a loading message while the pending list is on its way', async () => {
    const { fixture } = await createWith(() => NEVER);

    expect(textOf(fixture)).toContain('Cargando cobros pendientes');
    expect(textOf(fixture)).not.toContain('No hay cobros pendientes');
  });

  it('reports a connection error instead of claiming there are no pending payments', async () => {
    const { fixture } = await createWith(failWith(0));

    const alert = fixture.nativeElement.querySelector('.load-error') as HTMLElement;
    expect(alert.textContent).toContain('No se pudo conectar con el servidor');
    expect(textOf(fixture)).toContain('No se pudieron cargar los cobros pendientes.');
    expect(textOf(fixture)).not.toContain('No hay cobros pendientes');
  });

  it('shows the access-denied message on a 403', async () => {
    const { fixture } = await createWith(failWith(403));

    expect(fixture.nativeElement.querySelector('.load-error').textContent).toContain(FINANCIAL_ACCESS_DENIED);
  });

  it('shows a generic message on a server error', async () => {
    const { fixture } = await createWith(failWith(500));

    expect(fixture.nativeElement.querySelector('.load-error').textContent)
      .toContain('No se pudieron cargar los datos de cuotas');
  });

  it('"Reintentar" reads the data again and clears the error once it loads', async () => {
    let falla = true;
    const { fixture, pagosService } = await createWith(() => (falla ? failWith(0)() : response([])));

    falla = false;
    (fixture.nativeElement.querySelector('.load-error button') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(pagosService.getPendientes).toHaveBeenCalledTimes(2);
    expect(fixture.nativeElement.querySelector('.load-error')).toBeNull();
    expect(textOf(fixture)).toContain('No hay cobros pendientes en este momento.');
  });
});
