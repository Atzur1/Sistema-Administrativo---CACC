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
            var hoy = RelojNegocio.Hoy;
            var primerDiaMesActual = new DateTime(hoy.Year, hoy.Month, 1);
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
                        PuedeCancelar = actual.VigenteDesde.Date >= primerDiaMesActual,
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
            var hoy = RelojNegocio.Hoy;
            return new ArancelResumen
            {
                ArancelMasculinoVigente = ObtenerMontoVigente("Masculino", hoy),
                ArancelFemeninoVigente = ObtenerMontoVigente("Femenino", hoy),
                ProximoCambioFecha = ObtenerProximoCambio(hoy)
            };
        }

        // Las columnas de HU-106 guardan el impacto que se aprobó; TR_AUDIT_ARANCELES las copia al evento de auditoría.
        private const string InsertArancel = @"
            INSERT INTO ARANCELES (genero, FK_id_categoria, monto, vigente_desde,
                                   tipo_cambio, valor_cambio, monto_anterior, jugadores_alcanzados, diferencia_mensual, impacto)
            VALUES (@genero, @idCategoria, @monto, @vigenteDesde,
                    @tipoCambio, @valorCambio, @montoAnterior, @jugadoresAlcanzados, @diferenciaMensual, @impacto)";

        public void ProgramarArancel(string? genero, int? idCategoria, decimal monto, DateTime vigenteDesde)
        {
            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);
            using SqlCommand comando = new SqlCommand(InsertArancel, conexion);
            CargarParametrosArancel(comando, genero, idCategoria, monto, vigenteDesde, null);
            comando.ExecuteNonQuery();
        }

        public void ProgramarArancel(SqlConnection conexion, SqlTransaction transaccion, string? genero, int? idCategoria, decimal monto, DateTime vigenteDesde, ImpactoArancel? impacto = null)
        {
            using SqlCommand comando = new SqlCommand(InsertArancel, conexion, transaccion);
            CargarParametrosArancel(comando, genero, idCategoria, monto, vigenteDesde, impacto);
            comando.ExecuteNonQuery();
        }

        // ===== HU-106: lecturas del impacto. Las corre TariffTransactionDao: sin transacción para la simulación y con la
        // de la confirmación, la misma que después graba. Ninguna escribe.

        public decimal? ObtenerMontoVigenteDelDestino(SqlConnection conexion, SqlTransaction? transaccion, string? genero, int? idCategoria, DateTime fecha)
        {
            // El arancel PROPIO del destino (el de la categoría, o el del género sin categoría), no el que resulta de la
            // jerarquía para un jugador: es lo que el historial muestra como "Vigente" y sobre lo que se aplica el aumento.
            string query = $@"
                SELECT TOP (1) monto FROM ARANCELES
                WHERE {MismoDestino} AND vigente_desde <= @fecha
                ORDER BY vigente_desde DESC";

            using SqlCommand comando = new SqlCommand(query, conexion, transaccion);
            CargarParametrosDestino(comando, genero, idCategoria);
            comando.Parameters.Add("@fecha", System.Data.SqlDbType.Date).Value = fecha.Date;
            var resultado = comando.ExecuteScalar();
            return resultado == null || resultado == DBNull.Value ? null : Convert.ToDecimal(resultado);
        }

        // ARANCELES más el arancel que se está por programar, con las columnas que mira la jerarquía y una marca para
        // saber cuál ganó. vigente_desde va con el mismo tipo que la columna (date).
        private const string ArancelesConElNuevo = @"(
                SELECT FK_id_categoria, genero, monto, vigente_desde, CAST(0 AS BIT) AS es_nuevo FROM ARANCELES
                UNION ALL
                SELECT @idCategoria, @genero, @montoNuevo, @vigenteDesde, CAST(1 AS BIT))";

        public ImpactoArancelConsulta ConsultarImpacto(SqlConnection conexion, SqlTransaction? transaccion, string? genero, int? idCategoria, decimal montoNuevo, DateTime vigenteDesde)
        {
            // Una sola consulta para todo el club (nada de una por jugador): cada jugador del alcance que está en el club
            // ese mes, su cuota de hoy (actual) y la que tendría con el arancel nuevo (nuevo). Lo alcanza el cambio si el
            // arancel nuevo es el que gana la jerarquía; si gana el de su categoría (HU-048), queda en los excluidos.
            string query = $@"
                WITH alcance AS (
                    SELECT j.FK_id_categoria, LTRIM(RTRIM(p.genero)) AS genero, actual.monto AS monto_actual, nuevo.es_nuevo
                    FROM JUGADORES j
                    JOIN PERSONAS p ON j.FK_id_persona = p.PK_id_persona
                    OUTER APPLY ({SqlCuotaMensual.ArancelDelJugador("@mes")}) AS actual
                    CROSS APPLY ({SqlCuotaMensual.ArancelDelJugador("@mes", ArancelesConElNuevo, "a.es_nuevo")}) AS nuevo
                    WHERE (@genero IS NULL OR LTRIM(RTRIM(p.genero)) = @genero)
                      AND (@idCategoria IS NULL OR j.FK_id_categoria = @idCategoria)
                      AND {SqlCuotaMensual.JugadorEnElClub("@mes")}
                )
                SELECT al.es_nuevo, al.FK_id_categoria, c.nombre_categoria, al.genero, al.monto_actual, COUNT(*) AS jugadores
                FROM alcance al
                LEFT JOIN CATEGORIAS c ON c.PK_id_categoria = al.FK_id_categoria
                GROUP BY al.es_nuevo, al.FK_id_categoria, c.nombre_categoria, al.genero, al.monto_actual
                ORDER BY c.nombre_categoria, al.FK_id_categoria, al.genero, al.monto_actual;";

            using SqlCommand comando = new SqlCommand(query, conexion, transaccion);
            CargarParametrosDestino(comando, genero, idCategoria);
            comando.Parameters.Add("@montoNuevo", System.Data.SqlDbType.Decimal).Value = montoNuevo;
            comando.Parameters["@montoNuevo"].Precision = 18;
            comando.Parameters["@montoNuevo"].Scale = 2;
            comando.Parameters.Add("@vigenteDesde", System.Data.SqlDbType.Date).Value = vigenteDesde.Date;
            comando.Parameters.Add("@mes", System.Data.SqlDbType.Date).Value = new DateTime(vigenteDesde.Year, vigenteDesde.Month, 1);

            var resultado = new ImpactoArancelConsulta();
            // Clave 0 = sin categoría (los ids de CATEGORIAS empiezan en 1).
            var excluidos = new Dictionary<int, ImpactoArancelExcluidos>();
            using SqlDataReader reader = comando.ExecuteReader();
            while (reader.Read())
            {
                int? categoria = reader["FK_id_categoria"] == DBNull.Value ? null : Convert.ToInt32(reader["FK_id_categoria"]);
                string? nombre = reader["nombre_categoria"] == DBNull.Value ? null : reader["nombre_categoria"].ToString()?.Trim();
                int jugadores = Convert.ToInt32(reader["jugadores"]);
                if (Convert.ToBoolean(reader["es_nuevo"]))
                {
                    resultado.Lineas.Add(new ImpactoArancelLinea
                    {
                        IdCategoria = categoria,
                        NombreCategoria = nombre,
                        Genero = reader["genero"]?.ToString() ?? string.Empty,
                        MontoActual = reader["monto_actual"] == DBNull.Value ? null : Convert.ToDecimal(reader["monto_actual"]),
                        MontoNuevo = montoNuevo,
                        Jugadores = jugadores
                    });
                }
                else
                {
                    // Un excluido es alguien del alcance cuya categoría tiene arancel propio: se informa por categoría.
                    if (!excluidos.TryGetValue(categoria ?? 0, out var fila))
                    {
                        fila = new ImpactoArancelExcluidos { IdCategoria = categoria, NombreCategoria = nombre };
                        excluidos[categoria ?? 0] = fila;
                        resultado.Excluidos.Add(fila);
                    }
                    fila.Jugadores += jugadores;
                }
            }
            return resultado;
        }

        public Arancel? ObtenerPorId(SqlConnection conexion, SqlTransaction transaccion, int idArancel)
        {
            const string query = @"
                SELECT a.PK_id_arancel, a.genero, a.FK_id_categoria, c.nombre_categoria, a.monto, a.vigente_desde
                FROM dbo.ARANCELES a
                LEFT JOIN dbo.CATEGORIAS c ON c.PK_id_categoria = a.FK_id_categoria
                WHERE a.PK_id_arancel = @idArancel";

            using SqlCommand comando = new SqlCommand(query, conexion, transaccion);
            comando.Parameters.AddWithValue("@idArancel", idArancel);
            using SqlDataReader reader = comando.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            return new Arancel
            {
                IdArancel = Convert.ToInt32(reader["PK_id_arancel"]),
                Genero = reader["genero"] == DBNull.Value ? null : reader["genero"].ToString()?.Trim(),
                IdCategoria = reader["FK_id_categoria"] == DBNull.Value ? null : Convert.ToInt32(reader["FK_id_categoria"]),
                NombreCategoria = reader["nombre_categoria"] == DBNull.Value ? null : reader["nombre_categoria"].ToString()?.Trim(),
                Monto = Convert.ToDecimal(reader["monto"]),
                VigenteDesde = Convert.ToDateTime(reader["vigente_desde"])
            };
        }

        // Mismo destino = el mismo género o la misma categoría (un arancel es de uno o de otro, nunca de ambos).
        private const string MismoDestino = "((@genero IS NOT NULL AND genero = @genero) OR (@idCategoria IS NOT NULL AND FK_id_categoria = @idCategoria))";

        private static void CargarParametrosDestino(SqlCommand comando, string? genero, int? idCategoria)
        {
            comando.Parameters.Add("@genero", System.Data.SqlDbType.VarChar, 20).Value = (object?)genero ?? DBNull.Value;
            comando.Parameters.Add("@idCategoria", System.Data.SqlDbType.Int).Value = (object?)idCategoria ?? DBNull.Value;
        }

        public DateTime? ObtenerVigenteDesdeEnMes(SqlConnection conexion, SqlTransaction? transaccion, string? genero, int? idCategoria, int anio, int mes)
        {
            string query = $@"
                SELECT TOP (1) vigente_desde FROM ARANCELES WITH (UPDLOCK, HOLDLOCK)
                WHERE {MismoDestino} AND YEAR(vigente_desde) = @anio AND MONTH(vigente_desde) = @mes
                ORDER BY vigente_desde";

            using SqlCommand comando = new SqlCommand(query, conexion, transaccion);
            CargarParametrosDestino(comando, genero, idCategoria);
            comando.Parameters.AddWithValue("@anio", anio);
            comando.Parameters.AddWithValue("@mes", mes);
            var resultado = comando.ExecuteScalar();
            return resultado == null || resultado == DBNull.Value ? null : Convert.ToDateTime(resultado);
        }

        public DateTime? ObtenerSiguienteVigenteDesde(SqlConnection conexion, SqlTransaction? transaccion, string? genero, int? idCategoria, DateTime desde)
        {
            string query = $"SELECT MIN(vigente_desde) FROM ARANCELES WHERE {MismoDestino} AND vigente_desde > @desde";

            using SqlCommand comando = new SqlCommand(query, conexion, transaccion);
            CargarParametrosDestino(comando, genero, idCategoria);
            comando.Parameters.Add("@desde", System.Data.SqlDbType.Date).Value = desde.Date;
            var resultado = comando.ExecuteScalar();
            return resultado == null || resultado == DBNull.Value ? null : Convert.ToDateTime(resultado);
        }

        public void EliminarArancel(SqlConnection conexion, SqlTransaction transaccion, int idArancel)
        {
            using SqlCommand comando = new SqlCommand("DELETE FROM ARANCELES WHERE PK_id_arancel = @idArancel", conexion, transaccion);
            comando.Parameters.AddWithValue("@idArancel", idArancel);
            comando.ExecuteNonQuery();
        }

        private static void CargarParametrosArancel(SqlCommand comando, string? genero, int? idCategoria, decimal monto, DateTime vigenteDesde, ImpactoArancel? impacto)
        {
            // Género o categoría, uno solo: el otro va como NULL (la base también lo exige).
            comando.Parameters.Add("@genero", System.Data.SqlDbType.VarChar, 20).Value = (object?)genero ?? DBNull.Value;
            comando.Parameters.Add("@idCategoria", System.Data.SqlDbType.Int).Value = (object?)idCategoria ?? DBNull.Value;
            comando.Parameters.AddWithValue("@monto", monto);
            comando.Parameters.AddWithValue("@vigenteDesde", vigenteDesde.Date);

            // HU-106: lo que se aprobó (null en lo que no pasa por la confirmación con impacto).
            comando.Parameters.Add("@tipoCambio", System.Data.SqlDbType.VarChar, 12).Value = (object?)impacto?.Tipo.ToString() ?? DBNull.Value;
            AgregarDecimal(comando, "@valorCambio", impacto?.Valor);
            AgregarDecimal(comando, "@montoAnterior", impacto?.MontoBase);
            comando.Parameters.Add("@jugadoresAlcanzados", System.Data.SqlDbType.Int).Value = (object?)impacto?.Jugadores ?? DBNull.Value;
            AgregarDecimal(comando, "@diferenciaMensual", impacto?.DiferenciaMensual);
            comando.Parameters.Add("@impacto", System.Data.SqlDbType.NVarChar, -1).Value = impacto == null
                ? DBNull.Value
                : System.Text.Json.JsonSerializer.Serialize(impacto.Lineas.Select(l => new
                {
                    idCategoria = l.IdCategoria,
                    categoria = l.NombreCategoria,
                    genero = l.Genero,
                    montoActual = l.MontoActual,
                    montoNuevo = l.MontoNuevo,
                    jugadores = l.Jugadores,
                    diferenciaTotal = l.DiferenciaTotal
                }));
        }

        private static void AgregarDecimal(SqlCommand comando, string nombre, decimal? valor)
        {
            var parametro = comando.Parameters.Add(nombre, System.Data.SqlDbType.Decimal);
            parametro.Precision = 18;
            parametro.Scale = 2;
            parametro.Value = (object?)valor ?? DBNull.Value;
        }

        internal const string ArancelesSeleccion = @"
                FROM dbo.ARANCELES a
                LEFT JOIN dbo.CATEGORIAS c ON c.PK_id_categoria = a.FK_id_categoria
                OUTER APPLY (
                    SELECT TOP (1) ac.nombre_usuario, ac.apellido_usuario
                    FROM dbo.AUDITORIA_CAMBIOS ac
                    WHERE ac.entidad = N'ARANCELES' AND ac.id_entidad = CONVERT(NVARCHAR(128), a.PK_id_arancel)
                      AND ac.accion = 'INSERT'
                    ORDER BY ac.fecha_utc, ac.PK_id_evento
                ) actor";

        private List<Arancel> ObtenerTodos()
        {
            var resultado = new List<Arancel>();
            string query = @"
                SELECT a.PK_id_arancel, a.genero, a.FK_id_categoria, c.nombre_categoria, a.monto, a.vigente_desde,
                       actor.nombre_usuario AS responsable_nombre, actor.apellido_usuario AS responsable_apellido
                " + ArancelesSeleccion;

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
