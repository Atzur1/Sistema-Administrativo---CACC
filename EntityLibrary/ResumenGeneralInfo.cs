namespace EntityLibrary
{
    public class PuntoRecaudacionMensual
    {
        public string Mes { get; set; } = string.Empty; // "Ene", "Feb"...
        public int Anio { get; set; }
        public decimal Monto { get; set; }
    }

    public class PuntoCoberturaMensual
    {
        public string Mes { get; set; } = string.Empty;
        public int Anio { get; set; }
        public double Porcentaje { get; set; } // % de jugadores con >=1 pago ese mes
    }

    // Todo lo que necesita el dashboard "Resumen General" en una sola llamada.
    public class ResumenGeneralInfo
    {
        public int TotalJugadores { get; set; }
        public int CantidadCategorias { get; set; }

        public int PagosDelMes { get; set; }
        public decimal RecaudadoAnioActual { get; set; }

        public decimal IngresadoEsteMes { get; set; }
        public decimal IngresadoMesAnterior { get; set; }

        public decimal DeudaAcumulada { get; set; }

        // Jugadores agrupados por cantidad de cuotas impagas (PAGOS.estado = 0)
        public int JugadoresSinDeuda { get; set; }
        public int JugadoresConUnaImpaga { get; set; }
        public int JugadoresConDosOMasImpagas { get; set; }

        public List<PuntoRecaudacionMensual> RecaudacionMensual { get; set; } = new();
        public List<PuntoCoberturaMensual> CoberturaPagoMensual { get; set; } = new();
        public List<PendienteJugador> MayorDeudaPendiente { get; set; } = new();
    }
}
