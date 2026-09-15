namespace EntityLibrary;

public class MonthlyFeeGenerationResult
{
    private int totalGenerated = 0;
    private int totalSkipped = 0;

    public int TotalGenerated
    {
        get { return totalGenerated; }
        set { totalGenerated = value; }
    }

    public int TotalSkipped
    {
        get { return totalSkipped; }
        set { totalSkipped = value; }
    }
}
