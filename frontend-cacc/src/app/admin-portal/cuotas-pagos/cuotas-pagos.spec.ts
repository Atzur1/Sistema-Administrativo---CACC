import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { from } from 'rxjs';

import { CuotasPagos } from './cuotas-pagos';
import { PagosService, ResumenPagos } from '../../services/pagos';
import { ArancelesService, ArancelResumen } from '../../services/aranceles';
import { NotificationService } from '../../shared/notifications/notification.service';

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
