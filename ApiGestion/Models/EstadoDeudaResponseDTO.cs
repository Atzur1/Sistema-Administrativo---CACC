using EntityLibrary;

namespace ApiGestion.Models;

// Contrato de integración v1. El Portal Deportivo recibe SOLO el estado de habilitación de cada jugador: nunca montos,
// cantidad de cuotas, meses adeudados ni medios de pago (HU-028). Agregar un dato financiero acá es un cambio de contrato.
public sealed class EstadoDeudaResponseDTO
{
    public string Dni { get; init; } = "";
    public int IdJugador { get; init; }

    // "habilitado", "solo_entrenamientos" (puede entrenar, no jugar partidos oficiales), "inhabilitado" o "baja".
    public string Estado { get; init; } = "";

    // Puede entrar a entrenar: habilitado o solo entrenamientos.
    public bool AccesoPermitido { get; init; }

    // Fecha de la baja (yyyy-MM-dd) si el jugador está dado de baja.
    public string? FechaBaja { get; init; }

    public static EstadoDeudaResponseDTO FromEntity(EstadoDeudaJugador estado) => new()
    {
        Dni = estado.Dni,
        IdJugador = estado.IdJugador,
        Estado = estado.Estado switch
        {
            EstadoDeportivo.Habilitado => "habilitado",
            EstadoDeportivo.SoloEntrenamientos => "solo_entrenamientos",
            EstadoDeportivo.Inhabilitado => "inhabilitado",
            _ => "baja"
        },
        AccesoPermitido = estado.AccesoPermitido,
        FechaBaja = estado.FechaBaja?.ToString("yyyy-MM-dd")
    };
}

public sealed class EstadoDeudaLoteRequestDTO
{
    public List<string>? Dnis { get; init; }
}

public sealed class EstadoDeudaLoteResponseDTO
{
    public List<EstadoDeudaResponseDTO> Items { get; init; } = new();

    // DNI que no son un DNI válido, no corresponden a un jugador o no pertenecen al alcance del que consulta.
    public List<string> NoEncontrados { get; init; } = new();
}
