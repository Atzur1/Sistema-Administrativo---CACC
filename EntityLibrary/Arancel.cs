namespace EntityLibrary
{
    // Refleja la tabla ARANCELES: cada fila es un monto de cuota vigente desde una fecha para un
    // género. No hay columna "vigente_hasta" ni "estado" guardados: se calculan comparando
    // vigente_desde entre filas de un mismo género contra la fecha actual (evita que se
    // desactualicen si nadie corre un proceso que los cierre).
    public class Arancel
    {
        public int IdArancel { get; set; }
        public string Genero { get; set; } = string.Empty; // "Masculino" | "Femenino"
        public decimal Monto { get; set; }
        public DateTime VigenteDesde { get; set; }
    }

    // Fila de "Historial y aranceles programados", con vigencia y estado ya calculados.
    public class ArancelHistorialItem
    {
        public int IdArancel { get; set; }
        public string Genero { get; set; } = string.Empty;
        public decimal Monto { get; set; }
        public DateTime VigenteDesde { get; set; }
        public DateTime? VigenteHasta { get; set; } // null = todavía sigue vigente (no hay uno más nuevo después)
        public string Estado { get; set; } = string.Empty; // "Vigente" | "Programado" | "Anterior"
    }

    // Header de "Actualización de Aranceles": monto vigente de cada género + próximo cambio.
    public class ArancelResumen
    {
        public decimal? ArancelMasculinoVigente { get; set; }
        public decimal? ArancelFemeninoVigente { get; set; }
        public DateTime? ProximoCambioFecha { get; set; }
    }
}
