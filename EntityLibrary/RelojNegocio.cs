namespace EntityLibrary
{
    // Fecha y hora del club (Argentina), sin importar la zona horaria del servidor donde corra la API.
    // En un hosting en la nube el servidor suele estar en UTC: con DateTime.Now, un cobro hecho a las
    // 22:00 quedaba fechado al día siguiente y una cuota pasaba a "Vencida" tres horas antes.
    // Para la base de datos, el equivalente es DaoLibrary.SqlReloj.
    public static class RelojNegocio
    {
        private static readonly TimeZoneInfo ZonaClub = ResolverZona();

        public static DateTime Ahora => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ZonaClub);

        public static DateTime Hoy => Ahora.Date;

        // .NET 6+ acepta el id IANA también en Windows; el id de Windows queda de respaldo por si el
        // sistema no tiene los datos de ICU. Argentina no tiene horario de verano desde 2009.
        private static TimeZoneInfo ResolverZona()
        {
            foreach (var id in new[] { "America/Argentina/Buenos_Aires", "Argentina Standard Time" })
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
            return TimeZoneInfo.CreateCustomTimeZone("ART", TimeSpan.FromHours(-3), "Argentina", "Argentina");
        }
    }
}
