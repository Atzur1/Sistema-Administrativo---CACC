using EntityLibrary;

namespace ApiGestion.Models;

// A fee that would be generated for a month that was left without them: who and how much.
public class MissingFeeResponseDTO
{
    public int IdJugador { get; set; }
    public string NombreCompleto { get; set; } = "";
    public string Dni { get; set; } = "";
    public string Categoria { get; set; } = "";
    public decimal Monto { get; set; }

    public static MissingFeeResponseDTO FromEntity(CuotaFaltante fee)
    {
        return new MissingFeeResponseDTO
        {
            IdJugador = fee.IdJugador,
            NombreCompleto = fee.NombreCompleto,
            Dni = fee.Dni,
            Categoria = fee.Categoria,
            Monto = fee.Monto
        };
    }
}
