import { Component, Input } from '@angular/core';

// Indicador de carga del Portal Administrativo (HU-095): bloques grises con la forma de lo que va a aparecer
// (tabla, tarjetas o lista), en vez de un "Cargando…" suelto. Así se distingue "está trabajando" de "no hay
// nada" (para eso está app-estado-vacio).
//
// Redacción (la misma en todas las pantallas): "Cargando <qué>…", con el signo de puntos suspensivos (…),
// no tres puntos. El texto no se ve: lo anuncian los lectores de pantalla (role="status" + aria-live).
//
// Uso:
//   @if (cargando) {
//     <app-cargando texto="Cargando cobros pendientes…" forma="tabla" [filas]="5" [columnas]="4" />
//   }
//
// Con "movimiento reducido" activado en el sistema operativo los bloques quedan quietos.
@Component({
  selector: 'app-cargando',
  standalone: true,
  templateUrl: './cargando.html',
  styleUrl: './cargando.css',
})
export class Cargando {
  @Input({ required: true }) texto = '';
  @Input() forma: 'tabla' | 'tarjetas' | 'lista' = 'lista';
  @Input() filas = 3;
  @Input() columnas = 4;

  get listaFilas(): number[] {
    return Array.from({ length: Math.max(1, this.filas) }, (_, i) => i);
  }

  get listaColumnas(): number[] {
    return Array.from({ length: Math.max(1, this.columnas) }, (_, i) => i);
  }
}
