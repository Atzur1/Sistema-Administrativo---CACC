namespace ApiGestion.Tests;

using Microsoft.Data.SqlClient;

// Género y categoría obligatorios para todo jugador, contra la base real. De ellos sale el arancel que se le cobra, así que
// la base impide cargar un jugador incompleto sea cual sea el sistema que lo cargue. Cada caso corre en su propia
// transacción que se revierte: un THROW dentro de un trigger deja la transacción inutilizable, no se pueden encadenar.
[Collection(BaseDeDatosCollection.Nombre)]
public class JugadorGeneroCategoriaIntegrationTests
{
    private static string Conexion => Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable)!;

    private const int GeneroInvalido = 51010;
    private const int GeneroDeJugadorQuitado = 51011;
    private const int ColumnaNoAdmiteNulos = 515;

    private static string Dni() => "93" + Random.Shared.Next(100000, 999999);

    private static int Categoria(SqlConnection c, SqlTransaction t)
    {
        using var cmd = new SqlCommand("SELECT TOP (1) PK_id_categoria FROM CATEGORIAS ORDER BY PK_id_categoria", c, t);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int Persona(SqlConnection c, SqlTransaction t, string? genero)
    {
        using var cmd = new SqlCommand(
            "INSERT INTO PERSONAS (nombre, apellido, dni, genero) VALUES (N'Prueba', N'Obligatorios', @dni, @genero); SELECT CAST(SCOPE_IDENTITY() AS INT);", c, t);
        cmd.Parameters.AddWithValue("@dni", Dni());
        cmd.Parameters.AddWithValue("@genero", (object?)genero ?? DBNull.Value);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void Jugador(SqlConnection c, SqlTransaction t, object? persona, object? categoria)
    {
        using var cmd = new SqlCommand("INSERT INTO JUGADORES (FK_id_persona, FK_id_categoria) VALUES (@p, @c)", c, t);
        cmd.Parameters.AddWithValue("@p", persona ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@c", categoria ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    // Abre una transacción, corre el caso y la revierte siempre.
    private static void EnTransaccion(Action<SqlConnection, SqlTransaction> caso)
    {
        using var connection = new SqlConnection(Conexion);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try { caso(connection, transaction); }
        finally { try { transaction.Rollback(); } catch (InvalidOperationException) { /* ya revertida por el THROW del trigger */ } }
    }

    [IntegracionTheory]
    [InlineData("Masculino")]
    [InlineData("Femenino")]
    [InlineData(" Masculino ")]
    public void UnJugadorConGeneroYCategoria_SeCarga(string genero)
    {
        EnTransaccion((c, t) => Jugador(c, t, Persona(c, t, genero), Categoria(c, t)));
    }

    private static string? GeneroGuardado(SqlConnection c, SqlTransaction t, int persona)
    {
        using var cmd = new SqlCommand("SELECT genero FROM PERSONAS WHERE PK_id_persona = @id", c, t);
        cmd.Parameters.AddWithValue("@id", persona);
        return cmd.ExecuteScalar() as string;
    }

    // HU-112: el genero se guarda siempre como "Masculino" o "Femenino", llegue como llegue.
    [IntegracionTheory]
    [InlineData("masculino ", "Masculino")]
    [InlineData(" MASCULINO", "Masculino")]
    [InlineData("femenino", "Femenino")]
    [InlineData("  Femenino  ", "Femenino")]
    [InlineData("Masculino", "Masculino")]
    public void ElGeneroSeGuardaNormalizado(string recibido, string esperado)
    {
        EnTransaccion((c, t) =>
        {
            int persona = Persona(c, t, recibido);
            Assert.Equal(esperado, GeneroGuardado(c, t, persona));
        });
    }

    [IntegracionFact]
    public void ElCambioDeGeneroTambienSeNormaliza()
    {
        EnTransaccion((c, t) =>
        {
            int persona = Persona(c, t, "Masculino");
            using (var cmd = new SqlCommand("UPDATE PERSONAS SET genero = N'femenino ' WHERE PK_id_persona = @id", c, t))
            {
                cmd.Parameters.AddWithValue("@id", persona);
                cmd.ExecuteNonQuery();
            }

            Assert.Equal("Femenino", GeneroGuardado(c, t, persona));
        });
    }

    [IntegracionFact]
    public void UnGeneroQueNoEsNingunoDeLosDos_NoSeToca()
    {
        EnTransaccion((c, t) =>
        {
            int persona = Persona(c, t, "No especificado");
            Assert.Equal("No especificado", GeneroGuardado(c, t, persona));
        });
    }

    [IntegracionFact]
    public void SinCategoria_LaBaseRechazaElJugador()
    {
        EnTransaccion((c, t) =>
        {
            int persona = Persona(c, t, "Masculino");
            var error = Assert.Throws<SqlException>(() => Jugador(c, t, persona, null));
            Assert.Equal(ColumnaNoAdmiteNulos, error.Number);
        });
    }

    [IntegracionTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("No especificado")]
    [InlineData("masculino x")]
    public void SinUnGeneroValido_LaBaseRechazaElJugador(string? genero)
    {
        EnTransaccion((c, t) =>
        {
            int persona = Persona(c, t, genero);
            var error = Assert.Throws<SqlException>(() => Jugador(c, t, persona, Categoria(c, t)));
            Assert.Equal(GeneroInvalido, error.Number);
            Assert.Contains("genero", error.Message);
        });
    }

    [IntegracionFact]
    public void UnJugadorSinPersona_LaBaseLoRechaza()
    {
        EnTransaccion((c, t) =>
        {
            var error = Assert.Throws<SqlException>(() => Jugador(c, t, null, Categoria(c, t)));
            Assert.Equal(GeneroInvalido, error.Number);
        });
    }

    [IntegracionFact]
    public void NoSeLeQuitaElGeneroAUnaPersonaQueEsJugador()
    {
        EnTransaccion((c, t) =>
        {
            int persona = Persona(c, t, "Femenino");
            Jugador(c, t, persona, Categoria(c, t));

            using var cmd = new SqlCommand("UPDATE PERSONAS SET genero = NULL WHERE PK_id_persona = @p", c, t);
            cmd.Parameters.AddWithValue("@p", persona);
            var error = Assert.Throws<SqlException>(() => cmd.ExecuteNonQuery());
            Assert.Equal(GeneroDeJugadorQuitado, error.Number);
        });
    }

    [IntegracionFact]
    public void ElGeneroDeUnJugadorSePuedeCorregirPorOtroValido()
    {
        EnTransaccion((c, t) =>
        {
            int persona = Persona(c, t, "Masculino");
            Jugador(c, t, persona, Categoria(c, t));

            using var cmd = new SqlCommand("UPDATE PERSONAS SET genero = N'Femenino' WHERE PK_id_persona = @p", c, t);
            cmd.Parameters.AddWithValue("@p", persona);
            Assert.Equal(1, cmd.ExecuteNonQuery());
        });
    }

    // El Portal Deportivo carga al personal con "No especificado": la regla alcanza solo a quienes son jugadores.
    [IntegracionFact]
    public void QuienNoEsJugador_PuedeTenerCualquierGeneroOCarecerDeEl()
    {
        EnTransaccion((c, t) =>
        {
            int personal = Persona(c, t, "No especificado");
            using var cmd = new SqlCommand("UPDATE PERSONAS SET genero = NULL WHERE PK_id_persona = @p", c, t);
            cmd.Parameters.AddWithValue("@p", personal);
            Assert.Equal(1, cmd.ExecuteNonQuery());
        });
    }

    [IntegracionFact]
    public void EditarOtrosDatosDelJugador_NoVuelveAPedirElGenero()
    {
        EnTransaccion((c, t) =>
        {
            int persona = Persona(c, t, "Masculino");
            Jugador(c, t, persona, Categoria(c, t));

            using var cmd = new SqlCommand("UPDATE JUGADORES SET fecha_baja = @b WHERE FK_id_persona = @p", c, t);
            cmd.Parameters.AddWithValue("@b", new DateTime(2099, 1, 15)); // no anterior al alta (que toma la fecha de hoy)
            cmd.Parameters.AddWithValue("@p", persona);
            Assert.Equal(1, cmd.ExecuteNonQuery());
        });
    }
}
