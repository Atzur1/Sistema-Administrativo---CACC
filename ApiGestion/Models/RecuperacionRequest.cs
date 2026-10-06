using System.ComponentModel.DataAnnotations;

namespace ApiGestion.Models;

// "Olvidé mi contraseña": el DNI de la cuenta o, mientras dura la transición, su correo. El enlace siempre
// llega al correo que la cuenta tiene cargado, nunca a lo que se escriba acá.
public sealed class RecuperacionRequest
{
    [Required, MaxLength(254)]
    public string Usuario { get; init; } = string.Empty;
}
