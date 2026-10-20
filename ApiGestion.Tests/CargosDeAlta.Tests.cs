namespace ApiGestion.Tests;

using ApiGestion.Controllers;
using ApiGestion.Models;
using DaoLibrary;
using EntityLibrary;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceLibrary;

// Cargos de alta de un jugador que ya existe (el Portal Deportivo lo da de alta directo en la base y después avisa).
// Las reglas son las de JugadoresAltaService.Registrar: cuota solo desde el día 10, arancel de su categoría o género,
// inscripción de su género (en $0 no se cobra). Es idempotente y solo para altas recientes.
public class CargosDeAltaTests
{
    private static readonly DateTime Dia15 = new(2026, 10, 15);
    private static readonly DateTime Dia5 = new(2026, 10, 5);

    private static JugadorParaCargos Jugador(string genero = "Masculino", DateTime? alta = null, DateTime? baja = null)
        => new(7, genero, 3, alta ?? Dia15, baja);

    private static (JugadoresAltaService servicio, DaoFalso dao) Crear(JugadorParaCargos? jugador, DateTime hoy,
        decimal? inscripcionMasculina = 70_000m, decimal? inscripcionFemenina = 0m, decimal? cuota = 85_000m)
    {
        var dao = new DaoFalso(jugador);
        var servicio = new JugadoresAltaService(
            dao,
            new CategoriasFalso(),
            new InscripcionFalso(inscripcionMasculina, inscripcionFemenina),
            new ArancelesFalso(cuota),
            () => hoy);
        return (servicio, dao);
    }

    [Fact]
    public void UnVaronNuevo_RecibeLaInscripcionYLaCuotaDelMes()
    {
        var (servicio, dao) = Crear(Jugador(), Dia15);

        var resultado = servicio.GenerarCargosDeAlta(7);

        Assert.True(resultado.Exito);
        Assert.Equal(70_000m, dao.MontoInscripcion);
        Assert.Equal(85_000m, dao.MontoCuota);
        Assert.Equal(new DateTime(2026, 10, 1), dao.PrimerDiaDelMes);
        Assert.Equal(Dia15, dao.FechaInscripcion);
    }

    [Fact]
    public void UnaMujerConLaInscripcionEnCero_NoPagaInscripcionPeroSiLaCuota()
    {
        var (servicio, dao) = Crear(Jugador("Femenino"), Dia15, inscripcionFemenina: 0m, cuota: 70_000m);

        servicio.GenerarCargosDeAlta(7);

        Assert.Null(dao.MontoInscripcion);
        Assert.Equal(70_000m, dao.MontoCuota);
    }

    [Fact]
    public void AntesDelDia10_NoSeGeneraLaCuotaDelMes_PeroSiLaInscripcion()
    {
        var (servicio, dao) = Crear(Jugador(alta: Dia5), Dia5);

        servicio.GenerarCargosDeAlta(7);

        Assert.Null(dao.MontoCuota);
        Assert.Equal(70_000m, dao.MontoInscripcion);
    }

    [Fact]
    public void SinArancelDeCuotaVigente_NoSeGeneraLaCuota()
    {
        var (servicio, dao) = Crear(Jugador(), Dia15, cuota: null);

        servicio.GenerarCargosDeAlta(7);

        Assert.Null(dao.MontoCuota);
        Assert.Equal(70_000m, dao.MontoInscripcion);
    }

    [Fact]
    public void UnJugadorQueNoExiste_EsNoEncontrado_YNoSeGrabaNada()
    {
        var (servicio, dao) = Crear(null, Dia15);

        var resultado = servicio.GenerarCargosDeAlta(99);

        Assert.Equal(TipoResultado.NoEncontrado, resultado.Tipo);
        Assert.False(dao.Grabo);
    }

    [Fact]
    public void UnJugadorDadoDeBaja_NoRecibeCargos()
    {
        var (servicio, dao) = Crear(Jugador(baja: new DateTime(2026, 10, 14)), Dia15);

        var resultado = servicio.GenerarCargosDeAlta(7);

        Assert.Equal(TipoResultado.NoProcesable, resultado.Tipo);
        Assert.False(dao.Grabo);
    }

    // Esta operación completa un alta recién hecha en otro portal; no sirve para regularizar a jugadores viejos.
    [Theory]
    [InlineData(7, true)]
    [InlineData(8, false)]
    [InlineData(400, false)]
    public void SoloSeGeneranCargosParaAltasRecientes(int diasDeAntiguedad, bool permitido)
    {
        var (servicio, dao) = Crear(Jugador(alta: Dia15.AddDays(-diasDeAntiguedad)), Dia15);

        var resultado = servicio.GenerarCargosDeAlta(7);

        Assert.Equal(permitido, resultado.Exito);
        Assert.Equal(permitido, dao.Grabo);
    }

