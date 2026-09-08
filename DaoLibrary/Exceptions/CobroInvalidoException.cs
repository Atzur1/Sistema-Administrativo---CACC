namespace DaoLibrary.Exceptions
{
    // Error de validación de negocio (400): cuotas inexistentes, ya abonadas, método inválido, etc.
    // Se distingue de una excepción no controlada (500) para que el controller pueda mapear el código HTTP correcto.
    public class CobroInvalidoException : Exception
    {
        public CobroInvalidoException(string mensaje) : base(mensaje) { }
    }
}
