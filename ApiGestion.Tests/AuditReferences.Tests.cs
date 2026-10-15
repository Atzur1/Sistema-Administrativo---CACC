namespace ApiGestion.Tests;

using ApiGestion.Models;
using DaoLibrary;
using EntityLibrary;
using Microsoft.Data.SqlClient;

// La auditoría guarda los datos de otras tablas solo como número ({"idJugador":3}). Estas pruebas cubren cómo se
// detecta a qué se refiere cada número y que el nombre llega hasta la API; la búsqueda del nombre en la base se
// prueba abajo contra SQL Server (en una transacción que se revierte).
public class AuditReferencesTests
{
    private static AuditEvent Evento(string entidad, string id, string? antes = null, string? despues = null) => new()
    {
        Id = 1, Entity = entidad, EntityId = id, Action = "UPDATE", TimestampUtc = DateTime.UtcNow,
        BeforeJson = antes, AfterJson = despues,
    };

    [Fact]
    public void DetectaLosNumerosQueElEventoMencionaYDeQueTablaSon()
    {
        var evento = Evento("PAGOS", "900",
            despues: "{\"idJugador\":3,\"idDescuento\":12,\"montoFinal\":5000,\"idCategoria\":null}");

        var referencias = AuditReferences.Recolectar(evento);

        Assert.Equivalent(new[] { (AuditReferences.Jugador, 3L), (AuditReferences.Beneficio, 12L) }, referencias);
    }

    [Fact]
    public void LaPropiaFilaAuditadaTambienEsUnaReferencia()
    {
        Assert.Contains((AuditReferences.Jugador, 7L), AuditReferences.Recolectar(Evento("JUGADORES", "7")));
        Assert.Contains((AuditReferences.Usuario, 4L), AuditReferences.Recolectar(Evento("USUARIO", "4")));
        Assert.Contains((AuditReferences.Categoria, 9L), AuditReferences.Recolectar(Evento("CATEGORIAS", "9")));
    }

    // El número de una fila de PAGOS es el pago, no un jugador: no se confunde con uno.
    [Fact]
    public void ElNumeroDeUnaFilaDePagosNoSeTomaPorUnJugador()
    {
        Assert.Empty(AuditReferences.Recolectar(Evento("PAGOS", "900")));
    }

    [Fact]
    public void JuntaLoDeAntesYLoDeDespuesSinRepetir()
    {
        var evento = Evento("JUGADORES_DESCUENTOS", "2012",
            antes: "{\"idJugador\":1,\"idTipo\":2}", despues: "{\"idJugador\":1,\"idTipo\":2}");

        var referencias = AuditReferences.Recolectar(evento);

        Assert.Equal(2, referencias.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("esto no es json")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"idJugador\":\"3\"}")]
    public void UnaCopiaQueNoSePuedeLeerNoAportaReferenciasNiRompe(string? json)
    {
        Assert.Empty(AuditReferences.Recolectar(Evento("PAGOS", "1", despues: json)));
    }

    [Fact]
    public void LaApiDevuelveLosNombresJuntoAlEvento()
    {
        var evento = Evento("PAGOS", "900", despues: "{\"idJugador\":3}");
        evento.Referencias = new Dictionary<string, string> { ["jugador:3"] = "PRUEBA, NICOLAS (DNI 99000001)" };

        var dto = AuditEventDTO.FromEntity(evento);

        Assert.Equal("PRUEBA, NICOLAS (DNI 99000001)", dto.References["jugador:3"]);
    }
}

// La base guarda la hora de cada evento en UTC. Si la API la manda sin la "Z" final, el navegador la toma por hora
// local y la muestra 3 horas adelantada (un pago de las 16:25 aparecía a las 19:25).
public class AuditTimestampTests
{
    [Fact]
    public void LaApiMandaLaHoraConLaZDeUtc()
    {
        var evento = new AuditEvent { Id = 1, Entity = "PAGOS", EntityId = "1", Action = "INSERT",
            TimestampUtc = new DateTime(2026, 10, 11, 18, 30, 56, DateTimeKind.Utc) };

        string json = System.Text.Json.JsonSerializer.Serialize(AuditEventDTO.FromEntity(evento));

        Assert.Contains("\"TimestampUtc\":\"2026-10-11T18:30:56Z\"", json);
    }

    [IntegracionFact]
    public void LosEventosLeidosDeLaBaseSalenMarcadosComoUtc()
    {
        var dao = new AuditDao(Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable)!);

        var (items, _) = dao.Search(null, null, null, null, null, null, 1, 10);

        Assert.NotEmpty(items);
        Assert.All(items, e => Assert.Equal(DateTimeKind.Utc, e.TimestampUtc.Kind));
    }
}

[Collection(BaseDeDatosCollection.Nombre)]
public class AuditReferenceResolverIntegrationTests
{
    [IntegracionFact]
    public void ResuelveJugadoresDivisionesYBeneficiosYDejaAfueraLoQueNoExiste()
    {
        using var connection = new SqlConnection(Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable));
        connection.Open();
        using SqlTransaction transaction = connection.BeginTransaction();

