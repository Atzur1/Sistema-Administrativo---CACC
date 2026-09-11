namespace DaoLibrary.Exceptions
{
    // Error de validación de negocio (400) al programar un arancel: monto inválido, género
    // inválido, fecha en el pasado, etc.
    public class ArancelInvalidoException : Exception
    {
        public ArancelInvalidoException(string mensaje) : base(mensaje) { }
    }
}
