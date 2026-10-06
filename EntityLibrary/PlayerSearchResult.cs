namespace EntityLibrary;

// One match of the player search on the Cuotas panel (HU-023)
public class PlayerSearchResult
{
    public long Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Dni { get; set; } = "";
    public string CategoryName { get; set; } = "";
}
