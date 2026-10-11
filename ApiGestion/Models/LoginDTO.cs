namespace ApiGestion.Models;

using System.ComponentModel.DataAnnotations;

public class LoginDTO
{
    // The account's DNI or, during the transition, its email. The format is not checked here: a value that
    // is neither simply matches no account.
    [Required]
    [MaxLength(254)]
    public string Username { get; set; } = "";

    [Required]
    [MinLength(1)]
    [MaxLength(256)]
    public string Password { get; set; } = "";
}
