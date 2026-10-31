import { Subject } from 'rxjs';

import { LatestRequest } from './latest-request';

// HU-097: una respuesta vieja nunca pisa la de la consulta más reciente.
describe('LatestRequest', () => {
  it('a new request cancels the previous one, so its late answer is ignored', () => {
    const consulta = new LatestRequest();
    const vieja = new Subject<string>();
    const nueva = new Subject<string>();
    const recibidas: string[] = [];

    consulta.run(vieja, { next: (v) => recibidas.push(v) });
    consulta.run(nueva, { next: (v) => recibidas.push(v) });

    expect(vieja.observed).toBe(false);   // cancelada (con HttpClient, la petición se aborta)
    nueva.next('octubre');
    vieja.next('septiembre');             // llega tarde: nadie la escucha

    expect(recibidas).toEqual(['octubre']);
  });

  it('cancel() stops the request in flight', () => {
    const consulta = new LatestRequest();
    const fuente = new Subject<number>();
    const recibidas: number[] = [];

    consulta.run(fuente, { next: (v) => recibidas.push(v) });
    consulta.cancel();
    fuente.next(1);

    expect(recibidas).toEqual([]);
    expect(fuente.observed).toBe(false);
  });
});
