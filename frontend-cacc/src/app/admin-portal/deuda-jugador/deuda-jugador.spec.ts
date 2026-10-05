import { ChangeDetectorRef } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { Observable, Subject, from, isObservable } from 'rxjs';

import { DeudaJugador } from './deuda-jugador';
import { JugadoresService } from '../../services/jugadores';
import {
  CobrarCuotasResponse,
  CuotaJugador,
  CuotaPendienteDetalle,
  JugadorResumen,
  PagosService,
  PlayerStatement,
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

// HU-024: la tabla y el total salen del estado de cuenta. Se arma con las mismas cuotas que
// usaban los tests de HU-025/026, con el total calculado como lo hace el backend (saldo de las
// cuotas no pagadas).
function estadoDeCuenta(cuotas: CuotaJugador[]): PlayerStatement {
  return {
    playerId: 3,
    playerFullName: 'PRUEBA, MATIAS',
    dni: '99000003',
    totalDebtAmount: cuotas.filter((c) => c.estado !== 'Pagado').reduce((t, c) => t + c.saldoPendiente, 0),
    fees: cuotas.map((c) => ({
      id: c.idPago,
      periodName: c.periodo,
      amount: c.montoCuota,
      amountDue: c.estado === 'Pagado' ? 0 : c.saldoPendiente,
      amountPaid: c.montoAbonado,
      dueDate: c.fechaVencimiento,
      status: c.estado,
      paidAt: c.fechaPago,
      paymentMethod: c.metodoPago,
      coveredByBenefit: c.cubiertaPorBeneficio,
      benefitReason: c.motivoBeneficio,
    })),
  };
}

interface Opciones {
  // Una respuesta por cada lectura del estado de cuenta / de getDeuda (la última se repite). Un
  // Error o HttpErrorResponse simula que esa lectura falla. Un Observable controla cuándo responde.
  cuotas?: (CuotaJugador[] | Error | HttpErrorResponse | Observable<PlayerStatement>)[];
  deuda?: (CuotaPendienteDetalle[] | Error | HttpErrorResponse)[];
  // Un Observable permite controlar cuándo "responde" el backend al cobro.
  cobro?: CobrarCuotasResponse | HttpErrorResponse | Observable<CobrarCuotasResponse>;
}

const secuencia = <T>(lotes: T[]) => {
  let llamada = 0;
  return () => lotes[Math.min(llamada++, lotes.length - 1)];
};

async function crear(opciones: Opciones = {}) {
  const siguienteCuotas = secuencia(opciones.cuotas ?? [[ENERO_PAGADA, MARZO, ABRIL, MAYO_CUBIERTA]]);
  const siguienteDeuda = secuencia(opciones.deuda ?? [[]]);

  const pagos = {
    getEstadoDeCuenta: vi.fn(() => {
      const lote = siguienteCuotas();
      if (isObservable(lote)) return lote;
      return Array.isArray(lote) ? respuesta(estadoDeCuenta(lote)) : respuesta<PlayerStatement>(lote);
    }),
    getDeuda: vi.fn(() => respuesta(siguienteDeuda())),
    cobrarCuotas: vi.fn(() =>
      isObservable(opciones.cobro) ? opciones.cobro : respuesta(opciones.cobro ?? RESPUESTA_COBRO),
    ),
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
  it('lee el estado de cuenta y la deuda reales del jugador de la ruta', async () => {
    const { pagos } = await crear();

    expect(pagos.getEstadoDeCuenta).toHaveBeenCalledWith(3);
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

  it('al confirmar cobra, notifica y vuelve a leer el estado de cuenta y la deuda del backend', async () => {
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
    expect(pagos.getEstadoDeCuenta).toHaveBeenCalledTimes(2);
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

// ===== HU-026: confirmación visual y actualización del estado de cuenta =====
// Arranca cuando el backend confirma el cobro de HU-025. El toast solo sale con esa respuesta, la
// grilla y la deuda se vuelven a leer de la API, y un fallo de esa relectura nunca reintenta el cobro.

function deudaDe(idPago: number, periodo: string, saldo = 85000): CuotaPendienteDetalle {
  return {
    idPago,
    periodo,
    montoOriginal: 85000,
    saldoPendiente: saldo,
    tieneBeneficio: false,
    motivoBeneficio: null,
    tipoValorBeneficio: null,
    porcentajeBeneficio: null,
    montoFijoBeneficio: null,
    abonos: [],
  };
}

const MAYO = cuota(13, 'Mayo 2026', { fechaVencimiento: '2026-05-01T00:00:00' });
const pagada = (c: CuotaJugador, metodo = 'Transferencia'): CuotaJugador => ({
  ...c,
  estado: 'Pagado',
  saldoPendiente: 0,
  montoAbonado: c.montoCuota,
  metodoPago: metodo,
});
const DEUDA_INICIAL = [deudaDe(10, 'Marzo 2026'), deudaDe(11, 'Abril 2026'), deudaDe(13, 'Mayo 2026')];
const totalAdeudado = (el: HTMLElement) => texto(el.querySelector('.total-adeudado'));
const filaDe = (el: HTMLElement, periodo: string) =>
  texto(
    Array.from(el.querySelectorAll('.cuotas-table tbody tr')).find((tr) => texto(tr).includes(periodo)) ??
      null,
  );

type Contexto = Awaited<ReturnType<typeof crear>>;

async function pagar(ctx: Contexto, ids: number[], metodo = 'Transferencia') {
  await seleccionar(ctx.el, ctx.refrescar, ...ids);
  await ctx.elegirMetodo(metodo);
  botonRegistrar(ctx.el).click();
  await ctx.refrescar();
  ctx.el.querySelector<HTMLButtonElement>('.cobro-confirmar')!.click();
  await ctx.refrescar();
}

describe('DeudaJugador - confirmación visual y estado de cuenta (HU-026)', () => {
  it('no muestra éxito mientras el backend no confirmó el cobro', async () => {
    const respuestaPendiente = new Subject<CobrarCuotasResponse>();
    const ctx = await crear({ cobro: respuestaPendiente });

    await pagar(ctx, [10, 11]);

    expect(ctx.notificaciones.notify).not.toHaveBeenCalled();
    expect(ctx.el.querySelector('[role="dialog"]')).not.toBeNull();
    expect(texto(ctx.el.querySelector('.cobro-confirmar'))).toBe('Registrando...');
    expect(filaDe(ctx.el, 'Marzo 2026')).toContain('Vencido');

    respuestaPendiente.next(RESPUESTA_COBRO);
    respuestaPendiente.complete();
    await ctx.refrescar();

    expect(ctx.notificaciones.notify).toHaveBeenCalledTimes(1);
    expect(ctx.notificaciones.notify).toHaveBeenCalledWith(expect.any(String), 'success');
  });

  it('el toast confirma el registro con cantidad de cuotas y total cobrado por el backend', async () => {
    const ctx = await crear();

    await pagar(ctx, [10, 11]);

    const [mensaje, tipo] = ctx.notificaciones.notify.mock.calls[0];
    expect(tipo).toBe('success');
    expect(mensaje).toMatch(/^Pago registrado correctamente\. Se abonaron 2 cuotas por \$\s*170\.000\.$/);
  });

  it('con una sola cuota el mensaje habla en singular', async () => {
    const ctx = await crear({
      cobro: {
        ...RESPUESTA_COBRO,
        pagosAbonados: [10],
        cuotas: [RESPUESTA_COBRO.cuotas[0]],
        montoTotal: 85000,
      },
    });

    await pagar(ctx, [10], 'Efectivo');

    expect(ctx.notificaciones.notify.mock.calls[0][0]).toMatch(/Se abonó 1 cuota por \$\s*85\.000\.$/);
  });

  it('tras varias cuotas refleja el estado real: marzo y abril Pagado en verde, mayo sigue Pendiente', async () => {
    const ctx = await crear({
      cuotas: [
        [MARZO, ABRIL, MAYO],
        [pagada(MARZO), pagada(ABRIL), MAYO],
      ],
      deuda: [DEUDA_INICIAL, [deudaDe(13, 'Mayo 2026')]],
    });

    await pagar(ctx, [10, 11]);

    expect(filaDe(ctx.el, 'Marzo 2026')).toContain('Pagado');
    expect(filaDe(ctx.el, 'Abril 2026')).toContain('Pagado');
    expect(filaDe(ctx.el, 'Mayo 2026')).toContain('Pendiente');
    expect(ctx.el.querySelectorAll('.cuotas-table .estado-pagado')).toHaveLength(2);
    expect(checkbox(ctx.el, 10).disabled).toBe(true);
    expect(checkbox(ctx.el, 13).disabled).toBe(false);
  });

  it('la deuda mostrada pasa de $255.000 a $85.000 según el total que devuelve el backend', async () => {
    const ctx = await crear({
      cuotas: [
        [MARZO, ABRIL, MAYO],
        [pagada(MARZO), pagada(ABRIL), MAYO],
      ],
      deuda: [DEUDA_INICIAL, [deudaDe(13, 'Mayo 2026')]],
    });
    expect(totalAdeudado(ctx.el)).toMatch(/\$\s*255\.000/);

    await pagar(ctx, [10, 11]);

    expect(totalAdeudado(ctx.el)).toMatch(/\$\s*85\.000/);
    expect(ctx.pagos.getEstadoDeCuenta).toHaveBeenCalledTimes(2);
  });

  it('si se pagó todo, muestra deuda $0 en vez de ocultar el total', async () => {
    const ctx = await crear({
      cuotas: [
        [MARZO, ABRIL],
        [pagada(MARZO), pagada(ABRIL)],
      ],
      deuda: [[deudaDe(10, 'Marzo 2026'), deudaDe(11, 'Abril 2026')], []],
    });

    await pagar(ctx, [10, 11]);

    expect(totalAdeudado(ctx.el)).toMatch(/^\$\s*0$/);
  });

  it('se queda en la misma ficha: no navega ni genera comprobantes, impresiones o descargas', async () => {
    const imprimir = vi.fn();
    const printOriginal = window.print;
    window.print = imprimir;
    const crearUrl = vi.fn();
    const createObjectUrlOriginal = URL.createObjectURL;
    URL.createObjectURL = crearUrl;
    try {
      const ctx = await crear();
      const componenteAntes = ctx.component;

      await pagar(ctx, [10, 11]);

      expect(TestBed.inject(Router).navigate).not.toHaveBeenCalled();
      expect(ctx.fixture.componentInstance).toBe(componenteAntes);
      expect(imprimir).not.toHaveBeenCalled();
      expect(crearUrl).not.toHaveBeenCalled();
      expect(ctx.el.querySelector('a[download]')).toBeNull();
      expect(texto(ctx.el)).not.toMatch(/imprimir|comprobante|recibo|descargar|pdf/i);
    } finally {
      window.print = printOriginal;
      URL.createObjectURL = createObjectUrlOriginal;
    }
  });

  it('si el pago se registró pero falla la relectura, avisa sin negar el pago y sin volver a cobrar', async () => {
    const ctx = await crear({
      cuotas: [
        [MARZO, ABRIL, MAYO],
        new HttpErrorResponse({ status: 0 }),
        [pagada(MARZO), pagada(ABRIL), MAYO],
      ],
      deuda: [DEUDA_INICIAL, new HttpErrorResponse({ status: 0 }), [deudaDe(13, 'Mayo 2026')]],
    });

    await pagar(ctx, [10, 11]);

    expect(ctx.notificaciones.notify).toHaveBeenCalledTimes(1);
    expect(ctx.notificaciones.notify).toHaveBeenCalledWith(
      expect.stringContaining('Pago registrado correctamente'),
      'success',
    );
    expect(ctx.el.querySelector('[role="dialog"]')).toBeNull();
    const aviso = texto(ctx.el.querySelector('.aviso-actualizacion'));
    expect(aviso).toContain('El pago se registró correctamente');
    expect(aviso).toContain('no se pudo actualizar');
    expect(ctx.pagos.cobrarCuotas).toHaveBeenCalledTimes(1);

    ctx.el.querySelector<HTMLButtonElement>('.reintentar-actualizacion')!.click();
    await ctx.refrescar();

    expect(ctx.pagos.cobrarCuotas).toHaveBeenCalledTimes(1);
    expect(ctx.el.querySelector('.aviso-actualizacion')).toBeNull();
    expect(filaDe(ctx.el, 'Marzo 2026')).toContain('Pagado');
    expect(totalAdeudado(ctx.el)).toMatch(/\$\s*85\.000/);
  });

  it('ante un error del cobro no muestra éxito, deja el modal abierto con el error y no toca la deuda', async () => {
    const ctx = await crear({
      cuotas: [[MARZO, ABRIL, MAYO]],
      deuda: [DEUDA_INICIAL],
      cobro: new HttpErrorResponse({
        status: 500,
        error: { exito: false, mensaje: 'Error interno al procesar el cobro.' },
      }),
    });

    await pagar(ctx, [10, 11]);

    expect(ctx.notificaciones.notify).not.toHaveBeenCalledWith(expect.anything(), 'success');
    expect(ctx.el.querySelector('[role="dialog"]')).not.toBeNull();
    expect(texto(ctx.el.querySelector('.cobro-error'))).toContain('Error interno al procesar el cobro.');
    expect(totalAdeudado(ctx.el)).toMatch(/\$\s*255\.000/);
    expect(filaDe(ctx.el, 'Marzo 2026')).toContain('Vencido');
    expect(ctx.el.querySelector('.aviso-actualizacion')).toBeNull();
  });
});

// ===== HU-024: estado de cuenta del alumno =====
// Total a abonar y tabla de cuotas desde GET /api/pagos/jugador/{id}/estado-de-cuenta.

const etiquetaTotal = (el: HTMLElement) => texto(el.querySelector('.total-adeudado-label'));

describe('DeudaJugador - estado de cuenta (HU-024)', () => {
  it('toma la tabla y el total del estado de cuenta, sin pedir el listado de cuotas aparte', async () => {
    const { pagos, el } = await crear();

    expect(pagos.getEstadoDeCuenta).toHaveBeenCalledExactlyOnceWith(3);
    expect('getCuotas' in pagos).toBe(false);
    expect(el.querySelectorAll('.cuotas-table tbody tr')).toHaveLength(4);
  });

  it('muestra "Total a abonar" con el total que calculó el backend', async () => {
    const { el } = await crear();

    expect(etiquetaTotal(el)).toBe('Total a abonar');
    expect(totalAdeudado(el)).toMatch(/\$\s*170\.000/);
  });

  it('el total es el del estado de cuenta: una inscripción pendiente no lo suma', async () => {
    const inscripcion = { ...deudaDe(20, 'Octubre 2026', 50000), concepto: 'Inscripcion' };
    const { el } = await crear({ cuotas: [[MARZO, ABRIL]], deuda: [[...DEUDA_INICIAL.slice(0, 2), inscripcion]] });

    expect(totalAdeudado(el)).toMatch(/\$\s*170\.000/);
  });

  it('sin deuda muestra $0 y "Sin deuda"', async () => {
    const { el } = await crear({ cuotas: [[ENERO_PAGADA, pagada(MARZO)]] });

    expect(totalAdeudado(el)).toMatch(/^\$\s*0$/);
    expect(etiquetaTotal(el)).toBe('Sin deuda');
    expect(el.querySelector('.total-adeudado')!.classList).toContain('profile-stat-ok');
  });

  it('cuenta las cuotas adeudadas (no las pagadas ni las cubiertas por beneficio)', async () => {
    const { el } = await crear();

    const [, cantidad] = Array.from(el.querySelectorAll('.profile-stat')).map((stat) => [
      texto(stat.querySelector('.profile-stat-value')),
      texto(stat.querySelector('.profile-stat-label')),
    ]);
    expect(cantidad).toEqual(['2', 'Cuotas adeudadas']);
  });

  it('pinta cada estado con su color: Pagado verde, Pendiente ámbar, Vencido rojo', async () => {
    const { el } = await crear({ cuotas: [[ENERO_PAGADA, MARZO, ABRIL]] });

    const pill = (periodo: string) =>
      Array.from(el.querySelectorAll('.cuotas-table tbody tr'))
        .find((tr) => texto(tr).includes(periodo))!
        .querySelector('.estado-pill')!;
    expect(pill('Enero 2026').classList).toContain('estado-pagado');
    expect(pill('Marzo 2026').classList).toContain('estado-vencida');
    expect(pill('Abril 2026').classList).toContain('estado-pendiente');
    expect(texto(pill('Abril 2026'))).toBe('Pendiente');
  });

  it('muestra las cuotas en el orden cronológico que devuelve el backend', async () => {
    const { el } = await crear();

    const periodos = Array.from(el.querySelectorAll('.cuotas-table .cuota-periodo')).map(texto);
    expect(periodos).toEqual(['Enero 2026', 'Marzo 2026', 'Abril 2026', 'Mayo 2026']);
  });

  it('muestra el indicador de carga mientras el estado de cuenta no llegó', async () => {
    const { el } = await crear({ cuotas: [new Subject<PlayerStatement>()] });

    expect(texto(el)).toContain('Cargando cuotas...');
    expect(el.querySelector('.total-adeudado')).toBeNull();
  });

  it('ante un error de conexión avisa en español con un toast y en la sección', async () => {
    const { el, notificaciones } = await crear({ cuotas: [new HttpErrorResponse({ status: 0 })] });

    const mensaje = 'No se pudo conectar con el servidor. Verificá tu conexión e intentá de nuevo.';
    expect(notificaciones.notify).toHaveBeenCalledExactlyOnceWith(mensaje, 'error');
    expect(texto(el.querySelector('.panel-cuotas .cobro-error'))).toBe(mensaje);
    expect(el.querySelector('.total-adeudado')).toBeNull();
  });

  it('ante un error del servidor usa un mensaje propio del estado de cuenta', async () => {
    const { notificaciones } = await crear({ cuotas: [new HttpErrorResponse({ status: 500 })] });

    expect(notificaciones.notify).toHaveBeenCalledWith(
      'No se pudo cargar el estado de cuenta de este jugador.',
      'error',
    );
  });
});
