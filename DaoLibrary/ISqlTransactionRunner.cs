using Microsoft.Data.SqlClient;

namespace DaoLibrary
{
    // Abstrae la apertura de conexión + transacción para que la capa de negocio
    // pueda orquestar varias operaciones de DAO como una única unidad atómica,
    // y para poder mockearla en tests de servicio sin tocar la base real.
    public interface ISqlTransactionRunner
    {
        T EjecutarEnTransaccion<T>(Func<SqlConnection, SqlTransaction, T> operacion);
    }
}
