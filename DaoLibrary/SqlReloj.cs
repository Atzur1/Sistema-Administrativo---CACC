namespace DaoLibrary
{
    // Fecha y hora del club (Argentina) del lado de SQL Server, en reemplazo de GETDATE(), que devuelve la
    // hora local del servidor de base de datos (UTC en la mayoría de los hostings). Mismo criterio que
    // EntityLibrary.RelojNegocio del lado de la API. Requiere SQL Server 2016 o superior (AT TIME ZONE).
    internal static class SqlReloj
    {
        public const string Ahora = "CONVERT(DATETIME2, SYSDATETIMEOFFSET() AT TIME ZONE 'Argentina Standard Time')";

        public const string Hoy = "CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Argentina Standard Time' AS DATE)";
    }
}
