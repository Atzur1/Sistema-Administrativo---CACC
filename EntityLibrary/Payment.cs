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
    private DateTime? period;
    private string method = "";
    private bool isPaid = false;
    private string? reference;
    private long? registeredByUserId;
    private DateTime? registeredAt;

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

    // First day of the month the fee covers. Null for historical payments.
    public DateTime? Period
    {
        get { return period; }
        set { period = value; }
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

    public string? Reference
    {
        get { return reference; }
        set { reference = value; }
    }

    // Null for historical payments: that data was not recorded before HU-015
    public long? RegisteredByUserId
    {
        get { return registeredByUserId; }
        set { registeredByUserId = value; }
    }

    public DateTime? RegisteredAt
    {
        get { return registeredAt; }
        set { registeredAt = value; }
    }
}
