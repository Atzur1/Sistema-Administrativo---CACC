using EntityLibrary;

namespace ApiGestion.Models;

// Row of "Historial y aranceles programados", with validity and status already worked out.
public class TariffHistoryItemResponseDTO
{
    public int IdArancel { get; set; }
    public string? Genero { get; set; }
    public int? IdCategoria { get; set; }
    public string? NombreCategoria { get; set; }
    public decimal Monto { get; set; }
    public DateTime VigenteDesde { get; set; }
    public DateTime? VigenteHasta { get; set; }
    public string Estado { get; set; } = "";
    public bool PuedeCancelar { get; set; }
    public string? ResponsableNombre { get; set; }
    public string? ResponsableApellido { get; set; }

    public static TariffHistoryItemResponseDTO FromEntity(ArancelHistorialItem tariff)
    {
        return new TariffHistoryItemResponseDTO
        {
            IdArancel = tariff.IdArancel,
            Genero = tariff.Genero,
            IdCategoria = tariff.IdCategoria,
            NombreCategoria = tariff.NombreCategoria,
            Monto = tariff.Monto,
            VigenteDesde = tariff.VigenteDesde,
            VigenteHasta = tariff.VigenteHasta,
            Estado = tariff.Estado,
            PuedeCancelar = tariff.PuedeCancelar,
            ResponsableNombre = tariff.ResponsableNombre,
            ResponsableApellido = tariff.ResponsableApellido
        };
    }
}
