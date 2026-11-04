using EntityLibrary;
using Microsoft.Data.SqlClient;

namespace DaoLibrary
{
    public enum BajaJugadorResultado { Ok, NoEncontrado, YaDadoDeBaja, FechaInvalida }

    // Resultado de dar de baja: cuántas cuotas sin pagos de meses posteriores se quitaron y cuántas becas o
    // descuentos vigentes o programados se cancelaron automáticamente. FechaBajaVigente: la baja que ya tenía (con
    // YaDadoDeBaja), para que un reintento con la misma fecha se reconozca como tal. InicioPermanencia: desde cuándo
    // está en el club (con FechaInvalida).
    public sealed record ResultadoBajaJugador(BajaJugadorResultado Resultado, int CuotasEliminadas = 0, int BeneficiosCancelados = 0,
        DateTime? FechaBajaVigente = null, DateTime? InicioPermanencia = null);

    public enum ReincorporacionResultado { Reincorporado, NoEncontrado, YaActivo }

    // CategoriaCambiada: el reingreso vino con una categoría distinta de la que tenía.
    public sealed record ResultadoReincorporacion(ReincorporacionResultado Resultado, bool CategoriaCambiada = false);

    public class JugadoresDao : IJugadoresDao
    {
        private readonly string _cadenaConexion;

        private const string SelectJugador = @"
                SELECT j.PK_id_jugador, p.nombre, p.apellido, p.Dni, p.genero, c.nombre_categoria, j.fecha_alta, j.fecha_baja
                FROM JUGADORES j
                JOIN PERSONAS p ON j.FK_id_persona = p.PK_id_persona
                JOIN CATEGORIAS c ON j.FK_id_categoria = c.PK_id_categoria";

        public JugadoresDao(string cadenaConexion)
        {
            _cadenaConexion = cadenaConexion;
        }

        public IReadOnlyList<JugadorResumen> ListarJugadores()
        {
            var resultado = new List<JugadorResumen>();

            string query = SelectJugador + @"
                ORDER BY p.apellido, p.nombre";

            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);

            using SqlCommand comando = new SqlCommand(query, conexion);
            using SqlDataReader reader = comando.ExecuteReader();
            while (reader.Read())
            {
                resultado.Add(LeerJugador(reader));
            }

            return resultado;
        }

        public JugadorResumen? ObtenerJugadorPorId(int idJugador)
        {
            string query = SelectJugador + @"
                WHERE j.PK_id_jugador = @idJugador";

            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);

            using SqlCommand comando = new SqlCommand(query, conexion);
            comando.Parameters.AddWithValue("@idJugador", idJugador);

