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

        // El día del club (Argentina) en que ocurrió un instante guardado en UTC.
        public static DateTime FechaDelClub(DateTime utc)
            => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), ZonaClub).Date;

        // Identificador de la zona del club, para dejarlo escrito en las exportaciones.
        public static string Zona => ZonaClub.Id;

        // Una hora que ya está en hora del club (como PAGOS.fecha_hora_registro, que la base graba con
        // SqlReloj.Ahora) con su corrimiento explícito: viaja como "2026-10-31T14:35:00-03:00" y nadie
        // tiene que adivinar en qué zona está. El corrimiento sale de la zona, no de un "-3" fijo.
        public static DateTimeOffset EnHoraDelClub(DateTime horaDelClub)
        {
            var local = DateTime.SpecifyKind(horaDelClub, DateTimeKind.Unspecified);
            return new DateTimeOffset(local, ZonaClub.GetUtcOffset(local));
        }

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
            throw new TimeZoneNotFoundException("No se encontró la zona horaria del club: America/Argentina/Buenos_Aires o Argentina Standard Time.");
        }
    }
}
