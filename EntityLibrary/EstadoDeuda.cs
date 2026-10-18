namespace EntityLibrary;

// Situación deportiva de un jugador según lo que debe. Es la regla del club: se cuentan solo las cuotas YA VENCIDAS
// (vencen el día 10 del mes siguiente, ver CalendarioCuotas): 0 = habilitado, 1 = solo entrenamientos, 2 o más = inhabilitado.
public enum EstadoDeportivo { Habilitado, SoloEntrenamientos, Inhabilitado, Baja }

// Lo que el Portal Deportivo necesita saber de un jugador para el QR de ingreso y para sus listados.
// DeudaTotal es todo lo pendiente (cuotas e inscripción) con la beca o descuento ya aplicado, igual que el resto del
// sistema; CuotasVencidas cuenta solo las cuotas con saldo que ya vencieron.
public sealed record EstadoDeudaJugador(int IdJugador, string Dni, int CuotasVencidas, decimal DeudaTotal, DateTime? FechaBaja)
{
    public EstadoDeportivo Estado => FechaBaja is not null
        ? EstadoDeportivo.Baja
        : CuotasVencidas switch
        {
            <= 0 => EstadoDeportivo.Habilitado,
            1 => EstadoDeportivo.SoloEntrenamientos,
            _ => EstadoDeportivo.Inhabilitado
        };

    // Puede entrar a entrenar: habilitado o con una sola cuota vencida. Quien se fue del club o debe dos o más, no.
    public bool AccesoPermitido => Estado is EstadoDeportivo.Habilitado or EstadoDeportivo.SoloEntrenamientos;

    // Debe algo, pero nada está vencido todavía ("en término").
    public bool EnTermino => FechaBaja is null && CuotasVencidas == 0 && DeudaTotal > 0;
}
