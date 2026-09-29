using System.ComponentModel.DataAnnotations;

namespace ApiGestion.Models;

public sealed class EmailRequest
{
    [Required, EmailAddress, MaxLength(254)]
    public string Email { get; init; } = string.Empty;
}
