namespace EntityLibrary;

public class Player
{
    private long id = 0;
    private string firstName = "";
    private string lastName = "";
    private string document = "";
    private string category = "";

    public long Id
    {
        get { return id; }
        set { id = value; }
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

    public string Document
    {
        get { return document; }
        set { document = value; }
    }

    public string Category
    {
        get { return category; }
        set { category = value; }
    }
}
