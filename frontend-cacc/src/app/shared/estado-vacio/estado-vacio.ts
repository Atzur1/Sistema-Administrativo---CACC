import { Component, EventEmitter, Input, Output } from '@angular/core';

// Estado vacío del Portal Administrativo (HU-095): ícono, un mensaje corto, una aclaración opcional y una
// acción opcional. Reemplaza los párrafos grises sueltos ("No hay…") de cada pantalla.
//
// Redacción (la misma en todas las pantallas):
//   - Sin datos:            "No hay <qué>" + el período o contexto si hace falta ("No hay cobros en octubre 2026").
//   - Sin resultados:       "Ningún resultado coincide con la búsqueda o los filtros", con icono="busqueda" y,
//                           si la pantalla puede limpiarlos, textoAccion="Limpiar filtros".
//   - Nada pendiente (bien): icono="listo" ("Este jugador no tiene cuotas pendientes").
//   Sin punto final en el mensaje; la aclaración sí lleva punto.
//
// Uso:
//   <app-estado-vacio icono="busqueda" mensaje="Ningún resultado coincide con la búsqueda o los filtros"
//                     textoAccion="Limpiar filtros" (accion)="limpiarFiltros()" />
@Component({
  selector: 'app-estado-vacio',
  standalone: true,
  templateUrl: './estado-vacio.html',
  styleUrl: './estado-vacio.css',
})
export class EstadoVacio {
  @Input({ required: true }) mensaje = '';
  @Input() detalle = '';
  @Input() icono: 'vacio' | 'busqueda' | 'listo' = 'vacio';
  // Sin texto no hay botón.
  @Input() textoAccion = '';
  // Más bajo, para paneles chicos (una tarjeta lateral, una lista corta).
  @Input() compacto = false;

  @Output() readonly accion = new EventEmitter<void>();
}
