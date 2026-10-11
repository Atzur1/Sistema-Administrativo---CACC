using Microsoft.Data.SqlClient;

namespace DaoLibrary
{
    // Intentos fallidos de ingreso en la base (tabla INTENTOS_LOGIN, migración V20261013_01): sobreviven a un
    // reinicio de la API y valen igual si algún día corre más de una instancia.
    public class IntentosLoginDao : IIntentosLoginStore
    {
        private readonly string _cadenaConexion;

        public IntentosLoginDao(string cadenaConexion)
        {
            _cadenaConexion = cadenaConexion;
        }

        public (int Fallos, DateTimeOffset Desde)? Obtener(string clave)
        {
            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);
            using SqlCommand comando = new SqlCommand(
                "SELECT fallos, desde_utc FROM dbo.INTENTOS_LOGIN WHERE clave = @clave", conexion);
            comando.Parameters.Add("@clave", System.Data.SqlDbType.Char, 64).Value = clave;
            using SqlDataReader reader = comando.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }
            var desde = DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc);
            return (reader.GetInt32(0), new DateTimeOffset(desde));
        }

        // Una sola sentencia atómica (MERGE con HOLDLOCK): dos fallos simultáneos de la misma cuenta suman dos.
        // De paso borra las filas que ya salieron de la ventana, para que la tabla no crezca con identificadores
        // probados al azar.
        public void RegistrarFallo(string clave, DateTimeOffset ahora, TimeSpan ventana)
        {
            const string query = @"
                MERGE dbo.INTENTOS_LOGIN WITH (HOLDLOCK) AS t
                USING (SELECT @clave AS clave) AS s ON t.clave = s.clave
                WHEN MATCHED THEN UPDATE SET
                    fallos = CASE WHEN t.desde_utc <= @vencidoAntesDe THEN 1 ELSE t.fallos + 1 END,
                    desde_utc = CASE WHEN t.desde_utc <= @vencidoAntesDe THEN @ahora ELSE t.desde_utc END
                WHEN NOT MATCHED THEN INSERT (clave, fallos, desde_utc) VALUES (@clave, 1, @ahora);

                DELETE FROM dbo.INTENTOS_LOGIN WHERE desde_utc <= @vencidoAntesDe AND clave <> @clave;";
            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);
            using SqlCommand comando = new SqlCommand(query, conexion);
            comando.Parameters.Add("@clave", System.Data.SqlDbType.Char, 64).Value = clave;
            comando.Parameters.Add("@ahora", System.Data.SqlDbType.DateTime2).Value = ahora.UtcDateTime;
            comando.Parameters.Add("@vencidoAntesDe", System.Data.SqlDbType.DateTime2).Value = (ahora - ventana).UtcDateTime;
            comando.ExecuteNonQuery();
        }

        public void Limpiar(string clave)
        {
            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);
            using SqlCommand comando = new SqlCommand("DELETE FROM dbo.INTENTOS_LOGIN WHERE clave = @clave", conexion);
            comando.Parameters.Add("@clave", System.Data.SqlDbType.Char, 64).Value = clave;
            comando.ExecuteNonQuery();
        }
    }
}