            using SqlDataReader reader = comando.ExecuteReader();
            return reader.Read() ? LeerJugador(reader) : null;
        }

        // Da de baja a un jugador desde esa fecha: el generador deja de emitirle cuotas a partir del mes
        // siguiente. Las cuotas pendientes SIN pagos de meses posteriores al de la baja se borran (por
        // ejemplo, una baja que se carga tarde, o un mes futuro que se había emitido para pagar por
        // adelantado); las que ya tienen algún pago y las de meses anteriores quedan como deuda real.
        public ResultadoBajaJugador DarDeBaja(int idJugador, DateTime fechaBaja)
        {
            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);
            using SqlTransaction transaccion = conexion.BeginTransaction(System.Data.IsolationLevel.Serializable);
            BloqueoEscrituraPagos.Tomar(conexion, transaccion);

            var resultado = DarDeBajaCore(conexion, transaccion, idJugador, fechaBaja);
            if (resultado.Resultado == BajaJugadorResultado.Ok) transaccion.Commit(); else transaccion.Rollback();
            return resultado;
        }

        // internal (no private) para que la prueba de integración la corra dentro de una transacción que revierte.
        internal static ResultadoBajaJugador DarDeBajaCore(SqlConnection conexion, SqlTransaction transaccion, int idJugador, DateTime fechaBaja)
        {
            DateTime? fechaAlta;
            using (var buscar = new SqlCommand(
                "SELECT fecha_alta, fecha_baja FROM JUGADORES WITH (UPDLOCK, HOLDLOCK) WHERE PK_id_jugador = @id",
                conexion, transaccion))
            {
                buscar.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = idJugador;
                using var reader = buscar.ExecuteReader();
                if (!reader.Read())
                {
                    return new ResultadoBajaJugador(BajaJugadorResultado.NoEncontrado);
                }
                if (reader["fecha_baja"] != DBNull.Value)
                {
                    return new ResultadoBajaJugador(BajaJugadorResultado.YaDadoDeBaja, FechaBajaVigente: Convert.ToDateTime(reader["fecha_baja"]));
                }
                fechaAlta = reader["fecha_alta"] != DBNull.Value ? Convert.ToDateTime(reader["fecha_alta"]) : null;
            }

            // En la base unificada (HU-053) la permanencia abierta de JUGADORES_PERIODOS también marca desde cuándo está en el
            // club: los jugadores que trajo la unificación no tienen fecha_alta y su período empieza el día de la importación.
            // Una baja anterior violaría el CHECK del período (antes terminaba en un error 500). En una base sin esa tabla
            // queda solo fecha_alta (la tabla se resuelve recién al ejecutar el IF).
            DateTime? inicio = fechaAlta;
            using (var periodo = new SqlCommand(@"
                DECLARE @inicio DATE = NULL;
                IF OBJECT_ID(N'dbo.JUGADORES_PERIODOS', N'U') IS NOT NULL
                    SELECT @inicio = MAX(fecha_alta) FROM dbo.JUGADORES_PERIODOS WHERE FK_id_jugador = @id AND fecha_baja IS NULL;
                SELECT @inicio;", conexion, transaccion))
            {
                periodo.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = idJugador;
                if (periodo.ExecuteScalar() is DateTime abierto && (inicio == null || abierto > inicio))
                {
                    inicio = abierto;
                }
            }

            if (inicio.HasValue && fechaBaja.Date < inicio.Value.Date)
            {
                return new ResultadoBajaJugador(BajaJugadorResultado.FechaInvalida, InicioPermanencia: inicio.Value.Date);
            }

            using (var baja = new SqlCommand("UPDATE JUGADORES SET fecha_baja = @fecha WHERE PK_id_jugador = @id", conexion, transaccion))
            {
                baja.Parameters.Add("@fecha", System.Data.SqlDbType.Date).Value = fechaBaja.Date;
                baja.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = idJugador;
                baja.ExecuteNonQuery();
            }

            int eliminadas;
            using (var limpiar = new SqlCommand(@"
                DELETE p
                FROM PAGOS p
                WHERE p.FK_id_jugador = @id AND p.concepto = 'Cuota' AND p.estado = 0
                  AND p.fecha_vencimiento > EOMONTH(@fecha)
                  AND NOT EXISTS (
                      SELECT 1 FROM PAGOS q
                      WHERE q.FK_id_jugador = p.FK_id_jugador AND q.concepto = 'Cuota' AND q.estado = 1
                        AND YEAR(q.fecha_vencimiento) = YEAR(p.fecha_vencimiento)
                        AND MONTH(q.fecha_vencimiento) = MONTH(p.fecha_vencimiento))", conexion, transaccion))
            {
                limpiar.Parameters.Add("@fecha", System.Data.SqlDbType.Date).Value = fechaBaja.Date;
                limpiar.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = idJugador;
                eliminadas = limpiar.ExecuteNonQuery();
            }

            // Becas y descuentos vigentes o programados: se cancelan con la fecha de baja, con el mismo criterio
            // que una cancelación a mano (DiscountDao.DeactivateDiscount). Siguen aplicando a los meses que
            // vencieron hasta esa fecha, así que lo que el jugador ya pagó con el beneficio sigue pagado y lo que
            // quedó pendiente conserva el descuento de esos meses. cancelada_por_baja permite mostrarlo así.
            int cancelados;
            using (var cancelar = new SqlCommand(@"
                UPDATE JUGADORES_DESCUENTOS
                SET estado_activo = 0,
                    cancelada_por_baja = 1,
                    fecha_cancelacion = CASE WHEN @fecha < fecha_inicio THEN EOMONTH(fecha_inicio, -1) ELSE @fecha END
                WHERE FK_id_jugador = @id AND estado_activo = 1 AND fecha_fin >= @fecha", conexion, transaccion))
            {
                cancelar.Parameters.Add("@fecha", System.Data.SqlDbType.Date).Value = fechaBaja.Date;
                cancelar.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = idJugador;
                cancelados = cancelar.ExecuteNonQuery();
            }

            return new ResultadoBajaJugador(BajaJugadorResultado.Ok, eliminadas, cancelados);
        }

        // Vuelve a dejar activo a un jugador dado de baja. Se toma como un reingreso: la fecha de alta pasa a ser
        // hoy (la anterior queda en Auditoría), así los meses en que estuvo afuera nunca cuentan como cuotas
        // faltantes. Si ya pasó el día 10, se le emite la cuota del mes en curso en el momento; si no, se la
        // genera el generador mensual el 10, como a todos. La inscripción es anual: si vuelve dentro del año de su última
        // inscripción no la paga de nuevo; si ese año ya terminó, se le asigna una nueva con la fecha de hoy.
        public bool Reactivar(int idJugador)
            => Reincorporar(idJugador, null, RelojNegocio.Hoy).Resultado == ReincorporacionResultado.Reincorporado;

        // El mismo reingreso, pedido por el Portal Deportivo (HU-057): opcionalmente con la categoría que informa, que rige
        // para la cuota que se emite en el momento y las siguientes (las ya emitidas conservan su monto). Todo o nada.
        public ResultadoReincorporacion Reincorporar(int idJugador, int? idCategoria, DateTime hoy)
        {
            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);
            using SqlTransaction transaccion = conexion.BeginTransaction();
            BloqueoEscrituraPagos.Tomar(conexion, transaccion);
            try
            {
                var resultado = ReincorporarCore(conexion, transaccion, idJugador, idCategoria, hoy);
                if (resultado.Resultado == ReincorporacionResultado.Reincorporado) transaccion.Commit(); else transaccion.Rollback();
                return resultado;
            }
            catch
            {
                // Si la base ya la revirtió (error de un trigger, víctima de deadlock), un Rollback taparía la excepción original.
                if (transaccion.Connection != null) transaccion.Rollback();
                throw;
            }
        }

        internal static bool ReactivarCore(SqlConnection conexion, SqlTransaction transaccion, int idJugador, DateTime hoy)
            => ReincorporarCore(conexion, transaccion, idJugador, null, hoy).Resultado == ReincorporacionResultado.Reincorporado;

        internal static ResultadoReincorporacion ReincorporarCore(SqlConnection conexion, SqlTransaction transaccion, int idJugador, int? idCategoria, DateTime hoy)
        {
            // La fecha de alta anterior define el año de la inscripción de quien nunca tuvo una: se lee antes de que el reingreso la pise.
            DateTime? altaAnterior;
            int categoriaActual;
            using (SqlCommand leer = new SqlCommand(
                "SELECT fecha_alta, fecha_baja, FK_id_categoria FROM JUGADORES WITH (UPDLOCK, HOLDLOCK) WHERE PK_id_jugador = @id",
                conexion, transaccion))
            {
                leer.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = idJugador;
                using var reader = leer.ExecuteReader();
                if (!reader.Read())
                {
                    return new ResultadoReincorporacion(ReincorporacionResultado.NoEncontrado);
                }
                if (reader["fecha_baja"] == DBNull.Value)
                {
                    return new ResultadoReincorporacion(ReincorporacionResultado.YaActivo);
                }
                altaAnterior = reader["fecha_alta"] is DateTime fecha ? fecha : null;
                categoriaActual = Convert.ToInt32(reader["FK_id_categoria"]);
            }

            bool categoriaCambiada = idCategoria.HasValue && idCategoria.Value != categoriaActual;
            using (SqlCommand comando = new SqlCommand(
                "UPDATE JUGADORES SET fecha_baja = NULL, fecha_alta = @hoy, FK_id_categoria = @categoria WHERE PK_id_jugador = @id",
                conexion, transaccion))
            {
                comando.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = idJugador;
                comando.Parameters.Add("@hoy", System.Data.SqlDbType.Date).Value = hoy.Date;
                comando.Parameters.Add("@categoria", System.Data.SqlDbType.Int).Value = idCategoria ?? categoriaActual;
                comando.ExecuteNonQuery();
            }
            if (categoriaCambiada)
            {
                PlayerDAO.AsegurarCategoriaDelJugador(conexion, transaccion, idJugador, idCategoria!.Value);
            }

            if (CalendarioCuotas.YaSeGeneraElMesEnCurso(hoy))
            {
                SqlCuotaMensual.Generar(conexion, transaccion, null, null, idJugador, hoy.Month, hoy.Year);
            }

            // La inscripción es anual: dentro del año de la última no se vuelve a pagar; fuera de él empieza un año nuevo.
            PlayerDAO.GenerarInscripcionAlReingreso(conexion, transaccion, idJugador, hoy, altaAnterior);
            return new ResultadoReincorporacion(ReincorporacionResultado.Reincorporado, categoriaCambiada);
        }

        private static JugadorResumen LeerJugador(SqlDataReader reader) => new()
        {
            IdJugador = Convert.ToInt32(reader["PK_id_jugador"]),
            Nombre = reader["nombre"].ToString()?.Trim() ?? "",
            Apellido = reader["apellido"].ToString()?.Trim() ?? "",
            Dni = reader["Dni"].ToString()?.Trim() ?? "",
            Genero = reader["genero"].ToString()?.Trim() ?? "",
            Categoria = reader["nombre_categoria"].ToString()?.Trim() ?? "",
            FechaAlta = reader["fecha_alta"] != DBNull.Value ? Convert.ToDateTime(reader["fecha_alta"]) : null,
            FechaBaja = reader["fecha_baja"] != DBNull.Value ? Convert.ToDateTime(reader["fecha_baja"]) : null
        };
    }
}
