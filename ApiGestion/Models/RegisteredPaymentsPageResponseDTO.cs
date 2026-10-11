using EntityLibrary;

namespace ApiGestion.Models;

// One page of the "Pagos registrados" table, already filtered by the database.
public class RegisteredPaymentsPageResponseDTO
{
    public List<RecentPaymentResponseDTO> Items { get; set; } = new();

    // Payments that match the filters (for the paginator)
    public int Total { get; set; }

    // Payments without filters (for "N en total" and the empty state)
    public int TotalSinFiltros { get; set; }
    public int Pagina { get; set; }
    public int TamanioPagina { get; set; }

    public static RegisteredPaymentsPageResponseDTO FromEntity(PagosRegistradosPagina page)
    {
        return new RegisteredPaymentsPageResponseDTO
        {
            Items = page.Items.Select(RecentPaymentResponseDTO.FromEntity).ToList(),
            Total = page.Total,
            TotalSinFiltros = page.TotalSinFiltros,
            Pagina = page.Pagina,
            TamanioPagina = page.TamanioPagina
        };
    }
}
