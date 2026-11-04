namespace EntityLibrary
{
    // HU-069: cómo se interpreta el valor que escribe el SuperAdmin al programar una cuota mensual.
    //  - Absoluto:   el monto final.
    //  - Porcentaje: arancel vigente del destino + ese porcentaje, redondeado al peso.
    //  - MontoFijo:  arancel vigente del destino + ese monto.
    public enum TipoCambioArancel
    {
        Absoluto,
        Porcentaje,
        MontoFijo
    }

    // Lo que el SuperAdmin pidió cambiar: un destino (género O categoría), cómo y desde cuándo.
    public class CambioArancel
    {
        public string? Genero { get; set; }
        public int? IdCategoria { get; set; }
        public TipoCambioArancel Tipo { get; set; }
        public decimal Valor { get; set; }
        public DateTime VigenteDesde { get; set; }
    }

    // Jugadores que la cuota del mes del arancel nuevo alcanzaría, agrupados por categoría, género y monto actual.
    // MontoActual null: hoy no tienen ningún arancel para ese mes (no recibirían cuota sin el cambio).
    public class ImpactoArancelLinea
    {
        public int? IdCategoria { get; set; }
        public string? NombreCategoria { get; set; }
        public string Genero { get; set; } = string.Empty;
        public decimal? MontoActual { get; set; }
        public decimal MontoNuevo { get; set; }
        public int Jugadores { get; set; }

        public decimal DiferenciaPorJugador => MontoNuevo - (MontoActual ?? 0m);
        public decimal DiferenciaTotal => DiferenciaPorJugador * Jugadores;
    }

    // Jugadores del alcance que NO cambian porque su categoría tiene un arancel propio vigente (HU-048).
    public class ImpactoArancelExcluidos
    {
        public int? IdCategoria { get; set; }
        public string? NombreCategoria { get; set; }
        public int Jugadores { get; set; }
    }

    // Lo que leen la simulación y la confirmación (misma consulta): la cuota del mes del arancel nuevo, jugador
    // por jugador, agrupada.
    public class ImpactoArancelConsulta
    {
        public List<ImpactoArancelLinea> Lineas { get; set; } = new();
        public List<ImpactoArancelExcluidos> Excluidos { get; set; } = new();
    }

    // HU-106: impacto de un cambio de arancel ANTES de confirmarlo. Lo calcula el backend con los datos reales y la
    // misma lógica que después lo graba (ArancelesService.CalcularImpacto).
    public class ImpactoArancel
    {
        public TipoCambioArancel Tipo { get; set; }
        public decimal Valor { get; set; }
        public string? Genero { get; set; }
        public int? IdCategoria { get; set; }
        public string? NombreCategoria { get; set; }
        public DateTime VigenteDesde { get; set; }

        // Arancel vigente hoy del destino (la base de un porcentaje o monto fijo); null si nunca tuvo uno.
        public decimal? MontoBase { get; set; }
        public decimal MontoNuevo { get; set; }

        // Primer mes que cobra el arancel nuevo (el impacto mensual se calcula sobre ese mes) y, si ya hay otro
        // programado después para el mismo destino, desde cuándo lo reemplaza.
        public DateTime MesDeAplicacion { get; set; }
        public DateTime? ReemplazadoDesde { get; set; }

        public List<ImpactoArancelLinea> Lineas { get; set; } = new();
        public List<ImpactoArancelExcluidos> Excluidos { get; set; } = new();

        public int Jugadores => Lineas.Sum(l => l.Jugadores);
        public int Categorias => Lineas.Select(l => l.IdCategoria).Distinct().Count();
        public decimal DiferenciaMensual => Lineas.Sum(l => l.DiferenciaTotal);

        // Si el arancel rige desde el mes en curso: qué pasa con las cuotas de este mes que ya existen. Las que
        // tienen algún pago no se tocan; las pendientes sin pagos se vuelven a emitir con el monto nuevo (HU-049).
        // Las de meses anteriores no se tocan nunca.
        public bool MesEnCurso { get; set; }
        public int CuotasDelMesPendientesSinPagos { get; set; }
        public int CuotasDelMesConPagos { get; set; }
        // Si las cuotas con el monto nuevo todavía no se emiten: el día en que las genera el generador mensual.
        public DateTime? SeEmiteElDia { get; set; }

        // Más de una categoría: la confirmación exige escribir la cantidad de jugadores.
        public bool RequiereConfirmacionReforzada => Categorias > 1;

        // Resumen de todo lo anterior: la confirmación se acepta solo si, recalculado al grabar, sigue igual.
        public string Huella { get; set; } = string.Empty;
    }
}
