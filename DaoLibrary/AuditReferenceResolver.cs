using System.Data;
using Microsoft.Data.SqlClient;

namespace DaoLibrary;

// Busca en la base el nombre de cada referencia (ver AuditReferences) con una consulta por tipo, para toda una
// página de eventos a la vez: no es una consulta por fila. Solo lee.
internal static class AuditReferenceResolver
{
    // Cuántos números van en un mismo IN. Una página trae a lo sumo 100 eventos, así que casi nunca se llega.
    private const int LoteMaximo = 500;

    // Devuelve "tipo:número" -> nombre. Lo que ya no existe en la base (un jugador eliminado) no figura.
    public static Dictionary<string, string> Resolver(
        SqlConnection conexion, SqlTransaction? transaccion, IEnumerable<(string Tipo, long Id)> referencias)
    {
        var nombres = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var grupo in referencias.GroupBy(r => r.Tipo))
        {
            string? consulta = ConsultaDe(grupo.Key);
            if (consulta is null) continue;

            foreach (long[] lote in grupo.Select(r => r.Id).Distinct().Chunk(LoteMaximo))
            {
                var parametros = lote.Select((id, i) => new SqlParameter($"@p{i}", SqlDbType.BigInt) { Value = id }).ToArray();
                using var comando = new SqlCommand(
                    consulta.Replace("{ids}", string.Join(", ", parametros.Select(p => p.ParameterName))), conexion, transaccion);
                comando.Parameters.AddRange(parametros);

                using SqlDataReader lector = comando.ExecuteReader();
                while (lector.Read())
                {
                    string? nombre = lector.IsDBNull(1) ? null : lector.GetString(1).Trim();
                    if (!string.IsNullOrEmpty(nombre))
                    {
                        nombres[AuditReferences.Clave(grupo.Key, lector.GetInt64(0))] = nombre;
                    }
                }
            }
        }

        return nombres;
    }

    // Cada consulta devuelve (número, texto ya armado).
    private static string? ConsultaDe(string tipo) => tipo switch
    {
        AuditReferences.Jugador => @"
            SELECT CAST(j.PK_id_jugador AS BIGINT),
                   LTRIM(RTRIM(ISNULL(p.apellido, ''))) + ', ' + LTRIM(RTRIM(ISNULL(p.nombre, '')))
                       + CASE WHEN p.Dni IS NULL THEN '' ELSE ' (DNI ' + LTRIM(RTRIM(p.Dni)) + ')' END
            FROM JUGADORES j JOIN PERSONA p ON p.PK_id_persona = j.FK_id_persona
            WHERE j.PK_id_jugador IN ({ids})",

        AuditReferences.Persona => @"
            SELECT CAST(p.PK_id_persona AS BIGINT),
                   LTRIM(RTRIM(ISNULL(p.apellido, ''))) + ', ' + LTRIM(RTRIM(ISNULL(p.nombre, '')))
                       + CASE WHEN p.Dni IS NULL THEN '' ELSE ' (DNI ' + LTRIM(RTRIM(p.Dni)) + ')' END
            FROM PERSONA p
            WHERE p.PK_id_persona IN ({ids})",

        AuditReferences.Categoria => @"
            SELECT CAST(PK_id_categoria AS BIGINT), nombre_categoria
            FROM CATEGORIAS WHERE PK_id_categoria IN ({ids})",

        AuditReferences.Tipo => @"
            SELECT CAST(PK_id_descuento AS BIGINT), tipo_descuento
            FROM TIPO_DESCUENTO WHERE PK_id_descuento IN ({ids})",

        // Una cuenta del portal: nombre y apellido si los tiene cargados, si no su correo.
        AuditReferences.Usuario => @"
            SELECT CAST(PK_id_usuario AS BIGINT),
                   CASE WHEN LTRIM(RTRIM(ISNULL(apellido, ''))) = '' AND LTRIM(RTRIM(ISNULL(nombre, ''))) = ''
                        THEN email
                        ELSE LTRIM(RTRIM(ISNULL(apellido, ''))) + ', ' + LTRIM(RTRIM(ISNULL(nombre, ''))) END
            FROM USUARIO WHERE PK_id_usuario IN ({ids})",

        // Una bonificación, por su motivo y su valor: "Becado · 50 %" o "Descuento · $15.000".
        AuditReferences.Beneficio => @"
            SELECT CAST(jd.PK_id_jugador_descuento AS BIGINT),
                   td.tipo_descuento
                       + CASE WHEN jd.tipo_valor = '%' AND jd.porcentaje IS NOT NULL
                              THEN ' · ' + CONVERT(VARCHAR(12), CAST(jd.porcentaje AS DECIMAL(6, 2))) + ' %'
                              WHEN jd.monto_fijo IS NOT NULL
                              THEN ' · $' + CONVERT(VARCHAR(20), CAST(jd.monto_fijo AS DECIMAL(12, 2)))
                              ELSE '' END
            FROM JUGADORES_DESCUENTOS jd JOIN TIPO_DESCUENTO td ON td.PK_id_descuento = jd.FK_id_descuento
            WHERE jd.PK_id_jugador_descuento IN ({ids})",

        _ => null
    };
}
