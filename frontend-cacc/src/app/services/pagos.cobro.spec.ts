import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { PagosService } from './pagos';

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
