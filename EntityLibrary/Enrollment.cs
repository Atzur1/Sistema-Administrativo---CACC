namespace EntityLibrary;

// The enrollment fee charged to one player when joining the men's squad (HU-033),
// with what was already paid and what is still owed.
public class Enrollment
{
    private long playerId = 0;
    private decimal amount = 0;
    private decimal pendingBalance = 0;
    private DateTime dueDate;
    private List<EnrollmentPayment> payments = new List<EnrollmentPayment>();

    public long PlayerId
    {
        get { return playerId; }
        set { playerId = value; }
    }

    // The fee in force the day the player joined, frozen from then on
    public decimal Amount
    {
        get { return amount; }
        set { amount = value; }
    }

    public decimal PendingBalance
    {
        get { return pendingBalance; }
        set { pendingBalance = value; }
    }

    // The day the player joined, which is when the fee was charged
    public DateTime DueDate
    {
        get { return dueDate; }
        set { dueDate = value; }
    }

    public List<EnrollmentPayment> Payments
    {
        get { return payments; }
        set { payments = value; }
    }

    public decimal PaidAmount
    {
        get { return payments.Sum(p => p.Amount); }
    }
}
