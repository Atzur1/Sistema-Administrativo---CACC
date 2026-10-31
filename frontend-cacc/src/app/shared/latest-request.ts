import { Observable, Observer, Subscription } from 'rxjs';

// Solo vale la respuesta de la última consulta (HU-097). Al lanzar una consulta nueva se cancela la anterior: con
// HttpClient, desuscribirse aborta la petición, así una respuesta lenta de un filtro viejo nunca pisa los datos del
// filtro que se eligió después.
//
// Uso:
//   private readonly consulta = new LatestRequest();
//   cargar() { this.consulta.run(this.service.get(filtros), { next: …, error: … }); }
//   (y consulta.cancel() al destruir la pantalla)
export class LatestRequest {
  private current: Subscription | undefined;

  run<T>(source: Observable<T>, observer: Partial<Observer<T>>): void {
    this.cancel();
    this.current = source.subscribe(observer);
  }

  cancel(): void {
    this.current?.unsubscribe();
    this.current = undefined;
  }
}
