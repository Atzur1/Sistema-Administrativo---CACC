namespace EntityLibrary;

public class Tariff
{
    private long id = 0;
    private string branch = "";
    private decimal amount = 0;
    private DateTime validFrom;
    private DateTime? validTo;

    public long Id
    {
        get { return id; }
        set { id = value; }
    }

    // "M" (Masculino) or "F" (Femenino). Each branch keeps its own independent
    // tariff timeline: a change in one branch never reads or writes the other.
    public string Branch
    {
        get { return branch; }
        set { branch = value; }
    }

    public decimal Amount
    {
        get { return amount; }
        set { amount = value; }
    }

    public DateTime ValidFrom
    {
        get { return validFrom; }
        set { validFrom = value; }
    }

    // Null means open-ended: this is the branch's current tariff until a new
    // one is scheduled after it.
    public DateTime? ValidTo
    {
        get { return validTo; }
        set { validTo = value; }
    }

    // True only for the branch's open-ended row, the one a unique database
    // index guarantees is the sole such row per branch.
    public bool IsActive
    {
        get { return validTo == null; }
    }
}
