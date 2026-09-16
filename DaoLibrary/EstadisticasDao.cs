using EntityLibrary;
using Microsoft.Data.SqlClient;

namespace DaoLibrary
{
    public class EstadisticasDao : IEstadisticasDao
    {
        private static readonly string[] MesesAbreviados =
        {
            "Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic"
        };

        private readonly string _cadenaConexion;

        public EstadisticasDao(string cadenaConexion)
        {
            _cadenaConexion = cadenaConexion;
        }

        public ResumenGeneralInfo ObtenerResumenGeneral()
        {
            using SqlConnection conexion = new SqlConnection(_cadenaConexion);
            conexion.Open();

            var info = new ResumenGeneralInfo();

            ObtenerTotales(conexion, info);
            ObtenerBucketsDeDeuda(conexion, info);
            info.RecaudacionMensual = ObtenerRecaudacionMensual(conexion);
            info.CoberturaPagoMensual = ObtenerCoberturaMensual(conexion, info.TotalJugadores);
            info.MayorDeudaPendiente = ObtenerMayorDeudaPendiente(conexion);

            return info;
        }

        private static void ObtenerTotales(SqlConnection conexion, ResumenGeneralInfo info)
        {
            // deuda_acumulada ya descuenta el beneficio activo de Becados y Descuentos de cada
            // cuota (clampleado a 0), aunque la cuota se haya generado antes de asignarle el
            // beneficio.
            string query = $@"
                SELECT
                    (SELECT COUNT(*) FROM JUGADORES) AS total_jugadores,
                    (SELECT COUNT(DISTINCT FK_id_categoria) FROM JUGADORES) AS cantidad_categorias,
                    (SELECT COUNT(*) FROM PAGOS WHERE estado = 1 AND YEAR(fecha_pago) = YEAR(GETDATE()) AND MONTH(fecha_pago) = MONTH(GETDATE())) AS pagos_del_mes,
                    (SELECT ISNULL(SUM(monto_final), 0) FROM PAGOS WHERE estado = 1 AND YEAR(fecha_pago) = YEAR(GETDATE())) AS recaudado_anio,
                    (SELECT ISNULL(SUM(monto_final), 0) FROM PAGOS
                        WHERE estado = 1 AND fecha_pago >= DATEADD(MONTH, DATEDIFF(MONTH, 0, GETDATE()), 0)
                                          AND fecha_pago <  DATEADD(MONTH, DATEDIFF(MONTH, 0, GETDATE()) + 1, 0)) AS ingresado_mes_actual,
                    (SELECT ISNULL(SUM(monto_final), 0) FROM PAGOS
                        WHERE estado = 1 AND fecha_pago >= DATEADD(MONTH, DATEDIFF(MONTH, 0, GETDATE()) - 1, 0)
                                          AND fecha_pago <  DATEADD(MONTH, DATEDIFF(MONTH, 0, GETDATE()), 0)) AS ingresado_mes_anterior,
                    (SELECT ISNULL(SUM({DescuentosSql.SaldoAjustadoClampleadoExpr}), 0)
                        FROM PAGOS pg
                        {DescuentosSql.ApplyDescuentoActivo}
                        WHERE pg.estado = 0) AS deuda_acumulada";

            using SqlCommand comando = new SqlCommand(query, conexion);
            using SqlDataReader reader = comando.ExecuteReader();
            reader.Read();

            info.TotalJugadores = Convert.ToInt32(reader["total_jugadores"]);
            info.CantidadCategorias = Convert.ToInt32(reader["cantidad_categorias"]);
            info.PagosDelMes = Convert.ToInt32(reader["pagos_del_mes"]);
            info.RecaudadoAnioActual = Convert.ToDecimal(reader["recaudado_anio"]);
            info.IngresadoEsteMes = Convert.ToDecimal(reader["ingresado_mes_actual"]);
            info.IngresadoMesAnterior = Convert.ToDecimal(reader["ingresado_mes_anterior"]);
            info.DeudaAcumulada = Convert.ToDecimal(reader["deuda_acumulada"]);
        }

