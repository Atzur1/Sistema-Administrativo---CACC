namespace EntityLibrary;

// One row of the payment roster (HU-029): a player together with what he or she still owes.
// The amount follows the same rule as the "Deuda Global Total" indicator (HU-019): every pending
// installment, with the active Becados y Descuentos benefit already applied.
public class PlayerAccount
{
    private int playerId = 0;
    private string firstName = "";
    private string lastName = "";
    private string dni = "";
    private string category = "";
    private decimal amountOwed = 0;
    private int pendingInstallments = 0;

    public int PlayerId
    {
        get { return playerId; }
        set { playerId = value; }
    }

    public string FirstName
    {
        get { return firstName; }
        set { firstName = value; }
    }

    public string LastName
    {
        get { return lastName; }
        set { lastName = value; }
    }

    public string Dni
    {
        get { return dni; }
        set { dni = value; }
    }

    public string Category
    {
        get { return category; }
        set { category = value; }
    }

    // 0 when the player is up to date
    public decimal AmountOwed
    {
        get { return amountOwed; }
        set { amountOwed = value; }
    }

    // Installments that still have a real balance, not the ones a benefit already covered
    public int PendingInstallments
    {
        get { return pendingInstallments; }
        set { pendingInstallments = value; }
    }
}
