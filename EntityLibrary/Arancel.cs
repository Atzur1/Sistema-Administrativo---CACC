namespace EntityLibrary
{
    // Refleja la tabla ARANCELES: cada fila es un monto de cuota vigente desde una fecha para un
    // género O para una categoría (una u otra, nunca ambas). A cada jugador le corresponde el
    // arancel de su categoría si tiene uno vigente; si no, el de su género. No hay columna
    // "vigente_hasta" ni "estado" guardados: se calculan comparando vigente_desde entre filas
    // del mismo destino (mismo género o misma categoría) contra la fecha actual (evita que se
    // desactualicen si nadie corre un proceso que los cierre).
    public class Arancel
    {
        public int IdArancel { get; set; }
        public string? Genero { get; set; } // "Masculino" | "Femenino"; null si el arancel es por categoría
        public int? IdCategoria { get; set; } // null si el arancel es por género
        public string? NombreCategoria { get; set; }
        public decimal Monto { get; set; }
        public DateTime VigenteDesde { get; set; }
        public string? ResponsableNombre { get; set; }
        public string? ResponsableApellido { get; set; }
    }

    // Fila de "Historial y aranceles programados", con vigencia y estado ya calculados.
    public class ArancelHistorialItem
    {
        public int IdArancel { get; set; }
        public string? Genero { get; set; }
        public int? IdCategoria { get; set; }
        public string? NombreCategoria { get; set; }
        public decimal Monto { get; set; }
        public DateTime VigenteDesde { get; set; }
        public DateTime? VigenteHasta { get; set; } // null = todavía sigue vigente (no hay uno más nuevo después)
        public string Estado { get; set; } = string.Empty; // "Vigente" | "Programado" | "Anterior"
        // Solo se pueden cancelar los aranceles del mes en curso en adelante; los de meses anteriores
        // son historial (sus cuotas ya se emitieron en períodos cerrados).
        public bool PuedeCancelar { get; set; }
        public string? ResponsableNombre { get; set; }
        public string? ResponsableApellido { get; set; }
    }

    // Header de "Actualización de Aranceles": monto vigente de cada género + próximo cambio.
    public class ArancelResumen
    {
        public decimal? ArancelMasculinoVigente { get; set; }
        public decimal? ArancelFemeninoVigente { get; set; }
        public DateTime? ProximoCambioFecha { get; set; }
    }
}
