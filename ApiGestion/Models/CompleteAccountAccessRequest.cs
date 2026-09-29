using System.ComponentModel.DataAnnotations;

namespace ApiGestion.Models;

public sealed class CompleteAccountAccessRequest
{
    [Required, MinLength(40), MaxLength(200)]
    public string Token { get; init; } = string.Empty;

    [Required, MinLength(8), MaxLength(128)]
    public string Password { get; init; } = string.Empty;
}
