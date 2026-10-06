using System.ComponentModel.DataAnnotations;

namespace ApiGestion.Models;

public sealed class EditarCandidatoRequest
{
    [Required, MaxLength(100)]
    public string Nombre { get; init; } = string.Empty;

    [Required, MaxLength(100)]
    public string Apellido { get; init; } = string.Empty;

    // Con o sin puntos ("30.111.222"): el servidor lo guarda solo con dígitos.
    [Required, MaxLength(40)]
    public string Dni { get; init; } = string.Empty;

    [Required, EmailAddress, MaxLength(254)]
    public string Email { get; init; } = string.Empty;
}
