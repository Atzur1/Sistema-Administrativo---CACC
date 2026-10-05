namespace EntityLibrary
{
    // HU-024: datos del jugador y filas crudas de sus cuotas, tal como las lee PagosDao.
    public class PlayerStatementAccount
    {
        public int PlayerId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Dni { get; set; } = string.Empty;
        public IReadOnlyList<CuotaMovimiento> Movements { get; set; } = new List<CuotaMovimiento>();
    }

    // HU-024: estado de cuenta del jugador. Fees en orden cronológico, con el estado de cada
    // cuota; TotalDebtAmount es lo que falta abonar de las Pendientes y Vencidas.
    public class PlayerStatement
    {
        public int PlayerId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Dni { get; set; } = string.Empty;
        public decimal TotalDebtAmount { get; set; }
        public IReadOnlyList<CuotaJugador> Fees { get; set; } = new List<CuotaJugador>();
    }
}
