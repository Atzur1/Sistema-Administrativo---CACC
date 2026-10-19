using System.Text.Json;
using EntityLibrary;

namespace DaoLibrary;

// La auditoría guarda cada cambio como una copia de la fila (JSON) donde los datos de otras tablas aparecen solo como
// número: {"idJugador":3,"idCategoria":5}. Acá se detecta a qué se refiere cada número para poder mostrar el nombre
// ("PRUEBA, NICOLAS", "AFA 20067") en vez del código. No toca la base: de eso se ocupa AuditReferenceResolver.
public static class AuditReferences
{
    public const string Jugador = "jugador";
    public const string Persona = "persona";
    public const string Categoria = "categoria";
    public const string Tipo = "tipo";
    public const string Usuario = "usuario";
    public const string Beneficio = "beneficio";

    // Qué tabla nombra cada campo numérico de la copia guardada. idTipo es el motivo de la bonificación
    // (TIPOS_DESCUENTO) y idDescuento, en un pago, la bonificación aplicada (JUGADORES_DESCUENTOS).
    private static readonly Dictionary<string, string> TipoPorCampo = new(StringComparer.Ordinal)
    {
        ["idJugador"] = Jugador,
        ["idPersona"] = Persona,
        ["idCategoria"] = Categoria,
        ["idTipo"] = Tipo,
        ["idUsuario"] = Usuario,
        ["idDescuento"] = Beneficio,
    };

    // La propia fila auditada también es una referencia: el evento de una ficha de jugador trae el número de ese jugador.
    private static readonly Dictionary<string, string> TipoPorEntidad = new(StringComparer.Ordinal)
    {
        ["JUGADORES"] = Jugador,
        ["PERSONAS"] = Persona,
        ["USUARIOS"] = Usuario,
        ["CATEGORIAS"] = Categoria,
        ["TIPOS_DESCUENTO"] = Tipo,
    };

    public static string Clave(string tipo, long id) => $"{tipo}:{id}";

    // Todo lo que un evento menciona por número, sin repetidos.
    public static IReadOnlySet<(string Tipo, long Id)> Recolectar(AuditEvent evento)
    {
        var referencias = new HashSet<(string, long)>();

        if (TipoPorEntidad.TryGetValue(evento.Entity, out string? tipoDeLaFila) && long.TryParse(evento.EntityId, out long idDeLaFila))
        {
            referencias.Add((tipoDeLaFila, idDeLaFila));
        }

        Agregar(referencias, evento.BeforeJson);
        Agregar(referencias, evento.AfterJson);
        return referencias;
    }

    private static void Agregar(HashSet<(string, long)> referencias, string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;

        try
        {
            using JsonDocument documento = JsonDocument.Parse(json);
            if (documento.RootElement.ValueKind != JsonValueKind.Object) return;

            foreach (JsonProperty campo in documento.RootElement.EnumerateObject())
            {
                if (TipoPorCampo.TryGetValue(campo.Name, out string? tipo)
                    && campo.Value.ValueKind == JsonValueKind.Number
                    && campo.Value.TryGetInt64(out long id))
                {
                    referencias.Add((tipo, id));
                }
            }
        }
        catch (JsonException)
        {
            // Una copia que no se puede leer simplemente no aporta referencias: el detalle sigue mostrando el número.
        }
    }
}
