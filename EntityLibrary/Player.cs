namespace EntityLibrary;

// A player as registered on the club: the PERSONA row plus the JUGADORES row (HU-033)
public class Player
{
    private long id = 0;
    private string firstName = "";
    private string lastName = "";
    private string dni = "";
    private DateTime birthDate;
    private string gender = "";
    private long categoryId = 0;
    private DateTime joinDate;

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

    public string Dni
    {
        get { return dni; }
        set { dni = value; }
    }

    public DateTime BirthDate
    {
        get { return birthDate; }
        set { birthDate = value; }
    }

    // "Masculino" | "Femenino", the values PERSONA.genero and ARANCELES already use
    public string Gender
    {
        get { return gender; }
        set { gender = value; }
    }

    public long CategoryId
    {
        get { return categoryId; }
        set { categoryId = value; }
    }

    public DateTime JoinDate
    {
        get { return joinDate; }
        set { joinDate = value; }
    }
}
