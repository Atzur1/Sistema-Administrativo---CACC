using Microsoft.Data.SqlClient;

namespace DaoLibrary;

public interface IIntegracionRechazosDao
{
    void Registrar(RechazoIntegracion rechazo);
}

// HU-056: registro de los pedidos que la integración rechazó por una regla de negocio (INTEGRACION_RECHAZOS). Es de solo
// agregar. Abre SU PROPIA conexión: el rechazo se graba fuera de la transacción de negocio, así que revertirla no lo borra.
// El operador y el sistema de origen son los del pedido (SqlAuditActorContext), los mismos que ve la auditoría.
public class IntegracionRechazosDao : IIntegracionRechazosDao
{
    private readonly string _cadenaConexion;

    public IntegracionRechazosDao(string cadenaConexion)
    {
        _cadenaConexion = cadenaConexion;
    }

    public void Registrar(RechazoIntegracion rechazo)
    {
        const string query = @"
            INSERT INTO INTEGRACION_RECHAZOS (origen_sistema, operacion, id_usuario, id_persona, id_jugador, error_code, motivo, correlacion)
            VALUES (@origen, @operacion, @usuario, @persona, @jugador, @errorCode, @motivo, @correlacion);";

        using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);
        using SqlCommand comando = new SqlCommand(query, conexion);
        comando.Parameters.AddWithValue("@origen", (object?)SqlAuditActorContext.CurrentSystem ?? DBNull.Value);
        comando.Parameters.AddWithValue("@operacion", rechazo.Operacion);
        comando.Parameters.AddWithValue("@usuario", (object?)SqlAuditActorContext.CurrentUserId ?? DBNull.Value);
        comando.Parameters.AddWithValue("@persona", (object?)rechazo.IdPersona ?? DBNull.Value);
        comando.Parameters.AddWithValue("@jugador", (object?)rechazo.IdJugador ?? DBNull.Value);
        comando.Parameters.AddWithValue("@errorCode", rechazo.ErrorCode);
        // Los largos de la tabla: un motivo o una correlación de más no tienen que hacer fallar el registro.
        comando.Parameters.AddWithValue("@motivo", Acortar(rechazo.Motivo, 400));
        comando.Parameters.AddWithValue("@correlacion", (object?)Acortar(rechazo.Correlacion, 100) ?? DBNull.Value);
        comando.ExecuteNonQuery();
    }

    private static string? Acortar(string? texto, int maximo)
        => texto is null || texto.Length <= maximo ? texto : texto[..maximo];
}
