using System.Data;
using EntityLibrary;
using Microsoft.Data.SqlClient;

namespace DaoLibrary;

public sealed class ReportesDao(string conexion) : IReportesDao
{
    public int Contar(string tipo, int? idCategoria, int top, int anio, int mes)
    {
        using var db = SqlConnectionFactory.Open(conexion);
        return ContarCore(db, null, tipo, idCategoria, top, anio, mes);
    }

    internal static int ContarCore(SqlConnection db, SqlTransaction? transaccion,
        string tipo, int? idCategoria, int top, int anio, int mes)
    {
        // Cada selección pertenece al DAO que exporta. El conteo agrega solamente COUNT.
        string sql = tipo switch
        {
            "deudores" => "SELECT COUNT(*) FROM (" + PagosDao.DeudoresSeleccion + ") filas",
            "pagos-recientes" => PagosDao.PagosRegistradosCtes +
                " SELECT CASE WHEN COUNT(*) > @top THEN @top ELSE COUNT(*) END FROM Registrados WHERE cumple_filtros = 1",
            "becados" => "SELECT COUNT(*) " + DiscountDao.FromJoins,
            "aranceles" => "SELECT COUNT(*) " + ArancelesDao.ArancelesSeleccion,
            "recaudacion" => "SELECT COUNT(*) " + RecaudacionDao.PagosDelPeriodo,
            _ => throw new ArgumentException("Reporte desconocido.", nameof(tipo))
        };
        using var cmd = new SqlCommand(sql, db, transaccion);
        cmd.Parameters.Add("@idCategoria", SqlDbType.Int).Value = (object?)idCategoria ?? DBNull.Value;
        cmd.Parameters.Add("@top", SqlDbType.Int).Value = Math.Clamp(top, 1, 1000);
        cmd.Parameters.Add("@parcial", SqlDbType.Bit).Value = DBNull.Value;
        cmd.Parameters.Add("@patron", SqlDbType.NVarChar, 120).Value = DBNull.Value;
        if (tipo == "recaudacion")
        {
            var desde = new DateTime(anio, mes, 1);
            cmd.Parameters.Add("@desde", SqlDbType.Date).Value = desde;
            cmd.Parameters.Add("@hasta", SqlDbType.Date).Value = desde.AddMonths(1);
        }
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void Registrar(ExportacionReporte exportacion)
    {
        using var db = SqlConnectionFactory.Open(conexion);
        RegistrarCore(db, null, exportacion);
    }

    internal static void RegistrarCore(SqlConnection db, SqlTransaction? transaccion, ExportacionReporte exportacion)
    {
        // El nombre se resuelve desde la identidad autenticada en base, nunca desde el navegador.
        using var cmd = new SqlCommand(@"
            INSERT INTO dbo.EXPORTACIONES_REPORTES
                (fecha_hora, usuario_id, persona, tipo, formato, filtros, cantidad_filas)
            SELECT @fecha, @usuario,
                LTRIM(RTRIM(ISNULL(p.apellido, ''))) + ', ' + LTRIM(RTRIM(ISNULL(p.nombre, ''))),
                @tipo, @formato, @filtros, @filas
            FROM dbo.USUARIOS u
            JOIN dbo.PERSONAS p ON p.PK_id_persona = u.FK_id_persona
            WHERE u.PK_id_usuario = @usuario;
            IF @@ROWCOUNT <> 1
                THROW 51000, 'No se pudo identificar al responsable de la exportacion.', 1;", db, transaccion);
        cmd.Parameters.Add("@fecha", SqlDbType.DateTime2).Value = exportacion.FechaHora;
        cmd.Parameters.Add("@usuario", SqlDbType.Int).Value = exportacion.UsuarioId;
        cmd.Parameters.Add("@tipo", SqlDbType.NVarChar, 40).Value = exportacion.Tipo;
        cmd.Parameters.Add("@formato", SqlDbType.VarChar, 3).Value = exportacion.Formato;
        cmd.Parameters.Add("@filtros", SqlDbType.NVarChar, 600).Value = exportacion.Filtros;
        cmd.Parameters.Add("@filas", SqlDbType.Int).Value = exportacion.CantidadFilas;
        cmd.ExecuteNonQuery();
    }

    public HistorialExportacionesPagina Historial(int pagina, int tamanioPagina)
    {
        using var db = SqlConnectionFactory.Open(conexion);
        return HistorialCore(db, null, pagina, tamanioPagina);
    }

    internal static HistorialExportacionesPagina HistorialCore(SqlConnection db, SqlTransaction? transaccion,
        int pagina, int tamanioPagina)
    {
        using var cmd = new SqlCommand(@"
            SELECT COUNT(*) FROM dbo.EXPORTACIONES_REPORTES;
            SELECT id, fecha_hora, usuario_id, persona, tipo, formato, filtros, cantidad_filas
            FROM dbo.EXPORTACIONES_REPORTES
            ORDER BY fecha_hora DESC, id DESC
            OFFSET @saltar ROWS FETCH NEXT @tomar ROWS ONLY;", db, transaccion);
        cmd.Parameters.Add("@saltar", SqlDbType.Int).Value = checked((pagina - 1) * tamanioPagina);
        cmd.Parameters.Add("@tomar", SqlDbType.Int).Value = tamanioPagina;
        using var reader = cmd.ExecuteReader();
        reader.Read();
        var resultado = new HistorialExportacionesPagina
        {
            Total = reader.GetInt32(0), Pagina = pagina, TamanioPagina = tamanioPagina
        };
        reader.NextResult();
        while (reader.Read())
        {
            resultado.Items.Add(new ExportacionReporte
            {
                Id = reader.GetInt64(0), FechaHora = reader.GetDateTime(1), UsuarioId = reader.GetInt32(2),
                Persona = reader.GetString(3), Tipo = reader.GetString(4), Formato = reader.GetString(5),
                Filtros = reader.GetString(6), CantidadFilas = reader.GetInt32(7)
            });
        }
        return resultado;
    }
}
