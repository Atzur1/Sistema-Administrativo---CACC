import { ChangeDetectorRef } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { NEVER, Observable, from, throwError } from 'rxjs';

import { CuotasPagos, buscarJugadores, mensajeSinInscripcion, parsePeriodo } from './cuotas-pagos';
import { PagosService } from '../../services/pagos';
import { ArancelesService } from '../../services/aranceles';
import { NotificationService } from '../../shared/notifications/notification.service';
import { FINANCIAL_ACCESS_DENIED } from '../../services/auth';
import { PaymentSummaryModel, PendingFeeDetailModel } from '../../models/PaymentModel';
import { PlayerSummaryModel } from '../../models/PlayerSummaryModel';
import { EnrollmentFeeService } from '../../services/enrollment-fees';
import { EnrollmentService } from '../../services/enrollment';
import { guardarUltimoMedioPago, leerUltimoMedioPago } from '../../shared/ultimo-medio-pago';
import { TariffSummaryModel } from '../../models/TariffModel';

// HU-019: "Deuda Global Total" — un único indicador destacado (no dos métricas
// separadas) con el monto adeudado y la cantidad de jugadores morosos juntos, en el
// formato exacto que pide el criterio de aceptación: "$1.480.000 (32 jugadores morosos)".

const EMPTY_ARANCEL_RESUMEN: TariffSummaryModel = {
  arancelMasculinoVigente: null,
  arancelFemeninoVigente: null,
  proximoCambioFecha: null,
};

// Responses arrive asynchronously, as in the real app.
const response = <T>(value: T) => from(Promise.resolve(value));

async function create(resumen: PaymentSummaryModel): Promise<ComponentFixture<CuotasPagos>> {
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
  const RESUMEN: PaymentSummaryModel = {
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
    expect(textOf(fixture)).toContain('No hay cobros pendientes');
  });
});

describe('mensajeSinInscripcion', () => {
  it('si el arancel de inscripción de su género está en $0, dice que no se cobra', () => {
    expect(mensajeSinInscripcion('Mujer, Prueba', 'Femenino', 0)).toBe(
      'Mujer, Prueba no paga inscripción: la inscripción femenina está en $0 (no se cobra).',
    );
  });

  it('sin arancel de inscripción cargado, lo dice como tal', () => {
    expect(mensajeSinInscripcion('Varón, Prueba', 'Masculino', undefined)).toBe(
      'Varón, Prueba no paga inscripción: no hay un arancel de inscripción vigente para su género.',
    );
  });
});

// ===== HU-100: mostrador de cobro =====

const JUGADORES: PlayerSummaryModel[] = [
  { idJugador: 1, nombre: 'JUAN', apellido: 'BAZÁN', dni: '47970803', genero: 'Masculino', categoria: 'AFA 2010', nombreCompleto: 'BAZÁN, JUAN' },
  { idJugador: 2, nombre: 'ANA', apellido: 'PÉREZ', dni: '45111222', genero: 'Femenino', categoria: 'Femenino', nombreCompleto: 'PÉREZ, ANA' },
];

const cuotaPendiente = (idPago: number, periodo: string, saldo: number): PendingFeeDetailModel => ({
  idPago, periodo, montoOriginal: saldo, saldoPendiente: saldo, tieneBeneficio: false, motivoBeneficio: null,
  tipoValorBeneficio: null, porcentajeBeneficio: null, montoFijoBeneficio: null, abonos: [],
});

