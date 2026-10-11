using EntityLibrary;

namespace ApiGestion.Models;

// Row of "Deuda por categoría" for a month or a whole year.
public class CategoryDebtResponseDTO
{
    public int IdCategoria { get; set; }
    public string Categoria { get; set; } = "";
    public decimal MontoTotal { get; set; }
    public int CantidadJugadores { get; set; }

    public static CategoryDebtResponseDTO FromEntity(CategoriaDeuda debt)
    {
        return new CategoryDebtResponseDTO
        {
            IdCategoria = debt.IdCategoria,
            Categoria = debt.Categoria,
            MontoTotal = debt.MontoTotal,
            CantidadJugadores = debt.CantidadJugadores
        };
    }
}
