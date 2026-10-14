namespace ApiGestion.Models;

// What a successful login hands back. Never carries the password hash or the token version.
public class LoginResponseDTO
{
    public string Message { get; set; } = "";
    public string Email { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Dni { get; set; } = "";
    public int Role { get; set; }
    public string Token { get; set; } = "";
}
