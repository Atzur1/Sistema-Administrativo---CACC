using EntityLibrary;

namespace ApiGestion.Models;

public sealed class EstadoDeudaResponseDTO
{
    public string Dni { get; init; } = "";
    public int IdJugador { get; init; }

    // "habilitado", "solo_entrenamientos", "inhabilitado" o "baja".
    public string Estado { get; init; } = "";
    public bool AccesoPermitido { get; init; }
    public int CuotasVencidas { get; init; }
    public decimal DeudaTotal { get; init; }
    public bool EnTermino { get; init; }
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
        CuotasVencidas = estado.CuotasVencidas,
        DeudaTotal = estado.DeudaTotal,
        EnTermino = estado.EnTermino,
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
