using EntityLibrary;
using Microsoft.Data.SqlClient;

namespace DaoLibrary
{
    public class ArancelesDao : IArancelesDao
    {
        private readonly string _cadenaConexion;

        public ArancelesDao(string cadenaConexion)
        {
            _cadenaConexion = cadenaConexion;
        }

        public decimal? ObtenerMontoVigente(string genero, DateTime fecha)
        {
            string query = @"
                SELECT TOP (1) monto FROM ARANCELES
                WHERE genero = @genero AND FK_id_categoria IS NULL AND vigente_desde <= @fecha
                ORDER BY vigente_desde DESC";

            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);

            using SqlCommand comando = new SqlCommand(query, conexion);
            comando.Parameters.AddWithValue("@genero", genero);
            comando.Parameters.AddWithValue("@fecha", fecha.Date);

            var resultado = comando.ExecuteScalar();
            return resultado == null || resultado == DBNull.Value ? null : Convert.ToDecimal(resultado);
        }

        public decimal? ObtenerMontoVigente(string genero, int idCategoria, DateTime fecha)
        {
            // Jerarquía: primero el arancel de la categoría (si hay uno vigente, el género no
            // importa); recién si no hay, el del género. Misma regla que PagosDao al generar cuotas.
            string query = @"
                SELECT TOP (1) monto FROM ARANCELES
                WHERE vigente_desde <= @fecha
                  AND (FK_id_categoria = @idCategoria OR (FK_id_categoria IS NULL AND genero = @genero))
                ORDER BY CASE WHEN FK_id_categoria IS NULL THEN 1 ELSE 0 END, vigente_desde DESC";

            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);

            using SqlCommand comando = new SqlCommand(query, conexion);
            comando.Parameters.AddWithValue("@genero", genero);
            comando.Parameters.AddWithValue("@idCategoria", idCategoria);
            comando.Parameters.AddWithValue("@fecha", fecha.Date);

