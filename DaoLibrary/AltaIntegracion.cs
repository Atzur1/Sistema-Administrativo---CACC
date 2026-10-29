using EntityLibrary;

namespace DaoLibrary;

// HU-056: el alta de jugadores que llega por la API de integración. Estos tipos son el idioma entre el servicio (que decide
// las reglas de cobro) y PlayerDAO (que graba todo en una sola transacción).

// Lo que pide el Portal Deportivo, ya validado en su forma. Nombre, apellido y nacimiento solo se usan si la persona todavía no
// existe. La fecha de alta no viene del Deportivo: la decide el Administrativo (el día de hoy del club).
public sealed record DatosAltaIntegracion(
    string Dni, string? Nombre, string? Apellido, string Genero, DateTime? FechaNacimiento, int IdCategoria, DateTime FechaAlta);

// Lo que el DAO le cuenta al servicio, dentro de la transacción, para que decida los cargos: si el jugador se está creando
// ahora o ya existía (en ese caso con SU categoría y SU fecha de alta, no las del pedido).
public sealed record ContextoCargosAlta(bool JugadorNuevo, string Genero, int IdCategoria, DateTime FechaAlta);

// Los cargos que el servicio decidió. Un monto null = ese cargo no se genera. Si Rechazo trae un mensaje, el alta entera se
// rechaza (se revierte todo).
public sealed record PlanCargosAlta(decimal? MontoCuota, DateTime PrimerDiaDelMes, decimal? MontoInscripcion, string? Rechazo = null);

public enum EstadoAltaIntegracion
{
    Creado,
    YaExistia,
    FaltanDatosDePersona,
    GeneroNoCoincide,
    VariosJugadores,
    JugadorDeBaja,
    CargosRechazados,
    // Dos o más personas con el mismo DNI canónico (por ejemplo, uno histórico guardado con espacios): no se elige ni se fusiona.
    VariasPersonas
}

// Campo: el dato que falta cuando el estado es FaltanDatosDePersona. Mensaje: el motivo cuando los cargos se rechazan.
public sealed record ResultadoAltaIntegracion(
    EstadoAltaIntegracion Estado, int IdPersona, int IdJugador, bool PersonaExistente, CargosGenerados Cargos,
    string? Campo = null, string? Mensaje = null);

public enum EstadoCambioCategoria
{
    NoEncontrado,
    DeBaja,
    SinCambio,
    Cambiado
}

public sealed record ResultadoCambioCategoria(EstadoCambioCategoria Estado, int IdCategoria);

// Un pedido de integración que se rechazó por una regla (400/404/409/422). Nunca lleva el DNI ni nombres: solo ids internos.
// El operador y el sistema de origen los toma el DAO del contexto del pedido (SqlAuditActorContext).
public sealed record RechazoIntegracion(
    string Operacion, string ErrorCode, string Motivo, string? Correlacion, int? IdPersona, int? IdJugador);
