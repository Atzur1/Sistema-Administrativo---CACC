namespace EntityLibrary;

public class Discount
{
    private long id = 0;
    private long playerId = 0;
    private string playerName = "";
    private string category = "";
    private string type = "";
    private long typeId = 0;
    private string valueType = "";
    private decimal? percentage = null;
    private decimal? fixedAmount = null;
    private DateTime startDate;
    private DateTime endDate;
    private DiscountStatus status = DiscountStatus.Scheduled;
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

    // Id of the row in TIPO_DESCUENTO. Since HU-011 that table is a closed
    // catalogue of reasons, so this is what ties an assignment to its reason.
    public long TypeId
    {
        get { return typeId; }
        set { typeId = value; }
    }

    // '%' or '$'. Decides which of the two values below carries the benefit.
    public string ValueType
    {
        get { return valueType; }
        set { valueType = value; }
    }

    // Percentage and FixedAmount are mutually exclusive: the one that does not
    // apply stays null, the same way the table constraint demands it.
    public decimal? Percentage
    {
        get { return percentage; }
        set { percentage = value; }
    }

    public decimal? FixedAmount
    {
        get { return fixedAmount; }
        set { fixedAmount = value; }
    }

    public DateTime StartDate
    {
        get { return startDate; }
        set { startDate = value; }
    }

    // Since HU-012 the end date is mandatory: every benefit runs for an
    // authorised period and stops on its own when that period ends.
    public DateTime EndDate
    {
        get { return endDate; }
        set { endDate = value; }
    }

    // Resolved from the range against the server date, never stored. This is the
    // single source of truth about where the benefit stands: IsActive below is
    // derived from it, so the two can never disagree.
    public DiscountStatus Status
    {
        get { return status; }
        set
        {
            status = value;
            isActive = value == DiscountStatus.Active;
        }
    }

    // Kept for the consumers HU-014 left behind, the badge among them. It has no
    // setter on purpose: it reads Status and nothing else can move it, so the
    // "three independent flags that drift apart" problem cannot happen here.
    public bool IsActive
    {
        get { return isActive; }
    }
}
