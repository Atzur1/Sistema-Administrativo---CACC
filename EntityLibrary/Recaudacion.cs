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
}
