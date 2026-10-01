namespace ApiGestion.Models
{
    // Backea el form "Programar nuevo arancel" del frontend: género O categoría (uno solo) + monto + vigencia.
    public class ProgramarArancelRequestDto
    {
        public string? Genero { get; set; }
        public int? IdCategoria { get; set; }
        public decimal Monto { get; set; }
        public DateTime VigenteDesde { get; set; }
    }
}
