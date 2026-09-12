namespace EntityLibrary;

public class Discount
{
    private long id = 0;
    private long playerId = 0;
    private string playerName = "";
    private string category = "";
    private string type = "";
    private int percentage = 0;
    private DateTime startDate;
    private DateTime endDate;
    private bool isActive = false;

    public long Id
    {
        get { return id; }
        set { id = value; }
    }

    public long PlayerId
    {
        get { return playerId; }
        set { playerId = value; }
    }

    public string PlayerName
    {
        get { return playerName; }
        set { playerName = value; }
    }

    public string Category
    {
        get { return category; }
        set { category = value; }
    }

    public string Type
    {
        get { return type; }
        set { type = value; }
    }

    public int Percentage
    {
        get { return percentage; }
        set { percentage = value; }
    }

    public DateTime StartDate
    {
        get { return startDate; }
        set { startDate = value; }
    }

    public DateTime EndDate
    {
        get { return endDate; }
        set { endDate = value; }
    }

    public bool IsActive
    {
        get { return isActive; }
        set { isActive = value; }
    }
}
