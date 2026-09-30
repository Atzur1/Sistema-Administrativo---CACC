import { ChangeDetectorRef } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { from } from 'rxjs';

import { DeudaJugador } from './deuda-jugador';
import { JugadoresService } from '../../services/jugadores';
import {
  CobrarCuotasResponse,
  CuotaJugador,
  CuotaPendienteDetalle,
  JugadorResumen,
  PagosService,
} from '../../services/pagos';
import { NotificationService } from '../../shared/notifications/notification.service';

// HU-025: selección de cuotas, método de pago, confirmación y refresco con datos reales. Los
// servicios HTTP se reemplazan por dobles; el cobro contra SQL Server se valida en QA-HU-025.md.

const JUGADOR: JugadorResumen = {
  idJugador: 3,
  nombre: 'MATIAS',
  apellido: 'PRUEBA',
  dni: '99000003',
  genero: 'Masculino',
  categoria: 'AFA 20067',
  nombreCompleto: 'PRUEBA, MATIAS',
};

function cuota(idPago: number, periodo: string, extra: Partial<CuotaJugador> = {}): CuotaJugador {
  return {
    idPago,
    periodo,
    fechaVencimiento: '2026-03-01T00:00:00',
    montoCuota: 85000,
    saldoPendiente: 85000,
    montoAbonado: 0,
    estado: 'Pendiente',
    cubiertaPorBeneficio: false,
    motivoBeneficio: null,
    metodoPago: null,
    fechaPago: null,
    ...extra,
  };
}

const ENERO_PAGADA = cuota(9, 'Enero 2026', {
  fechaVencimiento: '2026-01-01T00:00:00',
  montoCuota: 70000,
  saldoPendiente: 0,
  montoAbonado: 70000,
  estado: 'Pagado',
  metodoPago: 'Efectivo',
  fechaPago: '2026-01-05T00:00:00',
});
const MARZO = cuota(10, 'Marzo 2026', { estado: 'Vencido' });
const ABRIL = cuota(11, 'Abril 2026', { fechaVencimiento: '2026-04-01T00:00:00' });
const MAYO_CUBIERTA = cuota(12, 'Mayo 2026', {
  fechaVencimiento: '2026-05-01T00:00:00',
  saldoPendiente: 0,
  cubiertaPorBeneficio: true,
  motivoBeneficio: 'Becado',
});

const RESPUESTA_COBRO: CobrarCuotasResponse = {
  exito: true,
  idJugador: 3,
  pagosAbonados: [10, 11],
  cuotas: [
    { idPago: 10, periodo: 'Marzo 2026', monto: 85000, estado: 'Pagado' },
    { idPago: 11, periodo: 'Abril 2026', monto: 85000, estado: 'Pagado' },
  ],
  montoTotal: 170000,
  metodoPago: 'Transferencia',
  fechaPago: '2026-09-30T00:00:00',
  fechaHoraRegistro: '2026-09-30T18:45:12',
  estado: 'Pagado',
  mensaje: 'Cobro registrado correctamente.',
};

// HttpErrorResponse no hereda de Error: se lo trata explícitamente como respuesta fallida.
const respuesta = <T>(valor: T | Error | HttpErrorResponse) =>
  from(
    valor instanceof Error || valor instanceof HttpErrorResponse
      ? Promise.reject(valor)
      : Promise.resolve(valor as T),
  );

interface Opciones {
  cuotas?: CuotaJugador[][]; // una lista por cada llamada a getCuotas (la última se repite)
  cobro?: CobrarCuotasResponse | HttpErrorResponse;
}

async function crear(opciones: Opciones = {}) {
  const lotes = opciones.cuotas ?? [[ENERO_PAGADA, MARZO, ABRIL, MAYO_CUBIERTA]];
  let llamada = 0;

  const pagos = {
    getCuotas: vi.fn(() => respuesta(lotes[Math.min(llamada++, lotes.length - 1)])),
    getDeuda: vi.fn(() => respuesta<CuotaPendienteDetalle[]>([])),
    cobrarCuotas: vi.fn(() => respuesta(opciones.cobro ?? RESPUESTA_COBRO)),
    registrarPago: vi.fn(),
  };
  const jugadores = { getJugador: vi.fn(() => respuesta(JUGADOR)) };
  const notificaciones = { notify: vi.fn() };

  await TestBed.configureTestingModule({
    imports: [DeudaJugador],
    providers: [
      provideRouter([]),
      { provide: PagosService, useValue: pagos },
      { provide: JugadoresService, useValue: jugadores },
      { provide: NotificationService, useValue: notificaciones },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '3' }) } } },
    ],
  }).compileComponents();

  vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

  const fixture: ComponentFixture<DeudaJugador> = TestBed.createComponent(DeudaJugador);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();

  const el = fixture.nativeElement as HTMLElement;
  const refrescar = async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };
  // Equivale a elegir el método en el selector: el evento de ngModel marca la vista para revisar
  // (el componente es OnPush por defecto en Angular 22).
  const elegirMetodo = async (metodo: string) => {
    fixture.componentInstance.metodoCobro = metodo;
    fixture.debugElement.injector.get(ChangeDetectorRef).markForCheck();
    await refrescar();
  };
  return { fixture, component: fixture.componentInstance, pagos, notificaciones, el, refrescar, elegirMetodo };
}

