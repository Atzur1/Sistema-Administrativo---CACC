using Microsoft.Data.SqlClient;

namespace DaoLibrary
{
    public class SqlTransactionRunner : ISqlTransactionRunner
    {
        private readonly string _cadenaConexion;

        public SqlTransactionRunner(string cadenaConexion)
        {
            _cadenaConexion = cadenaConexion;
        }

        // Abre una conexión, inicia una transacción (equivalente a TransactionScope pero
        // sobre una única conexión, sin escalar a transacción distribuida/MSDTC), ejecuta
        // la operación y hace commit. Cualquier excepción dispara rollback y se re-lanza
        // para que el llamador (controller) decida el código HTTP de respuesta.
        public T EjecutarEnTransaccion<T>(Func<SqlConnection, SqlTransaction, T> operacion)
        {
            using SqlConnection conexion = new SqlConnection(_cadenaConexion);
            conexion.Open();

            using SqlTransaction transaccion = conexion.BeginTransaction();
            try
            {
                T resultado = operacion(conexion, transaccion);
                transaccion.Commit();
                return resultado;
            }
            catch
            {
                transaccion.Rollback();
                throw;
            }
        }
    }
}
