import { EstadoDeportivo } from './PaymentModel';

// Contract of GET api/jugadores and GET api/jugadores/{id} (PlayerSummaryResponseDTO).
export interface PlayerSummaryModel {
  idJugador: number;
  nombre: string;
  apellido: string;
  dni: string;
  genero: string; // "Masculino" | "Femenino"
  categoria: string;
  nombreCompleto: string;
  fechaAlta?: string | null;
  // null = activo. Con fecha, el jugador dejó el club y no se le emiten más cuotas.
  fechaBaja?: string | null;
  // HU-096: solo lo informa GET api/jugadores/{id}, calculado por el backend (EstadoDeudaJugador). null = no
  // disponible: la cabecera lo dice así y no lo recalcula.
  estadoDeportivo?: EstadoDeportivo | null;
}
