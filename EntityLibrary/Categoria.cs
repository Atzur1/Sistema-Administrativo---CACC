namespace EntityLibrary
{
    // Catálogo de categorías/divisiones deportivas del club. La tabla CATEGORIAS
    // no tiene una columna de baja/alta: las 13 filas son, por diseño, las
    // categorías activas (HU-020).
    public class Categoria
    {
        public int IdCategoria { get; set; }
        public string Nombre { get; set; } = string.Empty;
    }
}
