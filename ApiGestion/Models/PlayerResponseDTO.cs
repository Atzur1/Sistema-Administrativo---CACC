namespace ApiGestion.Models;

// Outbound contract for the player lookup. Personal data beyond what the search
// needs (birth date, photo, QR token) stays out of the response.
public class PlayerResponseDTO
{
    public long Id { get; set; }
    public string FullName { get; set; } = "";
    public string Document { get; set; } = "";
    public string Category { get; set; } = "";
}
