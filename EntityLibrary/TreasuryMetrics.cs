namespace EntityLibrary;

public class TreasuryMetrics
{
    private decimal collectedThisYear = 0;
    private int paymentsThisMonth = 0;
    private int pendingCount = 0;

    public decimal CollectedThisYear
    {
        get { return collectedThisYear; }
        set { collectedThisYear = value; }
    }

    public int PaymentsThisMonth
    {
        get { return paymentsThisMonth; }
        set { paymentsThisMonth = value; }
    }

    public int PendingCount
    {
        get { return pendingCount; }
        set { pendingCount = value; }
    }
}
