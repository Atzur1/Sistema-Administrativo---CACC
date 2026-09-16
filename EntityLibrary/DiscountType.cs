namespace EntityLibrary;

// A row of the TIPO_DESCUENTO catalogue: the reason a benefit is granted for.
// Since HU-011 the catalogue is closed and holds no value of its own, so the
// amount and the validity of each grant live with the assignment instead.
public class DiscountType
{
    private long id = 0;
    private string name = "";

    public long Id
    {
        get { return id; }
        set { id = value; }
    }

    public string Name
    {
        get { return name; }
        set { name = value; }
    }
}
