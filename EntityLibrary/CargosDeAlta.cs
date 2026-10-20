namespace EntityLibrary;

// Lo que hace falta saber de un jugador que ya existe para decidir los cargos de su alta.
public sealed record JugadorParaCargos(int IdJugador, string Genero, int IdCategoria, DateTime FechaAlta, DateTime? FechaBaja);

// Qué cargos se generaron en esta llamada (false = ya existía o no correspondía generarlo). Nunca lleva montos.
public sealed record CargosGenerados(bool Inscripcion, bool Cuota);
