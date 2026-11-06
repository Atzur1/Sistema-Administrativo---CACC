namespace EntityLibrary
{
    // Datos institucionales del club, en un solo lugar. HU-087 los usa para que una contraseña nueva no contenga el nombre
    // del club. NombreOficial es el que figura en los reportes (PdfReportBuilder y los ReportBuilder lo tienen copiado:
    // pendiente de unificar fuera de HU-087).
    public static class Club
    {
        public const string NombreOficial = "Club Atlético Camioneros de Córdoba";

        // Cómo se lo nombra en el día a día y su sigla (la del portal: "Portal Administrativo CACC").
        public const string Nombre = "Camioneros";
        public const string Sigla = "CACC";
    }
}
