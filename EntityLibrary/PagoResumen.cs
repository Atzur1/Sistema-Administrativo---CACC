namespace EntityLibrary
{
    // Fila del panel "Pendientes de cobro": deuda agrupada por jugador (PAGOS.Estado = 0).
    public class PendienteJugador
    {
        public int IdJugador { get; set; }
        public string NombreCompleto { get; set; } = string.Empty;
        // Para que la búsqueda por documento y la columna DNI de la tabla no necesiten
        // un endpoint aparte (mismo criterio que IdCategoria más abajo).
        public string Dni { get; set; } = string.Empty;
        // HU-020: se expone el id (no solo el nombre) para poder armar el combo de categorías
        // del lado del cliente a partir de esta misma lista, sin necesitar un endpoint aparte.
        public int IdCategoria { get; set; }
        public string Categoria { get; set; } = string.Empty;
        public decimal MontoTotal { get; set; }
        public int CantidadCuotas { get; set; }
    }

    // Fila del panel "Deuda por categoría": deuda agrupada por categoría/división para un
    // período dado (un mes puntual, o el año completo si no se pidió un mes).
    public class CategoriaDeuda
    {
        public int IdCategoria { get; set; }
        public string Categoria { get; set; } = string.Empty;
        public decimal MontoTotal { get; set; }
        public int CantidadJugadores { get; set; }
    }

    // Fila del panel "Últimos pagos": PAGOS.Estado = 1, más recientes primero.
    public class PagoReciente
    {
        public int IdPago { get; set; }
        public int IdJugador { get; set; }
        public string NombreCompleto { get; set; } = string.Empty;
        public string MetodoPago { get; set; } = string.Empty;
        public decimal Monto { get; set; }
        public DateTime FechaPago { get; set; }
    }

    // Métricas del header de "Cuotas y Pagos".
    public class ResumenPagos
    {
        public decimal RecaudadoAnioActual { get; set; }
        public int PagosDelMes { get; set; }
        public int CantidadPendientes { get; set; }

        // HU-019 — indicador destacado "Deuda Global Total": suma de cada cuota pendiente
        // (PAGOS.estado = 0) por su monto_final YA CONGELADO al mes en que se emitió (no el
        // arancel vigente hoy — ver project.md §2.3), con el beneficio de Becados y Descuentos
        // aplicado si tiene uno (clampleado a 0). Misma fórmula que EstadisticasDao.DeudaAcumulada.
        public decimal DeudaGlobalTotal { get; set; }

        // Cantidad de jugadores ÚNICOS con al menos una cuota con saldo real > 0 (no la
        // cantidad de cuotas, que ya vive en CantidadPendientes).
        public int JugadoresMorosos { get; set; }
    }
}
