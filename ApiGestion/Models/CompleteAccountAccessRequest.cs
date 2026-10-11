using System.ComponentModel.DataAnnotations;

namespace ApiGestion.Models;

public sealed class CompleteAccountAccessRequest
{
    [Required, MinLength(40), MaxLength(200)]
    public string Token { get; init; } = string.Empty;

    // El resto de la política (letras y números, sin datos personales, no común) la controla PoliticaContrasenia.
    [Required, MinLength(10), MaxLength(128)]
    public string Password { get; init; } = string.Empty;
}
