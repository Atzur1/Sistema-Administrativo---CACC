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
}
