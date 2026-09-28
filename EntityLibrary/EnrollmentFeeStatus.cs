namespace EntityLibrary;

// Where an enrollment fee stands relative to today (HU-033). Not stored: it is
// resolved from the start date of the fee and of the next one every time it is read.
public enum EnrollmentFeeStatus
{
    Scheduled = 0,
    Current = 1,
    Previous = 2
}