async function crearMostrador(deuda: PendingFeeDetailModel[] = [cuotaPendiente(10, 'Agosto 2026', 85000), cuotaPendiente(11, 'Septiembre 2026', 85000)]) {
  const pagosService = {
    getJugadores: vi.fn(() => response(JUGADORES)),
    getPendientes: vi.fn(() => response([{ idJugador: 2, nombreCompleto: 'PÉREZ, ANA', dni: '45111222', categoria: 'Femenino', montoTotal: 85000, cantidadCuotas: 1 }])),
    getResumen: vi.fn(() => response({ recaudadoAnioActual: 0, pagosDelMes: 0, cantidadPendientes: 0, deudaGlobalTotal: 0, jugadoresMorosos: 0 })),
    getCategorias: vi.fn(() => response([])),
    getDeuda: vi.fn(() => response(deuda)),
    registrarPago: vi.fn((): Observable<unknown> => response({ mensaje: 'ok' })),
  };
  const enrollmentService = {
    getEnrollmentByPlayer: vi.fn(() => response({ playerId: 1, amount: 70000, paidAmount: 20000, pendingBalance: 50000, dueDate: '2026-03-01', payments: [] })),
    registerEnrollmentPayment: vi.fn(() => response({})),
  };
  const notify = vi.fn();

  await TestBed.configureTestingModule({
    imports: [CuotasPagos],
    providers: [
      provideRouter([]),
      { provide: PagosService, useValue: pagosService },
      { provide: ArancelesService, useValue: { getResumen: vi.fn(() => response(EMPTY_ARANCEL_RESUMEN)) } },
      { provide: EnrollmentFeeService, useValue: { getAllEnrollmentFees: vi.fn(() => response([])) } },
      { provide: EnrollmentService, useValue: enrollmentService },
      { provide: NotificationService, useValue: { notify } },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(CuotasPagos);
  fixture.detectChanges();
  await fixture.whenStable();
  const el = fixture.nativeElement as HTMLElement;
  // Las pruebas cambian el estado llamando métodos: la vista se marca antes de redibujar (OnPush por defecto).
  const refrescar = async () => {
    await fixture.whenStable();
    fixture.componentRef.injector.get(ChangeDetectorRef).markForCheck();
    fixture.detectChanges();
  };
  await refrescar();
  const component = fixture.componentInstance;
  const buscador = () => el.querySelector<HTMLInputElement>('#buscador-alumno')!;
  const escribir = (texto: string) => {
    buscador().value = texto;
    buscador().dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };
  const tecla = (key: string, target: EventTarget = buscador()) =>
    target.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
  const elegir = async (texto = 'bazan') => {
    escribir(texto);
    tecla('Enter');
    await refrescar();
  };
  const botonCobrar = () => el.querySelector<HTMLButtonElement>('.form-panel .submit-button');
  return { fixture, component, el, refrescar, buscador, escribir, tecla, elegir, botonCobrar, pagosService, enrollmentService, notify };
}

describe('CuotasPagos - mostrador: búsqueda y atajo "/" (HU-100)', () => {
  beforeEach(() => sessionStorage.clear());

  it('"/" focuses the player search from anywhere on the screen', async () => {
    const { buscador, tecla } = await crearMostrador();
    (document.activeElement as HTMLElement | null)?.blur();

    tecla('/', document);

    expect(document.activeElement).toBe(buscador());
  });

  it('"/" typed inside another field is just a slash, it does not steal the focus', async () => {
    const { el, buscador } = await crearMostrador();
    const otroCampo = el.querySelector<HTMLInputElement>('.search-control input')!;
    otroCampo.focus();

    const evento = new KeyboardEvent('keydown', { key: '/', bubbles: true, cancelable: true });
    otroCampo.dispatchEvent(evento);

    expect(document.activeElement).toBe(otroCampo);
    expect(evento.defaultPrevented).toBe(false);
    expect(document.activeElement).not.toBe(buscador());
  });

  it('finds a player by name in any order and without accents, or by DNI', async () => {
    const { component, escribir } = await crearMostrador();

    escribir('juan bazan');
    expect(component.matchingPlayers.map((j) => j.idJugador)).toEqual([1]);
    escribir('4797');
    expect(component.matchingPlayers.map((j) => j.idJugador)).toEqual([1]);
  });

  it('arrows and Enter choose a suggestion without touching the mouse', async () => {
    const { component, escribir, tecla, refrescar } = await crearMostrador();

    escribir('a');                // BAZÁN, JUAN y PÉREZ, ANA
    tecla('ArrowDown');
    tecla('Enter');
    await refrescar();

    expect(component.selectedPlayer?.idJugador).toBe(2);
  });
});

describe('CuotasPagos - mostrador: alumno y cobro lado a lado (HU-100)', () => {
  beforeEach(() => sessionStorage.clear());

  it('before choosing a player there is only the search; after, his fees and the payment side by side', async () => {
    const { el, elegir } = await crearMostrador();
    expect(el.querySelector('.mostrador-grid')).toBeNull();
    expect(el.querySelector('.mostrador-vacio')).not.toBeNull();

    await elegir();

    const grilla = el.querySelector('.mostrador-grid')!;
    expect(grilla.querySelector('.mostrador-cuotas')).not.toBeNull();
    expect(grilla.querySelector('.form-panel')).not.toBeNull();
    expect(Array.from(el.querySelectorAll('.cuota-periodo')).map((c) => c.textContent!.trim())).toEqual(['Agosto 2026', 'Septiembre 2026']);
  });

  it('"Registrar cobro" is always visible (not hidden behind tabs or steps), disabled until the payment is complete', async () => {
    const { component, elegir, botonCobrar, refrescar } = await crearMostrador();
    await elegir();

    expect(botonCobrar()!.textContent!.trim()).toBe('Registrar cobro');
    expect(botonCobrar()!.disabled).toBe(true);           // falta el medio de pago

    component.paymentForm.patchValue({ method: 'Efectivo' });
    await refrescar();
    expect(botonCobrar()!.disabled).toBe(false);

    component.switchPanelMode('inscripcion');
    await refrescar();
    expect(botonCobrar()!.textContent!.trim()).toBe('Registrar cobro');
  });

  it('proposes the oldest pending fee and puts the focus on the amount', async () => {
    const { component, el, elegir } = await crearMostrador();
    await elegir();

    expect(component.paymentForm.value).toMatchObject({ period: 'Agosto', year: 2026, amount: '85000' });
    expect(document.activeElement).toBe(el.querySelector('#amount'));
  });

  it('without pending fees it proposes the current month, for a payment in advance', async () => {
    const hoy = new Date();
    const { component, elegir } = await crearMostrador([]);
    await elegir();

    expect(component.paymentForm.value.period).toBe(component.periods[hoy.getMonth()]);
    expect(component.paymentForm.value.year).toBe(hoy.getFullYear());
    expect(component.paymentForm.value.amount).toBe('');
  });

  it('clicking a fee loads it in the payment', async () => {
    const { component, el, elegir, refrescar } = await crearMostrador();
    await elegir();

    el.querySelectorAll<HTMLButtonElement>('.cuota-item')[1].click();
    await refrescar();

    expect(component.paymentForm.value).toMatchObject({ period: 'Septiembre', year: 2026, amount: '85000' });
    expect(el.querySelectorAll('.cuota-elegida')).toHaveLength(1);
  });

  it('a row of "Pendientes de cobro" loads that player in the counter, without leaving the screen', async () => {
    const { component, el, refrescar } = await crearMostrador();

    el.querySelector<HTMLElement>('.debtors-table tbody tr')!.click();
    await refrescar();

    expect(component.selectedPlayer?.idJugador).toBe(2);
    expect(el.querySelector('.mostrador-grid')).not.toBeNull();
  });
});

describe('CuotasPagos - mostrador: medio de pago y foco después del cobro (HU-100)', () => {
  beforeEach(() => sessionStorage.clear());

  it('the last payment method used is preselected, and can be changed right away', async () => {
    guardarUltimoMedioPago('Transferencia');
    const { component, elegir } = await crearMostrador();
    await elegir();

    expect(component.paymentForm.value.method).toBe('Transferencia');
    component.paymentForm.patchValue({ method: 'Efectivo' });
    expect(component.paymentForm.value.method).toBe('Efectivo');
  });

  it('a full payment registers, remembers the method, clears the counter and returns the focus to the search', async () => {
    const { component, elegir, refrescar, buscador, pagosService } = await crearMostrador();
    await elegir();
    component.paymentForm.patchValue({ method: 'Efectivo' });

    component.onSubmit();
    await refrescar();

    expect(pagosService.registrarPago).toHaveBeenCalledWith(1, 'Agosto', 2026, 85000, 'Efectivo');
    expect(leerUltimoMedioPago()).toBe('Efectivo');
    expect(component.selectedPlayer).toBeNull();
    expect(buscador().value).toBe('');
    expect(document.activeElement).toBe(buscador());
    expect(component.paymentForm.value.method).toBe('Efectivo');   // listo para el siguiente alumno
  });

  it('a partial payment works the same way: only the amount changes', async () => {
    const { component, el, elegir, refrescar, buscador, pagosService } = await crearMostrador();
    await elegir();
    component.paymentForm.patchValue({ method: 'Transferencia' });
    const monto = el.querySelector<HTMLInputElement>('#amount')!;
    monto.value = '40000';
    monto.dispatchEvent(new Event('input'));

    component.onSubmit();
    await refrescar();

    expect(pagosService.registrarPago).toHaveBeenCalledWith(1, 'Agosto', 2026, 40000, 'Transferencia');
    expect(document.activeElement).toBe(buscador());
  });

  it('an enrollment payment also clears the counter and returns the focus to the search', async () => {
    guardarUltimoMedioPago('Efectivo');
    const { component, elegir, refrescar, buscador, enrollmentService } = await crearMostrador();
    await elegir();
    component.switchPanelMode('inscripcion');
    await refrescar();
    expect(component.inscripcionForm.value).toMatchObject({ amount: '50000', method: 'Efectivo' });

    component.onSubmitInscripcion();
    await refrescar();

    expect(enrollmentService.registerEnrollmentPayment).toHaveBeenCalledWith(1, { amount: 50000, paymentMethod: 'Efectivo' });
    expect(component.selectedPlayer).toBeNull();
    expect(document.activeElement).toBe(buscador());
  });

  it('if the payment fails, the player and the data stay so it can be retried', async () => {
    const { component, elegir, refrescar, pagosService, notify } = await crearMostrador();
    pagosService.registrarPago.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 422, error: { mensaje: 'El monto supera el saldo.' } })));
    await elegir();
    component.paymentForm.patchValue({ method: 'Efectivo' });

    component.onSubmit();
    await refrescar();

    expect(notify).toHaveBeenCalledWith('El monto supera el saldo.', 'error');
    expect(component.selectedPlayer?.idJugador).toBe(1);
    expect(component.paymentForm.value.amount).toBe('85000');
  });
});

