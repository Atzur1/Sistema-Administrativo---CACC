using System.ComponentModel.DataAnnotations;

namespace ApiGestion.Models
{
    public class AnularPagoRequestDto
    {
        [Required(ErrorMessage = "Indicá el motivo de la anulación.")]
        [StringLength(500, MinimumLength = 5, ErrorMessage = "El motivo debe tener entre 5 y 500 caracteres.")]
        public string Motivo { get; set; } = string.Empty;
    }
}
