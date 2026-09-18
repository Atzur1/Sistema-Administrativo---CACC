namespace ApiGestion.Models
{
    // Backea el form "Programar nuevo arancel" del frontend (categoría/género + monto + vigencia).
    public class ProgramarArancelRequestDto
    {
        public string Genero { get; set; } = string.Empty;
        public decimal Monto { get; set; }
        public DateTime VigenteDesde { get; set; }
    }
}