const texto = (el: Element | null) => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();
const checkbox = (el: HTMLElement, idPago: number) =>
  el.querySelector<HTMLInputElement>(`input[type="checkbox"][data-id="${idPago}"]`)!;
const botonRegistrar = (el: HTMLElement) => el.querySelector<HTMLButtonElement>('.registrar-pago-button')!;

async function seleccionar(el: HTMLElement, refrescar: () => Promise<void>, ...ids: number[]) {
  for (const id of ids) {
    checkbox(el, id).click();
  }
  await refrescar();
}

describe('DeudaJugador - registro de pago de cuotas (HU-025)', () => {
  it('lee las cuotas y la deuda reales del jugador de la ruta', async () => {
    const { pagos } = await crear();

    expect(pagos.getCuotas).toHaveBeenCalledWith(3);
    expect(pagos.getDeuda).toHaveBeenCalledWith(3);
  });

  it('lista cada cuota con período, monto, vencimiento y estado', async () => {
    const { el } = await crear();

    const filas = Array.from(el.querySelectorAll('.cuotas-table tbody tr')).map(texto);
    expect(filas).toHaveLength(4);
    expect(filas[0]).toContain('Enero 2026');
    expect(filas[0]).toContain('Pagado');
    expect(filas[0]).toContain('Efectivo');
    expect(filas[1]).toContain('Marzo 2026');
    expect(filas[1]).toContain('Vencido');
    expect(filas[1]).toContain('31/03/2026');
    expect(filas[2]).toContain('Pendiente');
  });

  it('permite seleccionar pendientes y vencidas, pero no pagadas ni cubiertas por beneficio', async () => {
    const { el } = await crear();

    expect(checkbox(el, 9).disabled).toBe(true);
    expect(checkbox(el, 12).disabled).toBe(true);
    expect(checkbox(el, 10).disabled).toBe(false);
    expect(checkbox(el, 11).disabled).toBe(false);
  });

  it('recalcula el total con cada selección', async () => {
    const { el, refrescar } = await crear();

    await seleccionar(el, refrescar, 10);
    expect(texto(el.querySelector('.cobro-total'))).toContain('85.000');

    await seleccionar(el, refrescar, 11);
    expect(texto(el.querySelector('.cobro-total'))).toContain('170.000');

    await seleccionar(el, refrescar, 10);
    expect(texto(el.querySelector('.cobro-total'))).toContain('85.000');
  });

  it('ofrece Efectivo y Transferencia como métodos de pago', async () => {
    const { component } = await crear();

    expect(component.metodosCobro).toEqual(['Efectivo', 'Transferencia']);
  });

  it('mantiene "Registrar Pago" deshabilitado sin cuotas o sin método', async () => {
    const { el, refrescar, elegirMetodo } = await crear();

    expect(botonRegistrar(el).disabled).toBe(true);

    await elegirMetodo('Transferencia');
    expect(botonRegistrar(el).disabled).toBe(true);

    await elegirMetodo('');
    await seleccionar(el, refrescar, 10);
    expect(botonRegistrar(el).disabled).toBe(true);

    await elegirMetodo('Transferencia');
    expect(botonRegistrar(el).disabled).toBe(false);
  });

  it('abre la confirmación con cuotas, montos, método y total, sin cobrar todavía', async () => {
    const { el, pagos, refrescar, elegirMetodo } = await crear();
    await seleccionar(el, refrescar, 10, 11);
    await elegirMetodo('Transferencia');

    botonRegistrar(el).click();
    await refrescar();

    const dialogo = el.querySelector('[role="dialog"]');
    expect(dialogo).not.toBeNull();
    const contenido = texto(dialogo);
    expect(contenido).toContain('2 cuotas');
    expect(contenido).toContain('Marzo 2026');
    expect(contenido).toContain('Abril 2026');
    expect(contenido).toContain('Transferencia');
    expect(texto(el.querySelector('.cobro-resumen-total'))).toContain('170.000');
    expect(pagos.cobrarCuotas).not.toHaveBeenCalled();
  });

  it('Cancelar cierra la confirmación sin cobrar', async () => {
    const { el, pagos, refrescar, elegirMetodo } = await crear();
    await seleccionar(el, refrescar, 10);
    await elegirMetodo('Efectivo');
    botonRegistrar(el).click();
    await refrescar();

    el.querySelector<HTMLButtonElement>('.cobro-cancelar')!.click();
    await refrescar();

    expect(el.querySelector('[role="dialog"]')).toBeNull();
    expect(pagos.cobrarCuotas).not.toHaveBeenCalled();
  });

  it('al confirmar cobra, notifica y vuelve a leer las cuotas y la deuda del backend', async () => {
    const pagadas = [
      ENERO_PAGADA,
      { ...MARZO, estado: 'Pagado' as const, saldoPendiente: 0, montoAbonado: 85000, metodoPago: 'Transferencia' },
      { ...ABRIL, estado: 'Pagado' as const, saldoPendiente: 0, montoAbonado: 85000, metodoPago: 'Transferencia' },
      MAYO_CUBIERTA,
    ];
    const { el, component, pagos, notificaciones, refrescar, elegirMetodo } = await crear({
      cuotas: [[ENERO_PAGADA, MARZO, ABRIL, MAYO_CUBIERTA], pagadas],
    });
    await seleccionar(el, refrescar, 10, 11);
    await elegirMetodo('Transferencia');
    botonRegistrar(el).click();
    await refrescar();

    el.querySelector<HTMLButtonElement>('.cobro-confirmar')!.click();
    await refrescar();

    expect(pagos.cobrarCuotas).toHaveBeenCalledWith(3, [10, 11], 'Transferencia');
    expect(el.querySelector('[role="dialog"]')).toBeNull();
    expect(notificaciones.notify).toHaveBeenCalledWith(expect.stringContaining('170.000'), 'success');
    expect(pagos.getCuotas).toHaveBeenCalledTimes(2);
    expect(pagos.getDeuda).toHaveBeenCalledTimes(2);
    expect(checkbox(el, 10).disabled).toBe(true);
    expect(texto(el.querySelectorAll('.cuotas-table tbody tr')[1])).toContain('Pagado');
    expect(component.cuotasSeleccionadas).toHaveLength(0);
  });

  it('si el backend rechaza, no muestra éxito ni marca nada como pagado', async () => {
    const rechazo = new HttpErrorResponse({
      status: 400,
      error: { exito: false, mensaje: 'Las siguientes cuotas ya fueron abonadas: 11.' },
    });
    const { el, notificaciones, refrescar, elegirMetodo } = await crear({ cobro: rechazo });
    await seleccionar(el, refrescar, 10, 11);
    await elegirMetodo('Transferencia');
    botonRegistrar(el).click();
    await refrescar();

    el.querySelector<HTMLButtonElement>('.cobro-confirmar')!.click();
    await refrescar();

    expect(texto(el.querySelector('.cobro-error'))).toContain('ya fueron abonadas');
    expect(notificaciones.notify).not.toHaveBeenCalledWith(expect.anything(), 'success');
    expect(texto(el.querySelectorAll('.cuotas-table tbody tr')[1])).toContain('Vencido');
  });

  it('informa un error de conexión cuando la API no responde', async () => {
    const sinConexion = new HttpErrorResponse({ status: 0 });
    const { el, refrescar, elegirMetodo } = await crear({ cobro: sinConexion });
    await seleccionar(el, refrescar, 10);
    await elegirMetodo('Efectivo');
    botonRegistrar(el).click();
    await refrescar();

    el.querySelector<HTMLButtonElement>('.cobro-confirmar')!.click();
    await refrescar();

    expect(texto(el.querySelector('.cobro-error'))).toContain('No se pudo conectar');
  });

  it('informa un error interno sin mostrar éxito', async () => {
    const interno = new HttpErrorResponse({
      status: 500,
      error: { exito: false, mensaje: 'Error interno al procesar el cobro.' },
    });
    const { el, notificaciones, refrescar, elegirMetodo } = await crear({ cobro: interno });
    await seleccionar(el, refrescar, 10);
    await elegirMetodo('Efectivo');
    botonRegistrar(el).click();
    await refrescar();

    el.querySelector<HTMLButtonElement>('.cobro-confirmar')!.click();
    await refrescar();

    expect(texto(el.querySelector('.cobro-error'))).toContain('Error interno al procesar el cobro.');
    expect(notificaciones.notify).not.toHaveBeenCalledWith(expect.anything(), 'success');
  });
});