    // ===== El endpoint =====

    [Fact]
    public void ElEndpointDevuelveSoloSiSeGeneroCadaCargo_NuncaMontos()
    {
        var (servicio, _) = Crear(Jugador(), Dia15);
        var controlador = new IntegracionJugadoresController(servicio, NullLogger<IntegracionJugadoresController>.Instance);

        var ok = Assert.IsType<OkObjectResult>(controlador.CargosDeAlta(7));
        var dto = Assert.IsType<CargosAltaResponseDTO>(ok.Value);

        Assert.Equal(7, dto.IdJugador);
        Assert.True(dto.InscripcionGenerada);
        Assert.True(dto.CuotaGenerada);
        Assert.Equal(new[] { "CuotaGenerada", "IdJugador", "InscripcionGenerada" },
            typeof(CargosAltaResponseDTO).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray());
    }

    [Fact]
    public void ElEndpointDeUnJugadorInexistente_Es404ConMensaje()
    {
        // El Portal Deportivo distingue este 404 (trae mensaje) de una ruta que no existe (404 vacío).
        var (servicio, _) = Crear(null, Dia15);
        var controlador = new IntegracionJugadoresController(servicio, NullLogger<IntegracionJugadoresController>.Instance);

        var noEncontrado = Assert.IsType<NotFoundObjectResult>(controlador.CargosDeAlta(99));

        Assert.Contains("mensaje", System.Text.Json.JsonSerializer.Serialize(noEncontrado.Value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ElEndpointConUnIdInvalido_Es400(int id)
    {
        var (servicio, dao) = Crear(Jugador(), Dia15);
        var controlador = new IntegracionJugadoresController(servicio, NullLogger<IntegracionJugadoresController>.Instance);

        Assert.IsType<BadRequestObjectResult>(controlador.CargosDeAlta(id));
        Assert.False(dao.Grabo);
    }

    [Fact]
    public void ElEndpointSoloLoUsanLosRolesQueDanDeAltaAJugadores()
    {
        var roles = typeof(IntegracionJugadoresController)
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .Select(a => a.Roles)
            .Single();

        // El SuperAdmin del Administrativo y el Administrador (Tesorero) del Deportivo; no el médico, el DT ni Don QR.
        Assert.Equal("1,Administrador (Tesorero)", roles);
    }

    // ===== Falsos =====

    private sealed class DaoFalso : PlayerDAO
    {
        private readonly JugadorParaCargos? _jugador;
        public decimal? MontoCuota { get; private set; }
        public decimal? MontoInscripcion { get; private set; }
        public DateTime PrimerDiaDelMes { get; private set; }
        public DateTime FechaInscripcion { get; private set; }
        public bool Grabo { get; private set; }

        public DaoFalso(JugadorParaCargos? jugador) : base("") => _jugador = jugador;

        public override JugadorParaCargos? ObtenerParaCargos(int idJugador) => _jugador;

        public override CargosGenerados GenerarCargosDeAlta(int idJugador, decimal? montoCuota, DateTime primerDiaDelMes,
            decimal? montoInscripcion, DateTime fechaInscripcion)
        {
            Grabo = true;
            MontoCuota = montoCuota;
            MontoInscripcion = montoInscripcion;
            PrimerDiaDelMes = primerDiaDelMes;
            FechaInscripcion = fechaInscripcion;
            return new CargosGenerados(montoInscripcion != null, montoCuota != null);
        }
    }

    private sealed class InscripcionFalso : EnrollmentFeeDAO
    {
        private readonly decimal? _masculina;
        private readonly decimal? _femenina;

        public InscripcionFalso(decimal? masculina, decimal? femenina) : base("")
        {
            _masculina = masculina;
            _femenina = femenina;
        }

        public override EnrollmentFee? GetCurrentEnrollmentFee(string gender)
        {
            decimal? monto = gender == "Femenino" ? _femenina : _masculina;
            return monto == null ? null : new EnrollmentFee { Gender = gender, Amount = monto.Value };
        }
    }

    private sealed class ArancelesFalso : IArancelesDao
    {
        private readonly decimal? _cuota;
        public ArancelesFalso(decimal? cuota) => _cuota = cuota;

        public decimal? ObtenerMontoVigente(string genero, DateTime fecha) => _cuota;
        public decimal? ObtenerMontoVigente(string genero, int idCategoria, DateTime fecha) => _cuota;
        public IReadOnlyList<ArancelHistorialItem> ObtenerHistorial() => new List<ArancelHistorialItem>();
        public ArancelResumen ObtenerResumen() => new ArancelResumen();
        public void ProgramarArancel(string? genero, int? idCategoria, decimal monto, DateTime vigenteDesde) { }
    }

    private sealed class CategoriasFalso : ICategoriasDao
    {
        public IReadOnlyList<Categoria> ObtenerTodas() => new List<Categoria> { new() { IdCategoria = 3, Nombre = "AFA 20067" } };
    }
}

// El SQL de los cargos de alta, contra la base real y dentro de una transacción que se revierte.
[Collection(BaseDeDatosCollection.Nombre)]
public class CargosDeAltaIntegrationTests
{
    private static SqlConnection Abrir()
    {
        var conexion = new SqlConnection(Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable));
        conexion.Open();
        return conexion;
    }

    private static int CrearJugador(SqlConnection conexion, SqlTransaction transaccion)
    {
        long categoria;
        using (var comando = new SqlCommand("SELECT TOP (1) PK_id_categoria FROM CATEGORIAS ORDER BY PK_id_categoria", conexion, transaccion))
        {
            categoria = Convert.ToInt64(comando.ExecuteScalar());
        }

        // Igual que el alta del Portal Deportivo: la persona y el jugador directo en la base, sin cargos.
        var jugador = new Player
        {
            FirstName = "PRUEBA", LastName = "CARGOS", Dni = "99999998", BirthDate = new DateTime(2010, 1, 1),
            Gender = "Masculino", CategoryId = categoria, JoinDate = new DateTime(2026, 10, 15)
        };
        return PlayerDAO.InsertPlayer(conexion, transaccion, PlayerDAO.InsertPerson(conexion, transaccion, jugador), jugador);
    }

    private static int Cargos(SqlConnection conexion, SqlTransaction transaccion, int idJugador, string concepto)
    {
        using var comando = new SqlCommand("SELECT COUNT(*) FROM PAGOS WHERE FK_id_jugador = @id AND concepto = @c AND estado = 0", conexion, transaccion);
        comando.Parameters.AddWithValue("@id", idJugador);
        comando.Parameters.AddWithValue("@c", concepto);
        return Convert.ToInt32(comando.ExecuteScalar());
    }

    [IntegracionFact]
    public void LosCargosSeGrabanUnaSolaVez_AunqueSeAvisePorDuplicado()
    {
        using var conexion = Abrir();
        using var transaccion = conexion.BeginTransaction();
        try
        {
            int id = CrearJugador(conexion, transaccion);
            var mes = new DateTime(2026, 10, 1);

            var primera = PlayerDAO.GenerarCargos(conexion, transaccion, id, 85_000m, mes, 70_000m, new DateTime(2026, 10, 15));
            var segunda = PlayerDAO.GenerarCargos(conexion, transaccion, id, 85_000m, mes, 70_000m, new DateTime(2026, 10, 15));

            Assert.Equal(new CargosGenerados(true, true), primera);
            Assert.Equal(new CargosGenerados(false, false), segunda);
            Assert.Equal(1, Cargos(conexion, transaccion, id, "Cuota"));
            Assert.Equal(1, Cargos(conexion, transaccion, id, "Inscripcion"));
        }
        finally
        {
            transaccion.Rollback();
        }
    }

    [IntegracionFact]
    public void SinMontos_NoSeGrabaNada()
    {
        using var conexion = Abrir();
        using var transaccion = conexion.BeginTransaction();
        try
        {
            int id = CrearJugador(conexion, transaccion);

            var resultado = PlayerDAO.GenerarCargos(conexion, transaccion, id, null, new DateTime(2026, 10, 1), null, new DateTime(2026, 10, 15));

            Assert.Equal(new CargosGenerados(false, false), resultado);
            Assert.Equal(0, Cargos(conexion, transaccion, id, "Cuota"));
            Assert.Equal(0, Cargos(conexion, transaccion, id, "Inscripcion"));
        }
        finally
        {
            transaccion.Rollback();
        }
    }

    // El generador mensual y esta operación no se pisan: si la cuota del mes ya existe (por ejemplo la generó el proceso
    // automático), no se duplica.
    [IntegracionFact]
    public void SiLaCuotaDelMesYaExiste_NoSeDuplica_PeroLaInscripcionSiSeGenera()
    {
        using var conexion = Abrir();
        using var transaccion = conexion.BeginTransaction();
        try
        {
            int id = CrearJugador(conexion, transaccion);
            var mes = new DateTime(2026, 10, 1);
            PlayerDAO.InsertPendingCharge(conexion, transaccion, id, 85_000m, mes, "Cuota");

            var resultado = PlayerDAO.GenerarCargos(conexion, transaccion, id, 85_000m, mes, 70_000m, new DateTime(2026, 10, 15));

            Assert.Equal(new CargosGenerados(true, false), resultado);
            Assert.Equal(1, Cargos(conexion, transaccion, id, "Cuota"));
        }
        finally
        {
            transaccion.Rollback();
        }
    }
}
