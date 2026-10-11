using EntityLibrary;

namespace ApiGestion.Models;

public class CategoryResponseDTO
{
    public int IdCategoria { get; set; }
    public string Nombre { get; set; } = "";

    public static CategoryResponseDTO FromEntity(Categoria category)
    {
        return new CategoryResponseDTO
        {
            IdCategoria = category.IdCategoria,
            Nombre = category.Nombre
        };
    }
}
