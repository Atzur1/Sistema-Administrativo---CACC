namespace ApiGestion.Models;

// Contrato de integración v1 (HU-057). La fecha llega como texto (yyyy-MM-dd) y la valida el servicio: así un formato
// equivocado responde con el mismo error de negocio ("field": "fechaBaja") que una fecha futura.
public sealed class BajaIntegracionRequestDTO
{
    // Día en que el jugador dejó el club (fecha efectiva, no la del pedido). Obligatoria.
    public string? FechaBaja { get; init; }
}

// Nunca lleva montos ni cuántas cuotas se quitaron: el Portal Deportivo solo recibe el estado.
public sealed class BajaIntegracionResponseDTO
{
    public int IdJugador { get; init; }

    // Siempre "baja": el mismo valor que devuelve estado-deuda.
    public string Estado { get; init; } = "baja";

    public string FechaBaja { get; init; } = string.Empty;

    // true si el pedido repetía una baja ya registrada con la misma fecha (un reintento): no se cambió nada.
    public bool YaEstabaDadoDeBaja { get; init; }
}

public sealed class ReincorporacionIntegracionRequestDTO
{
    // Categoría con la que vuelve. Sin valor, conserva la que tenía.
    public int? IdCategoria { get; init; }
}

public sealed class ReincorporacionIntegracionResponseDTO
{
    public int IdJugador { get; init; }

    // La fecha del reingreso: pasa a ser su fecha de alta.
    public string FechaAlta { get; init; } = string.Empty;

    public bool CategoriaCambiada { get; init; }
}
