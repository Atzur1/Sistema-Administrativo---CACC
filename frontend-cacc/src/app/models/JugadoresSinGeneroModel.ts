// Un jugador en el club cuya persona no tiene género cargado: sin ese dato no se le puede emitir la cuota.
export interface JugadorSinGeneroModel {
  idJugador: number;
  nombreCompleto: string;
  dni: string;
  categoria: string;
}

// total es el real; jugadores trae solo los primeros (para no cargar miles de filas en el Resumen General).
export interface JugadoresSinGeneroModel {
  total: number;
  jugadores: JugadorSinGeneroModel[];
}
