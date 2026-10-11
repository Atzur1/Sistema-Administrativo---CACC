namespace EntityLibrary
{
    // Los dos géneros del padrón (PERSONA.genero). El arancel por género y la inscripción (solo rama masculina)
    // dependen de estos valores exactos.
    public static class Generos
    {
        public const string Masculino = "Masculino";
        public const string Femenino = "Femenino";

        public static readonly IReadOnlyList<string> Validos = new[] { Masculino, Femenino };
    }
}