            var resultado = comando.ExecuteScalar();
            return resultado == null || resultado == DBNull.Value ? null : Convert.ToDecimal(resultado);
        }

        public IReadOnlyList<ArancelHistorialItem> ObtenerHistorial()
        {
            var aranceles = ObtenerTodos();
            var hoy = DateTime.Now.Date;
            var resultado = new List<ArancelHistorialItem>();

            // vigente_hasta se calcula, no se guarda: es el día anterior al vigente_desde del
            // próximo arancel del mismo destino (mismo género o misma categoría), si existe.
            // Así nunca queda desincronizado.
            foreach (var grupo in aranceles.GroupBy(a => (a.Genero, a.IdCategoria)))
            {
                var ordenados = grupo.OrderBy(a => a.VigenteDesde).ToList();
                for (int i = 0; i < ordenados.Count; i++)
                {
                    var actual = ordenados[i];
                    DateTime? vigenteHasta = i + 1 < ordenados.Count
                        ? ordenados[i + 1].VigenteDesde.AddDays(-1)
                        : null;

                    string estado;
                    if (actual.VigenteDesde.Date > hoy)
                    {
                        estado = "Programado";
                    }
                    else if (vigenteHasta == null || vigenteHasta.Value.Date >= hoy)
                    {
                        estado = "Vigente";
                    }
                    else
                    {
                        estado = "Anterior";
                    }

                    resultado.Add(new ArancelHistorialItem
                    {
                        IdArancel = actual.IdArancel,
                        Genero = actual.Genero,
                        IdCategoria = actual.IdCategoria,
                        NombreCategoria = actual.NombreCategoria,
                        Monto = actual.Monto,
                        VigenteDesde = actual.VigenteDesde,
                        VigenteHasta = vigenteHasta,
                        Estado = estado,
                        ResponsableNombre = actual.ResponsableNombre,
                        ResponsableApellido = actual.ResponsableApellido
                    });
                }
            }

            return resultado.OrderByDescending(a => a.VigenteDesde).ToList();
        }

        public ArancelResumen ObtenerResumen()
        {
            var hoy = DateTime.Now.Date;
            return new ArancelResumen
            {
                ArancelMasculinoVigente = ObtenerMontoVigente("Masculino", hoy),
                ArancelFemeninoVigente = ObtenerMontoVigente("Femenino", hoy),
                ProximoCambioFecha = ObtenerProximoCambio(hoy)
            };
        }

        private const string InsertArancel =
            "INSERT INTO ARANCELES (genero, FK_id_categoria, monto, vigente_desde) VALUES (@genero, @idCategoria, @monto, @vigenteDesde)";

        public void ProgramarArancel(string? genero, int? idCategoria, decimal monto, DateTime vigenteDesde)
        {
            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);
            using SqlCommand comando = new SqlCommand(InsertArancel, conexion);
            CargarParametrosArancel(comando, genero, idCategoria, monto, vigenteDesde);
            comando.ExecuteNonQuery();
        }

        public void ProgramarArancel(SqlConnection conexion, SqlTransaction transaccion, string? genero, int? idCategoria, decimal monto, DateTime vigenteDesde)
        {
            using SqlCommand comando = new SqlCommand(InsertArancel, conexion, transaccion);
            CargarParametrosArancel(comando, genero, idCategoria, monto, vigenteDesde);
            comando.ExecuteNonQuery();
        }

        private static void CargarParametrosArancel(SqlCommand comando, string? genero, int? idCategoria, decimal monto, DateTime vigenteDesde)
        {
            // Género o categoría, uno solo: el otro va como NULL (la base también lo exige).
            comando.Parameters.Add("@genero", System.Data.SqlDbType.VarChar, 20).Value = (object?)genero ?? DBNull.Value;
            comando.Parameters.Add("@idCategoria", System.Data.SqlDbType.Int).Value = (object?)idCategoria ?? DBNull.Value;
            comando.Parameters.AddWithValue("@monto", monto);
            comando.Parameters.AddWithValue("@vigenteDesde", vigenteDesde.Date);
        }

        private List<Arancel> ObtenerTodos()
        {
            var resultado = new List<Arancel>();
            string query = @"
                SELECT a.PK_id_arancel, a.genero, a.FK_id_categoria, c.nombre_categoria, a.monto, a.vigente_desde,
                       actor.nombre_usuario AS responsable_nombre, actor.apellido_usuario AS responsable_apellido
                FROM dbo.ARANCELES a
                LEFT JOIN dbo.CATEGORIAS c ON c.PK_id_categoria = a.FK_id_categoria
                OUTER APPLY (
                    SELECT TOP (1) ac.nombre_usuario, ac.apellido_usuario
                    FROM dbo.AUDITORIA_CAMBIOS ac
                    WHERE ac.entidad = N'ARANCELES' AND ac.id_entidad = CONVERT(NVARCHAR(128), a.PK_id_arancel)
                      AND ac.accion = 'INSERT'
                    ORDER BY ac.fecha_utc, ac.PK_id_evento
                ) actor";

            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);

            using SqlCommand comando = new SqlCommand(query, conexion);
            using SqlDataReader reader = comando.ExecuteReader();
            while (reader.Read())
            {
                resultado.Add(new Arancel
                {
                    IdArancel = Convert.ToInt32(reader["PK_id_arancel"]),
                    Genero = reader["genero"] == DBNull.Value ? null : reader["genero"].ToString()?.Trim(),
                    IdCategoria = reader["FK_id_categoria"] == DBNull.Value ? null : Convert.ToInt32(reader["FK_id_categoria"]),
                    NombreCategoria = reader["nombre_categoria"] == DBNull.Value ? null : reader["nombre_categoria"].ToString()?.Trim(),
                    Monto = Convert.ToDecimal(reader["monto"]),
                    VigenteDesde = Convert.ToDateTime(reader["vigente_desde"]),
                    ResponsableNombre = reader["responsable_nombre"] == DBNull.Value ? null : reader["responsable_nombre"].ToString()?.Trim(),
                    ResponsableApellido = reader["responsable_apellido"] == DBNull.Value ? null : reader["responsable_apellido"].ToString()?.Trim()
                });
            }

            return resultado;
        }

        private DateTime? ObtenerProximoCambio(DateTime hoy)
        {
            string query = "SELECT MIN(vigente_desde) FROM ARANCELES WHERE vigente_desde > @hoy";

            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);

            using SqlCommand comando = new SqlCommand(query, conexion);
            comando.Parameters.AddWithValue("@hoy", hoy);

            var resultado = comando.ExecuteScalar();
            return resultado == null || resultado == DBNull.Value ? null : Convert.ToDateTime(resultado);
        }
    }
}
