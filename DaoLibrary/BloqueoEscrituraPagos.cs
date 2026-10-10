using System.Data;
using Microsoft.Data.SqlClient;

namespace DaoLibrary
{
    // Pone en fila las transacciones que ESCRIBEN cuotas y pagos (PAGOS / JUGADORES): cobros, registro y
    // anulación de pagos, generación de cuotas, aranceles, inscripciones, altas y bajas de jugadores.
    //
    // Los ids salen de secuencias (V20261011_04), así que ya no hace falta bloquear la tabla para
    // calcularlos. Pero varias de esas operaciones leen con UPDLOCK/HOLDLOCK rangos amplios de PAGOS (por
    // ejemplo, la generación mensual recorre todos los jugadores) y después insertan: dos de ellas al mismo
    // tiempo pueden trabarse mutuamente (deadlock) y SQL Server cancela una. Un bloqueo de aplicación tomado
    // como PRIMER paso de cada una de esas transacciones las ordena sin tocar las lecturas, que siguen
    // libres. Se libera solo con el commit o el rollback.
    public static class BloqueoEscrituraPagos
    {
        private const string Recurso = "CACC_ESCRITURA_PAGOS";
        private const int EsperaMaximaMs = 30_000;

        public static void Tomar(SqlConnection conexion, SqlTransaction transaccion)
        {
            using var comando = new SqlCommand(
                "EXEC @resultado = sys.sp_getapplock @Resource = @recurso, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = @espera;",
                conexion, transaccion);
            comando.Parameters.Add("@recurso", SqlDbType.NVarChar, 255).Value = Recurso;
            comando.Parameters.Add("@espera", SqlDbType.Int).Value = EsperaMaximaMs;
            var resultado = comando.Parameters.Add("@resultado", SqlDbType.Int);
            resultado.Direction = ParameterDirection.Output;
            comando.ExecuteNonQuery();

            // 0 y 1 = concedido; negativo = venció la espera, deadlock o error.
            if (resultado.Value is not int codigo || codigo < 0)
            {
                throw new InvalidOperationException("Hay otra operación de cobro en curso. Intentá de nuevo en unos segundos.");
            }
        }
    }
}
