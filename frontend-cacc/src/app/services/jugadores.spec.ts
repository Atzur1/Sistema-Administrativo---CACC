import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { HistorialPagosResultado, JugadoresService } from './jugadores';

// HU-017: el servicio del perfil de jugador solo consulta (GET), nunca modifica pagos.
describe('JugadoresService', () => {
  let servicio: JugadoresService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    servicio = TestBed.inject(JugadoresService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('pide el historial de pagos con GET, la página y el tamaño indicados', () => {
    servicio.getHistorialPagos(3, 2, 10).subscribe();

    const req = http.expectOne('/api/jugadores/3/historial-pagos?page=2&pageSize=10');
    expect(req.request.method).toBe('GET');
    req.flush({ items: [], total: 0, page: 2, pageSize: 10 });
  });

  it('devuelve el historial tal como lo entrega la API', () => {
    const respuesta: HistorialPagosResultado = {
      items: [
        {
          periodo: 'Septiembre 2026',
          concepto: 'Cuota',
          montoTotal: 85000,
          montoOriginal: 85000,
          tieneBeneficio: false,
          motivoBeneficio: null,
          tipoValorBeneficio: null,
          porcentajeBeneficio: null,
          montoFijoBeneficio: null,
          abonos: [{ idPago: 1, monto: 85000, metodoPago: 'Transferencia', fechaPago: '2026-09-19T00:00:00' }],
        },
      ],
      total: 1,
      page: 1,
      pageSize: 10,
    };
    let recibido: HistorialPagosResultado | undefined;

    servicio.getHistorialPagos(3, 1, 10).subscribe((resultado) => (recibido = resultado));
    http.expectOne(() => true).flush(respuesta);

    expect(recibido).toEqual(respuesta);
  });

  it('pide los datos básicos del jugador con GET', () => {
    servicio.getJugador(3).subscribe();

    const req = http.expectOne('/api/jugadores/3');
    expect(req.request.method).toBe('GET');
    req.flush({});
  });

  // El historial sigue sin operaciones que editen importes ni fechas. Las únicas escrituras son la baja y
  // reactivación del jugador y la anulación auditada de un pago cargado por error (con motivo, solo SuperAdmin).
  it('solo expone como escrituras la baja/reactivación y la anulación auditada de pagos', () => {
    const metodos = Object.getOwnPropertyNames(JugadoresService.prototype).filter((nombre) => nombre !== 'constructor');

    expect(metodos.sort()).toEqual(['anularPago', 'darDeBaja', 'getHistorialPagos', 'getJugador', 'reactivar']);
  });

  it('anula un pago por su id y con el motivo, contra el endpoint de pagos', () => {
    servicio.anularPago(45, 'Se cargó dos veces').subscribe();

    const req = http.expectOne('/api/pagos/45/anular');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ motivo: 'Se cargó dos veces' });
    req.flush({ mensaje: 'ok', saldoReabierto: 85000 });
  });

  it('da de baja al jugador sin fecha (el servidor toma hoy)', () => {
    servicio.darDeBaja(3).subscribe();

    const req = http.expectOne('/api/jugadores/3/baja');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({});
    req.flush({ mensaje: 'ok' });
  });
});