describe('CuotasPagos - utilidades del mostrador (HU-100)', () => {
  it('buscarJugadores matches every word in any order, or the DNI digits', () => {
    expect(buscarJugadores(JUGADORES, 'perez').map((j) => j.idJugador)).toEqual([2]);
    expect(buscarJugadores(JUGADORES, 'ana pérez').map((j) => j.idJugador)).toEqual([2]);
    expect(buscarJugadores(JUGADORES, '45.111').map((j) => j.idJugador)).toEqual([2]);
    expect(buscarJugadores(JUGADORES, '   ')).toEqual([]);
  });

  it('parsePeriodo reads "Mes Año" and ignores anything else', () => {
    expect(parsePeriodo('Marzo 2026')).toEqual({ mes: 'Marzo', anio: 2026 });
    expect(parsePeriodo('Inscripción 2026')).toBeNull();
  });
});

describe('ultimo-medio-pago (HU-100)', () => {
  beforeEach(() => sessionStorage.clear());

  it('remembers only a valid payment method, for the browser session', () => {
    expect(leerUltimoMedioPago()).toBe('');
    guardarUltimoMedioPago('Transferencia');
    expect(leerUltimoMedioPago()).toBe('Transferencia');
    guardarUltimoMedioPago('Cheque');
    expect(leerUltimoMedioPago()).toBe('Transferencia');
  });

  it('without storage available the payment still works, it just does not remember', () => {
    const getItem = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('bloqueado'); });
    const setItem = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('bloqueado'); });
    try {
      expect(() => guardarUltimoMedioPago('Efectivo')).not.toThrow();
      expect(leerUltimoMedioPago()).toBe('');
    } finally {
      getItem.mockRestore();
      setItem.mockRestore();
    }
  });
});
