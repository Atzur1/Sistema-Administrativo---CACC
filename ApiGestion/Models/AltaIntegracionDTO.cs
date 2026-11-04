namespace ApiGestion.Models;

public sealed class AltaIntegracionRequestDTO
{
    public string? Dni { get; init; }
    public string? Nombre { get; init; }
    public string? Apellido { get; init; }
    public string? Genero { get; init; }
    public string? FechaNacimiento { get; init; }
    public int? IdCategoria { get; init; }
}

public sealed class AltaIntegracionResponseDTO
{
    public int IdJugador { get; init; }
    public bool Creado { get; init; }
    public bool PersonaExistente { get; init; }
    public bool InscripcionGenerada { get; init; }
    public bool CuotaGenerada { get; init; }
}

public sealed class CambioCategoriaIntegracionRequestDTO
{
    public int? IdCategoria { get; init; }
}

public sealed class CambioCategoriaIntegracionResponseDTO
{
    public int IdJugador { get; init; }
    public int IdCategoria { get; init; }
    public bool Cambiado { get; init; }
}
