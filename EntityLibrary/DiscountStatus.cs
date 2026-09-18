namespace EntityLibrary;

// Where a benefit stands relative to today (HU-012).
//
// It is not stored: it is resolved from the validity range every time the
// benefit is read, so a benefit expires on its own the day it has to and nobody
// needs to flip a flag. Storing it would mean a daily job, and a column that
// silently drifts out of sync with the dates next to it.
//
// The server decides it, never the browser: the date used is the one the
// database reports, so two administrators in different time zones read the same
// state for the same benefit.
// Cancelled is not part of HU-012, which describes three states, but the model
// already had one: a benefit taken down by hand keeps its row, because PAGOS
// point at it. Leaving it out would have meant exposing estado_activo next to
// this enum, and two fields the client has to combine are exactly the drifting
// flags the story asks to avoid. So it is one field with four values, and
// cancelling wins over the dates: a benefit taken down in June is not "active"
// in July just because its range says so.
public enum DiscountStatus
{
    // The range starts in the future
    Scheduled = 0,

    // Today falls inside the range
    Active = 1,

    // The range already ended
    Expired = 2,

    // Taken down by an administrator, whatever the range says
    Cancelled = 3
}
