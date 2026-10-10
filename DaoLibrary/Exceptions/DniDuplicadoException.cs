namespace DaoLibrary.Exceptions
{
    // Otra alta con el mismo DNI se grabó entre la verificación previa y la transacción del alta (400).
    public class DniDuplicadoException : Exception
    {
        public DniDuplicadoException(string dni) : base($"Ya hay una persona registrada con el DNI {dni}.") { }
    }
}
