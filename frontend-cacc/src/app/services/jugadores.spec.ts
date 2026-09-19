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

    const req = http.expectOne('http://localhost:5118/api/jugadores/3/historial-pagos?page=2&pageSize=10');
    expect(req.request.method).toBe('GET');
    req.flush({ items: [], total: 0, page: 2, pageSize: 10 });
  });

  it('devuelve el historial tal como lo entrega la API', () => {
    const respuesta: HistorialPagosResultado = {
      items: [
        {
          periodo: 'Septiembre 2026',
          montoTotal: 85000,
          montoOriginal: 85000,
          tieneBeneficio: false,
          motivoBeneficio: null,
          tipoValorBeneficio: null,
          porcentajeBeneficio: null,
          montoFijoBeneficio: null,
          abonos: [{ monto: 85000, metodoPago: 'Transferencia', fechaPago: '2026-09-19T00:00:00' }],
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

    const req = http.expectOne('http://localhost:5118/api/jugadores/3');
    expect(req.request.method).toBe('GET');
    req.flush({});
  });

  it('no expone operaciones de escritura', () => {
    const metodos = Object.getOwnPropertyNames(JugadoresService.prototype).filter((nombre) => nombre !== 'constructor');

    expect(metodos.sort()).toEqual(['getHistorialPagos', 'getJugador']);
  });
});
