using Microsoft.Data.SqlClient;

namespace DaoLibrary
{
    // Operaciones sobre las cuotas (concepto 'Cuota' de PAGOS) que necesita el servicio de aranceles
    // para programar o cancelar un arancel dentro de una transacción. Es una interfaz chica a propósito:
    // ArancelesService no depende de todo IPagosDao, y se prueba con un fake mínimo.
    //
    // Una cuota es "con pagos" si para ese jugador y período existe alguna fila con estado = 1
    // (abonada, parcial o total). Una cuota "pendiente sin pagos" es la fila con estado = 0 de un
    // jugador y período que no tiene ninguna fila abonada.
    //
    // genero / idCategoria acotan a qué jugadores alcanza (uno de los dos; ambos null = todos).
    // desde / hasta acotan los períodos por fecha_vencimiento (hasta es exclusivo; null = sin límite).
    // montoBase, si se informa, se queda solo con las cuotas emitidas por ese monto: así se distinguen
    // las cuotas de un arancel de las de otro del mismo destino.
    public interface ICuotasPorArancelDao
    {
        // Genera la cuota del mes para quien todavía no la tiene (ver IPagosDao). No pisa las existentes.
        void GenerarCuotasPendientesDelMes(SqlConnection conexion, SqlTransaction transaccion, string? genero, int? idCategoria, int mes, int anio);

        // Cantidad de cuotas (jugador + período) que ya tienen algún pago registrado.
        int ContarCuotasConPagos(SqlConnection conexion, SqlTransaction transaccion, string? genero, int? idCategoria, DateTime desde, DateTime? hasta, decimal? montoBase);

        // Borra las cuotas pendientes que no tienen ningún pago y devuelve cuántas borró.
        int EliminarCuotasPendientesSinPagos(SqlConnection conexion, SqlTransaction transaccion, string? genero, int? idCategoria, DateTime desde, DateTime? hasta, decimal? montoBase);
    }
}
