namespace ApiGestion.Models;

// Outbound shape of one match of GET api/players/search (HU-023)
public class PlayerSearchResultDto
{
    public long Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Dni { get; set; } = "";
    public string CategoryName { get; set; } = "";
}
