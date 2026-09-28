namespace EntityLibrary;

// One payment, full or partial, made against a player's enrollment fee (HU-033)
public class EnrollmentPayment
{
    private long id = 0;
    private decimal amount = 0;
    private string paymentMethod = "";
    private DateTime paymentDate;

    public long Id
    {
        get { return id; }
        set { id = value; }
    }

    public decimal Amount
    {
        get { return amount; }
        set { amount = value; }
    }

    public string PaymentMethod
    {
        get { return paymentMethod; }
        set { paymentMethod = value; }
    }

    public DateTime PaymentDate
    {
        get { return paymentDate; }
        set { paymentDate = value; }
    }
}
