namespace DaoLibrary.Exceptions
{
    // Pedido inválido sobre cuotas faltantes (400): un mes fuera de los que se revisan.
    public class CuotasFaltantesInvalidasException : Exception
    {
        public CuotasFaltantesInvalidasException(string mensaje) : base(mensaje) { }
    }
}
