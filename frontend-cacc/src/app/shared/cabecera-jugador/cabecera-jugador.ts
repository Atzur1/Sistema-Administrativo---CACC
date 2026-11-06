import { NgClass } from '@angular/common';
import { Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { Router } from '@angular/router';
import { EstadoDeportivo } from '../../models/PaymentModel';

let siguienteId = 0;

// Lo que la cabecera necesita del jugador. PlayerSummaryModel (GET api/jugadores/{id}) ya lo cumple, así que cada
// pantalla le pasa el jugador que YA cargó: la cabecera nunca pide datos a la API.
export interface DatosCabeceraJugador {
  nombre: string;
  apellido: string;
  dni: string;
  categoria?: string | null;
  // Formato del backend ("APELLIDO, Nombre"). Si no viene, se arma con la misma convención.
  nombreCompleto?: string | null;
  // Calculado por el backend (EstadoDeudaJugador). La cabecera lo presenta; nunca lo deduce de cuotas.
  estadoDeportivo?: EstadoDeportivo | null;
  fechaBaja?: string | null;
  // Todavía no hay fotos de jugadores en el sistema: queda preparado y, sin foto, se muestran las iniciales.
  fotoUrl?: string | null;
}

// Mismos textos y tonos que Deudas y Morosidad (HU-093): Dado de baja va en neutro porque ya no tiene situación
// deportiva.
const ESTADOS: Record<EstadoDeportivo, { texto: string; tono: string }> = {
  habilitado: { texto: 'Habilitado', tono: 'tono-exito' },
  solo_entrenamientos: { texto: 'Solo entrenamientos', tono: 'tono-aviso' },
  inhabilitado: { texto: 'Inhabilitado', tono: 'tono-peligro' },
  baja: { texto: 'Dado de baja', tono: 'tono-neutro' },
};

const TITULOS_ESTADO: Record<EstadoDeportivo, string> = {
  habilitado: 'Puede entrenar y jugar partidos oficiales',
  solo_entrenamientos: '1 cuota vencida: puede entrenar, no jugar partidos oficiales',
  inhabilitado: '2 o más cuotas vencidas: no puede entrenar ni jugar partidos oficiales',
  baja: 'Dejó el club: no genera cuotas nuevas, pero se le puede cobrar lo que debe',
};

// Cabecera única de jugador (HU-096) para Perfil y Deuda: Volver, foto o iniciales, nombre (el h1 de la pantalla),
// DNI, categoría y estado deportivo. Cada pantalla agrega lo suyo:
//   - acciones propias con el atributo cabeceraAcciones (Perfil: Dar de baja / Reactivar);
//   - contenido propio debajo de los datos, dentro de la misma tarjeta (los indicadores de cada pantalla).
// La carga y el error los sigue mostrando la pantalla: sin jugador, la cabecera solo muestra Volver.
//
// Uso:
//   <app-cabecera-jugador [jugador]="jugador" (volver)="volver()">
//     <button cabeceraAcciones type="button">Dar de baja</button>
//     <div class="profile-stats">…</div>
//   </app-cabecera-jugador>
@Component({
  selector: 'app-cabecera-jugador',
  standalone: true,
  imports: [NgClass],
  templateUrl: './cabecera-jugador.html',
  styleUrl: './cabecera-jugador.css',
})
export class CabeceraJugador {
  private readonly router = inject(Router);

  @Input() jugador: DatosCabeceraJugador | null | undefined = null;
  // Destino de Volver cuando la pantalla no escucha (volver). Mismo formato que router.navigate.
  @Input() volverA: string | readonly unknown[] | null = null;
  @Input() textoVolver = 'Volver';

  // Si la pantalla lo escucha, ella decide adónde volver (así conserva su destino de siempre).
  @Output() readonly volver = new EventEmitter<void>();

  readonly idTitulo = `cabecera-jugador-${++siguienteId}`;
  private fotoFallida: string | null = null;

  get nombreCompleto(): string {
    const j = this.jugador;
    if (!j) {
      return '';
    }
    const backend = j.nombreCompleto?.trim();
    return backend || [j.apellido?.trim(), j.nombre?.trim()].filter(Boolean).join(', ');
  }

  get iniciales(): string {
    const j = this.jugador;
    if (!j) {
      return '';
    }
    const primera = (valor: string | null | undefined) => Array.from((valor ?? '').trim())[0] ?? '';
    return `${primera(j.apellido)}${primera(j.nombre)}`.toLocaleUpperCase('es-AR');
  }

  get categoria(): string {
    return this.jugador?.categoria?.trim() || 'Sin categoría';
  }

  get mostrarFoto(): boolean {
    const url = this.jugador?.fotoUrl;
    return !!url && url !== this.fotoFallida;
  }

  get textoEstado(): string {
    const estado = this.jugador?.estadoDeportivo;
    if (!estado) {
      return 'Estado no disponible';
    }
    const fecha = estado === 'baja' ? this.fechaBaja : '';
    return fecha ? `${ESTADOS[estado].texto} el ${fecha}` : ESTADOS[estado].texto;
  }

  get tonoEstado(): string {
    const estado = this.jugador?.estadoDeportivo;
    return estado ? ESTADOS[estado].tono : 'tono-neutro';
  }

  get tituloEstado(): string | null {
    const estado = this.jugador?.estadoDeportivo;
    return estado ? TITULOS_ESTADO[estado] : null;
  }

  // La fecha de baja es un dato del jugador, no un cálculo: si el estado no llegó, igual se informa.
  get bajaSinEstado(): boolean {
    return !this.jugador?.estadoDeportivo && !!this.fechaBaja;
  }

  // "2026-09-30" o "2026-09-30T00:00:00" -> "30/09/2026", sin pasar por Date (no se corre por zona horaria).
  get fechaBaja(): string {
    const [anio, mes, dia] = (this.jugador?.fechaBaja ?? '').slice(0, 10).split('-');
    return anio && mes && dia ? `${dia}/${mes}/${anio}` : '';
  }

  fotoNoCargo() {
    this.fotoFallida = this.jugador?.fotoUrl ?? null;
  }

  irAtras() {
    if (this.volver.observed) {
      this.volver.emit();
      return;
    }
    if (this.volverA) {
      this.router.navigate(typeof this.volverA === 'string' ? [this.volverA] : [...this.volverA]);
    }
  }
}
