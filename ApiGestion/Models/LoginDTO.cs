namespace ApiGestion.Models;

using System.ComponentModel.DataAnnotations;

public class LoginDTO
{
    // The account's DNI or, during the transition, its email. The format is not checked here: a value that
    // is neither simply matches no account.
    [Required(ErrorMessage = "Indicá el usuario.")]
    [MaxLength(254, ErrorMessage = "El usuario no puede superar los 254 caracteres.")]
    public string Username { get; set; } = "";

    [Required(ErrorMessage = "Indicá la contraseña.")]
    [MinLength(1, ErrorMessage = "Indicá la contraseña.")]
    [MaxLength(256, ErrorMessage = "La contraseña no puede superar los 256 caracteres.")]
    public string Password { get; set; } = "";
}