        // Agrupa jugadores por cuántas cuotas con saldo real > 0 tienen: 0 / 1 / 2+. Una cuota
        // que un beneficio de Becados y Descuentos dejó en $0 no cuenta como deuda impaga.
        private static void ObtenerBucketsDeDeuda(SqlConnection conexion, ResumenGeneralInfo info)
        {
            string query = $@"
                SELECT
                    SUM(CASE WHEN cnt = 0 THEN 1 ELSE 0 END) AS sin_deuda,
                    SUM(CASE WHEN cnt = 1 THEN 1 ELSE 0 END) AS una_impaga,
                    SUM(CASE WHEN cnt >= 2 THEN 1 ELSE 0 END) AS dos_o_mas
                FROM (
                    SELECT j.PK_id_jugador, COUNT(ca.PK_id_pago) AS cnt
                    FROM JUGADORES j
                    LEFT JOIN (
                        SELECT pg.PK_id_pago, pg.FK_id_jugador, ({DescuentosSql.SaldoAjustadoExpr}) AS saldo_ajustado
                        FROM PAGOS pg
                        {DescuentosSql.ApplyDescuentoActivo}
                        WHERE pg.estado = 0
                    ) ca ON ca.FK_id_jugador = j.PK_id_jugador AND ca.saldo_ajustado > 0
                    GROUP BY j.PK_id_jugador
                ) t";

            using SqlCommand comando = new SqlCommand(query, conexion);
            using SqlDataReader reader = comando.ExecuteReader();
            reader.Read();

            info.JugadoresSinDeuda = reader["sin_deuda"] != DBNull.Value ? Convert.ToInt32(reader["sin_deuda"]) : 0;
            info.JugadoresConUnaImpaga = reader["una_impaga"] != DBNull.Value ? Convert.ToInt32(reader["una_impaga"]) : 0;
            info.JugadoresConDosOMasImpagas = reader["dos_o_mas"] != DBNull.Value ? Convert.ToInt32(reader["dos_o_mas"]) : 0;
        }

        // Últimos 8 meses con recaudación real (PAGOS.estado = 1), en orden cronológico.
        // Se agrupa por PERÍODO de la cuota (fecha_vencimiento), no por cuándo se registró el
        // pago: si alguien paga en septiembre una cuota atrasada de enero, esa plata cuenta para
        // "enero" en el gráfico, no para "septiembre". Los pagos históricos previos a este sistema
        // nunca tuvieron fecha_vencimiento cargada, así que caen a fecha_pago como respaldo (sin
        // esto, esos ~1258 pagos reales desaparecerían del gráfico).
        // "periodo <= GETDATE()": no puede estar fechado en el futuro. Sin este filtro, una fila
        // con un año mal cargado (ej. un typo "2926" en vez de "2026") se cuela como el mes "más
        // reciente" y desplaza meses reales del gráfico.
        private static List<PuntoRecaudacionMensual> ObtenerRecaudacionMensual(SqlConnection conexion)
        {
            string query = @"
                SELECT TOP (8) YEAR(periodo) AS anio, MONTH(periodo) AS mes, SUM(monto_final) AS monto
                FROM (
                    SELECT monto_final, COALESCE(fecha_vencimiento, fecha_pago) AS periodo
                    FROM PAGOS
                    WHERE estado = 1
                ) x
                WHERE periodo IS NOT NULL AND periodo <= GETDATE()
                GROUP BY YEAR(periodo), MONTH(periodo)
                ORDER BY YEAR(periodo) DESC, MONTH(periodo) DESC";

            var puntos = new List<PuntoRecaudacionMensual>();
            using (SqlCommand comando = new SqlCommand(query, conexion))
            using (SqlDataReader reader = comando.ExecuteReader())
            {
                while (reader.Read())
                {
                    int mes = Convert.ToInt32(reader["mes"]);
                    puntos.Add(new PuntoRecaudacionMensual
                    {
                        Anio = Convert.ToInt32(reader["anio"]),
                        Mes = MesesAbreviados[mes - 1],
                        Monto = Convert.ToDecimal(reader["monto"])
                    });
                }
            }

            puntos.Reverse(); // más viejo primero, como el gráfico de barras lo espera
            return puntos;
        }

