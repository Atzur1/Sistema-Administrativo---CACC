namespace ApiGestion.Models;

// Respuesta del aviso de baja del Portal Deportivo (contrato v1): solo cuántas cuotas futuras sin pagos se quitaron y cuántas
// becas se cancelaron, nunca montos.
public class BajaInformadaResponseDTO
{
    public int IdJugador { get; set; }
    public int CuotasFuturasQuitadas { get; set; }
    public int BeneficiosCancelados { get; set; }
}
