using EntityLibrary;

namespace ApiGestion.Models;

// Player list and profile header (GET api/jugadores, GET api/jugadores/{id}).
public class PlayerSummaryResponseDTO
{
    public int IdJugador { get; set; }
    public string Nombre { get; set; } = "";
    public string Apellido { get; set; } = "";
    public string Dni { get; set; } = "";
    public string Categoria { get; set; } = "";
    public string Genero { get; set; } = "";
    public DateTime? FechaAlta { get; set; }

    // null while the player is active
    public DateTime? FechaBaja { get; set; }
    public bool Activo { get; set; }
    public string NombreCompleto { get; set; } = "";

    public static PlayerSummaryResponseDTO FromEntity(JugadorResumen player)
    {
        return new PlayerSummaryResponseDTO
        {
            IdJugador = player.IdJugador,
            Nombre = player.Nombre,
            Apellido = player.Apellido,
            Dni = player.Dni,
            Categoria = player.Categoria,
            Genero = player.Genero,
            FechaAlta = player.FechaAlta,
            FechaBaja = player.FechaBaja,
            Activo = player.Activo,
            NombreCompleto = player.NombreCompleto
        };
    }
}
