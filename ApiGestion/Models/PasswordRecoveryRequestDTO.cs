using System.ComponentModel.DataAnnotations;

namespace ApiGestion.Models;

// "Olvidé mi contraseña": el DNI de la cuenta o, mientras dura la transición, su correo. El enlace siempre
// llega al correo que la cuenta tiene cargado, nunca a lo que se escriba acá.
public sealed class PasswordRecoveryRequestDTO
{
    [Required(ErrorMessage = "Indicá tu usuario o correo electrónico."), MaxLength(254, ErrorMessage = "El usuario no puede superar los 254 caracteres.")]
    public string Usuario { get; init; } = string.Empty;
}
