import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { PagosService, feeToCuotaJugador } from './pagos';

// HU-025: la ficha financiera lee las cuotas con su estado y cobra varias en un solo POST.
describe('PagosService - cobro de cuotas (HU-025)', () => {
  let service: PagosService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(PagosService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lee las cuotas del jugador con GET', () => {
    service.getCuotas(12).subscribe();

    const req = http.expectOne('/api/pagos/cuotas/12');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('lee el estado de cuenta del jugador con GET (HU-024)', () => {
    service.getEstadoDeCuenta(12).subscribe();

    const req = http.expectOne('/api/pagos/jugador/12/estado-de-cuenta');
    expect(req.request.method).toBe('GET');
    req.flush({ playerId: 12, playerFullName: '', dni: '', totalDebtAmount: 0, fees: [] });
  });

  it('adapta cada cuota del estado de cuenta a la fila de la tabla de cobro (HU-024)', () => {
    expect(
      feeToCuotaJugador({
        id: 10,
        periodName: 'Marzo 2026',
        amount: 85000,
        amountDue: 42500,
        amountPaid: 0,
        dueDate: '2026-03-31',
        status: 'Vencido',
        paidAt: null,
        paymentMethod: null,
        coveredByBenefit: false,
        benefitReason: 'Descuento',
      }),
    ).toEqual({
      idPago: 10,
      periodo: 'Marzo 2026',
      fechaVencimiento: '2026-03-31',
      montoCuota: 85000,
      saldoPendiente: 42500,
      montoAbonado: 0,
      estado: 'Vencido',
      cubiertaPorBeneficio: false,
      motivoBeneficio: 'Descuento',
      metodoPago: null,
      fechaPago: null,
    });
  });

  it('cobra las cuotas seleccionadas con un único POST a /pagos/cobro', () => {
    service.cobrarCuotas(12, [10, 11], 'Transferencia').subscribe();

    const req = http.expectOne('/api/pagos/cobro');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ idJugador: 12, idsPago: [10, 11], metodoPago: 'Transferencia' });
    req.flush({});
  });

  it('nunca envía el usuario en el body: el backend lo toma del token', () => {
    service.cobrarCuotas(12, [10], 'Efectivo').subscribe();

    const req = http.expectOne('/api/pagos/cobro');
    expect(Object.keys(req.request.body)).not.toContain('idUsuario');
    expect(Object.keys(req.request.body)).not.toContain('idUsuarioRegistro');
    req.flush({});
  });
});
