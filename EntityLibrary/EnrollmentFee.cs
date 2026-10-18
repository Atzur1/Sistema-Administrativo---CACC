namespace EntityLibrary;

// One row of ARANCELES_INSCRIPCION: the one-time enrollment fee charged to new players of one gender
// (Gender), in force from StartDate until the next fee of that same gender starts. A gender with no
// fee in force, or with a fee of 0, is not charged an enrollment.
public class EnrollmentFee
{
    private long id = 0;
    private decimal amount = 0;
    private DateTime startDate;
    private DateTime? endDate;
    private EnrollmentFeeStatus status = EnrollmentFeeStatus.Scheduled;

    public long Id
    {
        get { return id; }
        set { id = value; }
    }

    // Masculino or Femenino. Each gender has its own history of fees.
    public string Gender { get; set; } = Generos.Masculino;

    public decimal Amount
    {
        get { return amount; }
        set { amount = value; }
    }

    public DateTime StartDate
    {
        get { return startDate; }
        set { startDate = value; }
    }

    // Null while no later fee has been scheduled
    public DateTime? EndDate
    {
        get { return endDate; }
        set { endDate = value; }
    }

    public EnrollmentFeeStatus Status
    {
        get { return status; }
        set { status = value; }
    }

    public string? ResponsibleName { get; set; }
    public string? ResponsibleSurname { get; set; }
    public DateTime? RegisteredAtUtc { get; set; }
}