        // Últimos 6 meses: % de jugadores que registraron al menos un pago abonado ese mes.
        // Mismo criterio que ObtenerRecaudacionMensual: por período de la cuota, con fecha_pago
        // como respaldo para los pagos históricos sin fecha_vencimiento cargada.
        private static List<PuntoCoberturaMensual> ObtenerCoberturaMensual(SqlConnection conexion, int totalJugadores)
        {
            string query = @"
                SELECT TOP (6) YEAR(periodo) AS anio, MONTH(periodo) AS mes, COUNT(DISTINCT FK_id_jugador) AS jugadores_que_pagaron
                FROM (
                    SELECT FK_id_jugador, COALESCE(fecha_vencimiento, fecha_pago) AS periodo
                    FROM PAGOS
                    WHERE estado = 1
                ) x
                WHERE periodo IS NOT NULL AND periodo <= GETDATE()
                GROUP BY YEAR(periodo), MONTH(periodo)
                ORDER BY YEAR(periodo) DESC, MONTH(periodo) DESC";

            var puntos = new List<PuntoCoberturaMensual>();
            using (SqlCommand comando = new SqlCommand(query, conexion))
            using (SqlDataReader reader = comando.ExecuteReader())
            {
                while (reader.Read())
                {
                    int mes = Convert.ToInt32(reader["mes"]);
                    int jugadoresQuePagaron = Convert.ToInt32(reader["jugadores_que_pagaron"]);
                    puntos.Add(new PuntoCoberturaMensual
                    {
                        Anio = Convert.ToInt32(reader["anio"]),
                        Mes = MesesAbreviados[mes - 1],
                        Porcentaje = totalJugadores > 0 ? Math.Round(jugadoresQuePagaron * 100.0 / totalJugadores, 1) : 0
                    });
                }
            }

            puntos.Reverse();
            return puntos;
        }

        private static List<PendienteJugador> ObtenerMayorDeudaPendiente(SqlConnection conexion)
        {
            // Mismo criterio que PagosDao.ObtenerPendientesAgrupados: saldo ya con el beneficio
            // aplicado, y afuera si un beneficio le deja todo en $0.
            string query = $@"
                SELECT TOP (5) j.PK_id_jugador, p.nombre, p.apellido, c.nombre_categoria,
                       SUM({DescuentosSql.SaldoAjustadoClampleadoExpr}) AS monto_total,
                       COUNT(CASE WHEN ({DescuentosSql.SaldoAjustadoExpr}) > 0 THEN 1 END) AS cantidad_cuotas
                FROM PAGOS pg
                {DescuentosSql.ApplyDescuentoActivo}
                JOIN JUGADORES j ON pg.FK_id_jugador = j.PK_id_jugador
                JOIN PERSONA p ON j.FK_id_persona = p.PK_id_persona
                JOIN CATEGORIAS c ON j.FK_id_categoria = c.PK_id_categoria
                WHERE pg.estado = 0
                GROUP BY j.PK_id_jugador, p.nombre, p.apellido, c.nombre_categoria
                HAVING SUM({DescuentosSql.SaldoAjustadoClampleadoExpr}) > 0
                ORDER BY monto_total DESC";

            var resultado = new List<PendienteJugador>();
            using SqlCommand comando = new SqlCommand(query, conexion);
            using SqlDataReader reader = comando.ExecuteReader();
            while (reader.Read())
            {
                resultado.Add(new PendienteJugador
                {
                    IdJugador = Convert.ToInt32(reader["PK_id_jugador"]),
                    NombreCompleto = $"{reader["apellido"].ToString()?.Trim()}, {reader["nombre"].ToString()?.Trim()}",
                    Categoria = reader["nombre_categoria"].ToString()?.Trim() ?? "",
                    MontoTotal = Convert.ToDecimal(reader["monto_total"]),
                    CantidadCuotas = Convert.ToInt32(reader["cantidad_cuotas"])
                });
            }

            return resultado;
        }
    }
}
