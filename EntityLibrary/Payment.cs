namespace EntityLibrary;

public class Payment
{
    private long id = 0;
    private long playerId = 0;
    private string playerName = "";
    private string category = "";
    private decimal baseAmount = 0;
    private decimal finalAmount = 0;
    private DateTime? paymentDate;
    private DateTime? dueDate;
    private string method = "";
    private bool isPaid = false;

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

    public decimal BaseAmount
    {
        get { return baseAmount; }
        set { baseAmount = value; }
    }

    public decimal FinalAmount
    {
        get { return finalAmount; }
        set { finalAmount = value; }
    }

    public DateTime? PaymentDate
    {
        get { return paymentDate; }
        set { paymentDate = value; }
    }

    public DateTime? DueDate
    {
        get { return dueDate; }
        set { dueDate = value; }
    }

    public string Method
    {
        get { return method; }
        set { method = value; }
    }

    public bool IsPaid
    {
        get { return isPaid; }
        set { isPaid = value; }
    }
}
