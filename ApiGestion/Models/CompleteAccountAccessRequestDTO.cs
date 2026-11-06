using System.ComponentModel.DataAnnotations;

namespace ApiGestion.Models;

public sealed class CompleteAccountAccessRequestDTO
{
    [Required(ErrorMessage = "El enlace no es válido."), MinLength(40, ErrorMessage = "El enlace no es válido."), MaxLength(200, ErrorMessage = "El enlace no es válido.")]
    public string Token { get; init; } = string.Empty;

    // HU-087: sin validaciones acá. Toda la política (largo, letras y números, datos personales, comunes, club) la juzga
    // PoliticaContrasenia: si el DTO cortara una contraseña corta antes, la respuesta no traería "problemas" y la pantalla
    // la tomaría por un enlace vencido. Una contraseña faltante también llega a la política (y se informa como corta).
    public string? Password { get; init; }
}
