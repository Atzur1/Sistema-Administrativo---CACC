namespace EntityLibrary;

// Un jugador en el club cuya persona no tiene un género válido (Masculino o Femenino). Sin ese dato el sistema no le
// encuentra arancel de género (ni la inscripción por género), y por lo tanto no se le puede emitir la cuota.
public sealed record JugadorSinGenero(int IdJugador, string NombreCompleto, string Dni, string Categoria);

// Total de jugadores en esa situación y los primeros de la lista (para no traer miles de filas al Resumen General).
public sealed record JugadoresSinGenero(int Total, IReadOnlyList<JugadorSinGenero> Primeros);
