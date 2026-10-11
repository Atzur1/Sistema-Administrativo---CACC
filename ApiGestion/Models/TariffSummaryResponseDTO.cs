using EntityLibrary;

namespace ApiGestion.Models;

// Header of "Actualización de Aranceles": fee in force per gender and next scheduled change.
public class TariffSummaryResponseDTO
{
    public decimal? ArancelMasculinoVigente { get; set; }
    public decimal? ArancelFemeninoVigente { get; set; }
    public DateTime? ProximoCambioFecha { get; set; }

    public static TariffSummaryResponseDTO FromEntity(ArancelResumen summary)
    {
        return new TariffSummaryResponseDTO
        {
            ArancelMasculinoVigente = summary.ArancelMasculinoVigente,
            ArancelFemeninoVigente = summary.ArancelFemeninoVigente,
            ProximoCambioFecha = summary.ProximoCambioFecha
        };
    }
}