        try
        {
            using var categoria = new SqlCommand("SELECT TOP 1 PK_id_categoria, nombre_categoria FROM CATEGORIAS ORDER BY PK_id_categoria", connection, transaction);
            long idCategoria;
            string nombreCategoria;
            using (var lector = categoria.ExecuteReader())
            {
                lector.Read();
                idCategoria = Convert.ToInt64(lector[0]);
                nombreCategoria = lector.GetString(1).Trim();
            }

            var jugador = new Player
            {
                FirstName = "XQJUAN", LastName = "QWZARTEZ", Dni = "99887733", BirthDate = new DateTime(2010, 1, 1),
                Gender = "Masculino", CategoryId = idCategoria, JoinDate = new DateTime(2026, 10, 1)
            };
            int idPersona = PlayerDAO.InsertPerson(connection, transaction, jugador);
            long idJugador = PlayerDAO.InsertPlayer(connection, transaction, idPersona, jugador);

            var nombres = AuditReferenceResolver.Resolver(connection, transaction, new[]
            {
                (AuditReferences.Jugador, idJugador),
                (AuditReferences.Persona, (long)idPersona),
                (AuditReferences.Categoria, idCategoria),
                (AuditReferences.Jugador, 987654321L), // un jugador que no existe
            });

            Assert.Equal("QWZARTEZ, XQJUAN (DNI 99887733)", nombres[AuditReferences.Clave(AuditReferences.Jugador, idJugador)]);
            Assert.Equal("QWZARTEZ, XQJUAN (DNI 99887733)", nombres[AuditReferences.Clave(AuditReferences.Persona, idPersona)]);
            Assert.Equal(nombreCategoria, nombres[AuditReferences.Clave(AuditReferences.Categoria, idCategoria)]);
            Assert.DoesNotContain(AuditReferences.Clave(AuditReferences.Jugador, 987654321L), nombres.Keys);
        }
        finally
        {
            transaction.Rollback();
        }
    }

    // El camino completo del DAO: a cada evento le llegan solo los nombres de lo que ese evento menciona.
    [IntegracionFact]
    public void CadaEventoRecibeSoloLosNombresDeLoQueMenciona()
    {
        using var connection = new SqlConnection(Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable));
        connection.Open();
        using SqlTransaction transaction = connection.BeginTransaction();

        try
        {
            long idCategoria = Convert.ToInt64(new SqlCommand("SELECT TOP 1 PK_id_categoria FROM CATEGORIAS ORDER BY PK_id_categoria", connection, transaction).ExecuteScalar());
            var jugador = new Player
            {
                FirstName = "XQJUAN", LastName = "QWZARTEZ", Dni = "99887722", BirthDate = new DateTime(2010, 1, 1),
                Gender = "Masculino", CategoryId = idCategoria, JoinDate = new DateTime(2026, 10, 1)
            };
            long idJugador = PlayerDAO.InsertPlayer(connection, transaction, PlayerDAO.InsertPerson(connection, transaction, jugador), jugador);

            var delPago = new AuditEvent
            {
                Id = 1, Entity = "PAGOS", EntityId = "900", Action = "INSERT", TimestampUtc = DateTime.UtcNow,
                AfterJson = $"{{\"idJugador\":{idJugador},\"montoFinal\":5000}}"
            };
            var sinReferencias = new AuditEvent { Id = 2, Entity = "ARANCELES", EntityId = "5", Action = "INSERT", TimestampUtc = DateTime.UtcNow };

            AuditDao.AgregarReferencias(connection, transaction, new[] { delPago, sinReferencias });

            Assert.Equal("QWZARTEZ, XQJUAN (DNI 99887722)", delPago.Referencias[AuditReferences.Clave(AuditReferences.Jugador, idJugador)]);
            Assert.Empty(sinReferencias.Referencias);
        }
        finally
        {
            transaction.Rollback();
        }
    }

    [IntegracionFact]
    public void ResuelveCuentasDelPortalYElMotivoDeUnaBonificacion()
    {
        using var connection = new SqlConnection(Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable));
        connection.Open();
        using SqlTransaction transaction = connection.BeginTransaction();

        try
        {
            long idUsuario = Convert.ToInt64(new SqlCommand("SELECT TOP 1 PK_id_usuario FROM USUARIO ORDER BY PK_id_usuario", connection, transaction).ExecuteScalar());
            long idTipo = Convert.ToInt64(new SqlCommand("SELECT TOP 1 PK_id_descuento FROM TIPO_DESCUENTO ORDER BY PK_id_descuento", connection, transaction).ExecuteScalar());

            var nombres = AuditReferenceResolver.Resolver(connection, transaction, new[]
            {
                (AuditReferences.Usuario, idUsuario),
                (AuditReferences.Tipo, idTipo),
            });

            // Una cuenta siempre se puede nombrar: con su nombre y apellido o, si no los tiene cargados, con su correo.
            Assert.False(string.IsNullOrWhiteSpace(nombres[AuditReferences.Clave(AuditReferences.Usuario, idUsuario)]));
            Assert.False(string.IsNullOrWhiteSpace(nombres[AuditReferences.Clave(AuditReferences.Tipo, idTipo)]));
        }
        finally
        {
            transaction.Rollback();
        }
    }
}
