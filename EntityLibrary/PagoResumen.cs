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

    // Fila del panel "Últimos pagos": PAGOS.Estado = 1, más recientes primero. Incluye tanto
    // cuotas ya completadas (Estado = "Pagado") como cuotas que siguen con un saldo pendiente
    // pero ya recibieron algún abono (Estado = "Parcial") — ver PagosDao.ObtenerUltimosPagos.
    public class PagoReciente
    {
        public int IdPago { get; set; }
        public int IdJugador { get; set; }
        public string NombreCompleto { get; set; } = string.Empty;
        public string Categoria { get; set; } = string.Empty;
        public string Periodo { get; set; } = string.Empty;
        public string MetodoPago { get; set; } = string.Empty;
        public decimal Monto { get; set; }
        public string Estado { get; set; } = string.Empty; // "Pagado" | "Parcial"
        public DateTime FechaPago { get; set; }
        // "Cuota" o "Inscripcion" — para que el frontend pueda etiquetarlo
        public string Concepto { get; set; } = "Cuota";
        // Momento exacto en que se grabó (columna agregada después): null para pagos
        // registrados antes de esa migración, que solo tienen el día en FechaPago.
        public DateTime? FechaHoraRegistro { get; set; }
        public string? ResponsableNombre { get; set; }
        public string? ResponsableApellido { get; set; }
    }

    // Métricas del banner de "Actividad y Movimientos": a diferencia de ResumenPagos (año/mes),
    // esto es puntual del día de hoy.
    public class ResumenPagosHoy
    {
        public int PagosHoy { get; set; }
        public decimal RecaudadoHoy { get; set; }
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
