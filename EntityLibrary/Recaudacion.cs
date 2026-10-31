namespace EntityLibrary;

// HU-018: lo recaudado en un período, ya separado por medio de pago.
// Los dos subtotales siempre suman el total: la base agrupa por metodo_pago
// sobre el mismo conjunto de filas, así que no pueden desalinearse.
public class RecaudacionPeriodo
{
    private decimal total = 0;
    private decimal efectivo = 0;
    private decimal transferencia = 0;
    private int pagos = 0;
    private int pagosEfectivo = 0;
    private int pagosTransferencia = 0;
    private List<PagoDelPeriodo> detalle = new List<PagoDelPeriodo>();

    public decimal Total
    {
        get { return total; }
        set { total = value; }
    }

    public decimal Efectivo
    {
        get { return efectivo; }
        set { efectivo = value; }
    }

    public decimal Transferencia
    {
        get { return transferencia; }
        set { transferencia = value; }
    }

    // Cantidad de cobros del período, para el contador de transacciones
    public int Pagos
    {
        get { return pagos; }
        set { pagos = value; }
    }

    public int PagosEfectivo
    {
        get { return pagosEfectivo; }
        set { pagosEfectivo = value; }
    }

    public int PagosTransferencia
    {
        get { return pagosTransferencia; }
        set { pagosTransferencia = value; }
    }

    public List<PagoDelPeriodo> Detalle
    {
        get { return detalle; }
        set { detalle = value; }
    }
}

// Una fila de la tabla "Pagos del período"
public class PagoDelPeriodo
{
    private long idPago = 0;
    private string jugador = "";
    private string dni = "";
    private string categoria = "";
    private decimal monto = 0;
    private string medioPago = "";
    private DateTime fechaPago;
    private DateTime? fechaHoraRegistro;

    public long IdPago
    {
        get { return idPago; }
        set { idPago = value; }
    }

    // "APELLIDO, Nombre", igual que el resto de los listados del portal
    public string Jugador
    {
        get { return jugador; }
        set { jugador = value; }
    }

    public string Dni
    {
        get { return dni; }
        set { dni = value; }
    }

    public string Categoria
    {
        get { return categoria; }
        set { categoria = value; }
    }

    public decimal Monto
    {
        get { return monto; }
        set { monto = value; }
    }

    public string MedioPago
    {
        get { return medioPago; }
        set { medioPago = value; }
    }

    // HU-107: día del cobro (PAGOS.fecha_pago, el mismo que define a qué mes pertenece).
    public DateTime FechaPago
    {
        get { return fechaPago; }
        set { fechaPago = value; }
    }

    // HU-107: momento real en que se registró el cobro (PAGOS.fecha_hora_registro), ya en hora del
    // club: la base lo graba con SqlReloj.Ahora. Null en los pagos anteriores a esa columna.
    public DateTime? FechaHoraRegistro
    {
        get { return fechaHoraRegistro; }
        set { fechaHoraRegistro = value; }
    }
}

// HU-107: por qué se puede ordenar la tabla "Pagos del período". Un enum y no el texto que llega
// por la URL: el ORDER BY sale de una lista cerrada y nunca del nombre que manda el cliente.
public enum OrdenPagosDelPeriodo
{
    Fecha,
    Monto
}

// HU-107: una página de "Pagos del período", filtrada, ordenada y paginada en la base.
public class FiltroPagosDelPeriodo
{
    public int Anio { get; set; }
    public int Mes { get; set; }
    public int? IdCategoria { get; set; }
    public int Pagina { get; set; } = 1;
    public int TamanioPagina { get; set; } = 10;
    public OrdenPagosDelPeriodo OrdenarPor { get; set; } = OrdenPagosDelPeriodo.Fecha;
    public bool Descendente { get; set; } = true;
}

public class PagosDelPeriodoPagina
{
    public List<PagoDelPeriodo> Items { get; set; } = new();

    // Cobros de TODO el período (con el filtro de categoría), no los de la página.
    public int Total { get; set; }
    public int Pagina { get; set; }
    public int TamanioPagina { get; set; }

    // Un período sin cobros sigue teniendo una página (vacía), igual que el paginador de HU-088.
    public int TotalPaginas => Math.Max(1, (int)Math.Ceiling(Total / (double)TamanioPagina));
}
