using System.ComponentModel.DataAnnotations;

namespace ApiGestion.Models;

public sealed class EditarCandidatoRequest
{
    [Required, MaxLength(100)]
    public string Nombre { get; init; } = string.Empty;

    [Required, MaxLength(100)]
    public string Apellido { get; init; } = string.Empty;

    [Required, EmailAddress, MaxLength(254)]
    public string Email { get; init; } = string.Empty;
}
