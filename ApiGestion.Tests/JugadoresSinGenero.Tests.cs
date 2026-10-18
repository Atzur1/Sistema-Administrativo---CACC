namespace ApiGestion.Tests;

using System.Reflection;
using ApiGestion.Controllers;
using DaoLibrary;
using EntityLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;

// Aviso del Resumen General: jugadores sin género. El controlador solo informa; el SQL se prueba contra la base real.
public class JugadoresSinGeneroControllerTests
{
    private sealed class FakeDao : IJugadoresSinGeneroDao
    {
        public JugadoresSinGenero Resultado { get; set; } = new(0, new List<JugadorSinGenero>());
        public int MaximoPedido { get; private set; }
        public bool Falla { get; set; }

        public JugadoresSinGenero Obtener(int maximo)
        {
            if (Falla) throw new InvalidOperationException("base caída");
            MaximoPedido = maximo;
            return Resultado;
        }
    }

    [Fact]
    public void SinJugadoresSinGenero_ResponderConTotalCero()
    {
        var controller = new JugadoresSinGeneroController(new FakeDao(), NullLogger<JugadoresSinGeneroController>.Instance);

        var ok = Assert.IsType<OkObjectResult>(controller.Obtener());

        Assert.Contains("\"total\":0", System.Text.Json.JsonSerializer.Serialize(ok.Value));
    }

    [Fact]
    public void ListaElTotalYLosPrimeros_ConLosNombresQueUsaLaPantalla()
    {
        var dao = new FakeDao { Resultado = new(250, new List<JugadorSinGenero> { new(7, "Pérez, Juan", "41000001", "Pt  AFA 2009") }) };
        var controller = new JugadoresSinGeneroController(dao, NullLogger<JugadoresSinGeneroController>.Instance);

        var ok = Assert.IsType<OkObjectResult>(controller.Obtener());
        string json = System.Text.Json.JsonSerializer.Serialize(ok.Value);

        Assert.Equal(JugadoresSinGeneroController.MaximoListado, dao.MaximoPedido);
        Assert.Contains("\"total\":250", json);
        Assert.Contains("\"nombreCompleto\":\"Pérez, Juan\"", json.Replace("\\u00E9", "é"));
        Assert.Contains("\"dni\":\"41000001\"", json);
    }

    [Fact]
    public void SiLaBaseFalla_Devuelve500SinFiltrarElError()
    {
        var controller = new JugadoresSinGeneroController(new FakeDao { Falla = true }, NullLogger<JugadoresSinGeneroController>.Instance);

        var resultado = Assert.IsType<ObjectResult>(controller.Obtener());

        Assert.Equal(500, resultado.StatusCode);
        Assert.DoesNotContain("base caída", System.Text.Json.JsonSerializer.Serialize(resultado.Value));
    }

    [Fact]
    public void SoloLoVeElSuperAdmin()
    {
        var roles = typeof(JugadoresSinGeneroController).GetCustomAttribute<AuthorizeAttribute>()!.Roles;

        Assert.Equal("1", roles);
    }
}

[Collection(BaseDeDatosCollection.Nombre)]
public class JugadoresSinGeneroDaoIntegrationTests
{
    private static string Conexion => Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable)!;

    private static int Jugador(SqlConnection c, SqlTransaction t, string? genero, bool dadoDeBaja)
    {
        int categoria;
        using (var cmd = new SqlCommand("SELECT TOP (1) PK_id_categoria FROM CATEGORIAS ORDER BY PK_id_categoria", c, t))
            categoria = Convert.ToInt32(cmd.ExecuteScalar());
        var player = new Player
        {
            FirstName = "Prueba", LastName = "SinGenero", Dni = "96" + Random.Shared.Next(100000, 999999), BirthDate = new DateTime(2010, 1, 1),
            Gender = genero ?? "Masculino", CategoryId = categoria, JoinDate = new DateTime(2020, 1, 1)
        };
        int persona = PlayerDAO.InsertPerson(c, t, player);
        if (genero is null)
        {
            using var cmd = new SqlCommand("UPDATE PERSONAS SET genero = NULL WHERE PK_id_persona = @p", c, t);
            cmd.Parameters.AddWithValue("@p", persona);
            cmd.ExecuteNonQuery();
        }
        int jugador = PlayerDAO.InsertPlayer(c, t, persona, player);
        if (dadoDeBaja)
        {
            using var cmd = new SqlCommand("UPDATE JUGADORES SET fecha_baja = @b WHERE PK_id_jugador = @j", c, t);
            cmd.Parameters.AddWithValue("@b", RelojNegocio.Hoy);
            cmd.Parameters.AddWithValue("@j", jugador);
            cmd.ExecuteNonQuery();
        }
        return jugador;
    }

    [IntegracionFact]
    public void ListaALosJugadoresEnElClubSinGeneroValido_YNoAlResto()
    {
        using var connection = new SqlConnection(Conexion);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            int sinGenero = Jugador(connection, transaction, null, dadoDeBaja: false);
            int conOtroTexto = Jugador(connection, transaction, "Otro", dadoDeBaja: false);
            int masculino = Jugador(connection, transaction, "Masculino", dadoDeBaja: false);
            int femenina = Jugador(connection, transaction, "Femenino", dadoDeBaja: false);
            int sinGeneroPeroDeBaja = Jugador(connection, transaction, null, dadoDeBaja: true);

            var resultado = JugadoresSinGeneroDao.ObtenerCore(connection, transaction, 10_000);
            var ids = resultado.Primeros.Select(j => j.IdJugador).ToHashSet();

            Assert.Contains(sinGenero, ids);
            Assert.Contains(conOtroTexto, ids);
            Assert.DoesNotContain(masculino, ids);
            Assert.DoesNotContain(femenina, ids);
            Assert.DoesNotContain(sinGeneroPeroDeBaja, ids); // ya no recibe cuotas
            Assert.True(resultado.Total >= 2);
            Assert.Equal(resultado.Total, resultado.Primeros.Count); // sin tope no se recorta nada

            var recortado = JugadoresSinGeneroDao.ObtenerCore(connection, transaction, 1);
            Assert.Single(recortado.Primeros);
            Assert.Equal(resultado.Total, recortado.Total); // el total es el real aunque la lista se recorte
        }
        finally { transaction.Rollback(); }
    }
}
