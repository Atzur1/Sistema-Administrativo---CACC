namespace ApiGestion.Tests;

using DaoLibrary;
using DaoLibrary.Exceptions;
using EntityLibrary;
using ServiceLibrary;

// Reglas de ArancelesService: el arancel es por género O por categoría, hay uno solo por mes y destino,
// y un arancel se puede cancelar (con sus cuotas pendientes sin pagos) del mes en curso en adelante.
// HU-106: el cambio se simula (sin grabar nada) y se confirma con la huella de esa simulación; el impacto se vuelve a
// calcular al grabar y, si cambió, no se graba.
// Hoy simulado: 3 de octubre de 2026.
public class ArancelesServiceTests
{
    private static readonly DateTime Hoy = new(2026, 10, 3);

    private static (ArancelesService service, FakeArancelesDao aranceles, FakeCuotasDao cuotas) Create(DateTime? hoy = null)
    {
        var cuotas = new FakeCuotasDao();
        var aranceles = new FakeArancelesDao(cuotas);
        var service = new ArancelesService(aranceles, new FakeTariffTransactionDao(aranceles, cuotas), new FakeCategoriasDao(), () => hoy ?? Hoy);
        return (service, aranceles, cuotas);
    }

    private static CambioArancel Cambio(string? genero, int? idCategoria, decimal valor = 50000, DateTime? desde = null,
        TipoCambioArancel tipo = TipoCambioArancel.Absoluto) => new()
    {
        Genero = genero,
        IdCategoria = idCategoria,
        Tipo = tipo,
        Valor = valor,
        VigenteDesde = desde ?? Hoy
    };

    // Lo que hace el portal: simula, el SuperAdmin revisa y confirma con la huella (y la cantidad, si hace falta).
    // Las lecturas de la simulación se descartan para que las pruebas vean solo lo que pasa al confirmar.
    private static ProgramarArancelResultado SimularYConfirmar(ArancelesService service, FakeCuotasDao cuotas, CambioArancel cambio)
    {
        ImpactoArancel impacto = service.SimularArancel(cambio);
        cuotas.Llamadas.Clear();
        return service.ProgramarArancel(Confirmacion(cambio, impacto.Huella, impacto.Jugadores));
    }

    private static ProgramarArancelRequest Confirmacion(CambioArancel cambio, string huella, int? jugadores = null) => new()
    {
        Genero = cambio.Genero,
        IdCategoria = cambio.IdCategoria,
        Tipo = cambio.Tipo,
        Valor = cambio.Valor,
        VigenteDesde = cambio.VigenteDesde,
        Huella = huella,
        JugadoresConfirmados = jugadores
    };

    private static ImpactoArancelLinea Linea(int idCategoria, string nombre, decimal? actual, int jugadores, string genero = "Masculino") => new()
    {
        IdCategoria = idCategoria,
        NombreCategoria = nombre,
        Genero = genero,
        MontoActual = actual,
        Jugadores = jugadores
    };

    // ===== Validaciones (corren antes de tocar la base) =====

    [Fact]
    public void ProgramarArancel_WithGenderAndCategory_IsRefused()
    {
        var ex = Assert.Throws<ArancelInvalidoException>(() => Create().service.SimularArancel(Cambio("Masculino", 1)));
        Assert.Contains("género o por categoría", ex.Message);
        Assert.Throws<ArancelInvalidoException>(() => Create().service.ProgramarArancel(Confirmacion(Cambio("Masculino", 1), "x")));
    }

    [Fact]
    public void ProgramarArancel_WithNeitherGenderNorCategory_IsRefused()
    {
        Assert.Throws<ArancelInvalidoException>(() => Create().service.SimularArancel(Cambio(null, null)));
        Assert.Throws<ArancelInvalidoException>(() => Create().service.SimularArancel(Cambio("  ", null)));
    }

    [Fact]
    public void ProgramarArancel_WithAnInvalidGender_IsRefused()
    {
        Assert.Throws<ArancelInvalidoException>(() => Create().service.SimularArancel(Cambio("Otro", null)));
    }

    [Fact]
    public void ProgramarArancel_WithAnUnknownCategory_IsRefused()
    {
        var ex = Assert.Throws<ArancelInvalidoException>(() => Create().service.SimularArancel(Cambio(null, 999)));
        // HU-090: mensaje de dominio, sin el id interno de la categoría.
        Assert.Equal("La categoría elegida no existe.", ex.Message);
        Assert.DoesNotContain("999", ex.Message);
    }

    [Fact]
    public void ProgramarArancel_WithANonPositiveAmount_IsRefused()
    {
        Assert.Throws<ArancelInvalidoException>(() => Create().service.SimularArancel(Cambio("Masculino", null, 0)));
        Assert.Throws<ArancelInvalidoException>(() => Create().service.SimularArancel(Cambio(null, 1, -5)));
    }

    [Fact]
    public void ProgramarArancel_WithoutTheSimulationFingerprint_IsRefusedAndChangesNothing()
    {
        var (service, aranceles, cuotas) = Create();

        var ex = Assert.Throws<ArancelInvalidoException>(() => service.ProgramarArancel(Confirmacion(Cambio("Masculino", null), "")));

        Assert.Contains("Revisá el impacto", ex.Message);
        Assert.Empty(aranceles.Programados);
        Assert.Empty(cuotas.Llamadas);
    }

    // ===== HU-069 en el backend: cómo se calcula el monto nuevo =====

    [Theory]
    [InlineData(85000, 10, 93500)]       // el ejemplo de la HU
    [InlineData(85000, 12.5, 95625)]
    [InlineData(1005, 10, 1106)]         // 1105,5: la mitad redondea hacia arriba, como Math.round del portal
    [InlineData(85555, 10, 94110.5)]     // casos que probaba el portal antes de HU-106: 94.111
    [InlineData(85333, 10, 93866.3)]     // 93.866
    [InlineData(61999, 7.25, 66493.9275)]
    [InlineData(85000, 0.01, 85009)]     // el mínimo que se puede escribir (85008,5 -> 85009)
    [InlineData(1000, 999.99, 10999.9)]  // el máximo: 10999,9 -> 11000 (ver abajo)
    public void CalcularMontoNuevo_Percentage_RoundsToTheWholePeso(decimal base_, decimal porcentaje, decimal sinRedondear)
    {
        decimal monto = ArancelesService.CalcularMontoNuevo(TipoCambioArancel.Porcentaje, porcentaje, base_);

        Assert.Equal(Math.Round(sinRedondear, 0, MidpointRounding.AwayFromZero), monto);
        Assert.Equal(decimal.Truncate(monto), monto);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1000)]       // más de 999,99
    [InlineData(12.345)]     // más de dos decimales
    public void CalcularMontoNuevo_Percentage_OutOfTheHU069Limits_IsRefused(decimal porcentaje)
    {
        Assert.Throws<ArancelInvalidoException>(() => ArancelesService.CalcularMontoNuevo(TipoCambioArancel.Porcentaje, porcentaje, 85000));
    }

    [Fact]
    public void CalcularMontoNuevo_FixedAmount_AddsToTheCurrentTariff()
    {
        Assert.Equal(90000, ArancelesService.CalcularMontoNuevo(TipoCambioArancel.MontoFijo, 5000, 85000));
        Assert.Throws<ArancelInvalidoException>(() => ArancelesService.CalcularMontoNuevo(TipoCambioArancel.MontoFijo, 0, 85000));
        Assert.Throws<ArancelInvalidoException>(() => ArancelesService.CalcularMontoNuevo(TipoCambioArancel.MontoFijo, 10.005m, 85000));
    }

    [Fact]
    public void CalcularMontoNuevo_Absolute_IsTheTypedAmount()
    {
        Assert.Equal(92000, ArancelesService.CalcularMontoNuevo(TipoCambioArancel.Absoluto, 92000, null));
        Assert.Equal(92000, ArancelesService.CalcularMontoNuevo(TipoCambioArancel.Absoluto, 92000, 85000));
    }

    [Theory]
    [InlineData(TipoCambioArancel.Porcentaje)]
    [InlineData(TipoCambioArancel.MontoFijo)]
    public void CalcularMontoNuevo_RelativeChangeWithoutACurrentTariff_AsksForAnAbsoluteAmount(TipoCambioArancel tipo)
    {
        var ex = Assert.Throws<ArancelInvalidoException>(() => ArancelesService.CalcularMontoNuevo(tipo, 10, null, "El género Femenino"));

        Assert.Contains("El género Femenino no tiene un arancel vigente", ex.Message);
        Assert.Contains("Monto absoluto", ex.Message);
    }

    // ===== HU-106: simulación =====

    [Fact]
    public void SimularArancel_ShowsTheImpactPerCategoryAndWritesNothing()
    {
        var (service, aranceles, cuotas) = Create();
        aranceles.MontoVigente = 85000;
        aranceles.Lineas = new() { Linea(2, "Sub 15", 85000, 20), Linea(1, "Sub 17", 60000, 15) };

        ImpactoArancel impacto = service.SimularArancel(Cambio("Masculino", null, 10, new DateTime(2026, 11, 1), TipoCambioArancel.Porcentaje));

        Assert.Equal(85000, impacto.MontoBase);
        Assert.Equal(93500, impacto.MontoNuevo);
        Assert.Equal(93500, aranceles.ConsultadoConMonto); // la consulta de jugadores usa el monto que calculó el backend
        Assert.Equal(35, impacto.Jugadores);
        Assert.Equal(2, impacto.Categorias);
        Assert.Equal(20 * 8500 + 15 * 33500, impacto.DiferenciaMensual);
        Assert.True(impacto.RequiereConfirmacionReforzada);
        Assert.Equal(new DateTime(2026, 11, 10), impacto.SeEmiteElDia);
        Assert.False(impacto.MesEnCurso);
        Assert.NotEmpty(impacto.Huella);
        // Nada grabado ni tocado: solo lecturas.
        Assert.Empty(aranceles.Programados);
        Assert.DoesNotContain(cuotas.Llamadas, l => l.Operacion is "eliminar" or "generar");
    }

    [Fact]
    public void SimularArancel_ForTheCurrentMonth_TellsWhatHappensToThisMonthsCuotas()
    {
        var (service, aranceles, cuotas) = Create(new DateTime(2026, 10, 12));
        aranceles.MontoVigente = 85000;
        aranceles.Lineas = new() { Linea(1, "Sub 17", 85000, 8) };
        cuotas.ConPagos = 2;
        cuotas.Pendientes = 6;

        ImpactoArancel impacto = service.SimularArancel(Cambio(null, 1, 90000, new DateTime(2026, 10, 12)));

        Assert.True(impacto.MesEnCurso);
        Assert.Equal(6, impacto.CuotasDelMesPendientesSinPagos);
        Assert.Equal(2, impacto.CuotasDelMesConPagos);
        Assert.Null(impacto.SeEmiteElDia); // desde el 10: se vuelven a emitir al confirmar
        Assert.DoesNotContain(cuotas.Llamadas, l => l.Operacion is "eliminar" or "generar");
    }

    [Fact]
    public void SimularArancel_WithZeroPlayers_ShowsZeroAndNoMoney()
    {
        var (service, aranceles, _) = Create();
        aranceles.Lineas = new();

        ImpactoArancel impacto = service.SimularArancel(Cambio(null, 2, 50000, new DateTime(2026, 11, 1)));

        Assert.Equal(0, impacto.Jugadores);
        Assert.Equal(0, impacto.DiferenciaMensual);
        Assert.False(impacto.RequiereConfirmacionReforzada); // la regla es por categorías, no por jugadores
    }

    [Fact]
    public void Huella_IsTheSameForTheSameImpactAndChangesWithAnyApprovedNumber()
    {
        var (service, aranceles, _) = Create();
        aranceles.MontoVigente = 85000;
        aranceles.Lineas = new() { Linea(1, "Sub 17", 85000, 8) };
        var cambio = Cambio(null, 1, 10, new DateTime(2026, 11, 1), TipoCambioArancel.Porcentaje);

        string primera = service.SimularArancel(cambio).Huella;
        Assert.Equal(primera, service.SimularArancel(cambio).Huella);

        aranceles.Lineas = new() { Linea(1, "Sub 17", 85000, 9) };
        Assert.NotEqual(primera, service.SimularArancel(cambio).Huella);

        aranceles.Lineas = new() { Linea(1, "Sub 17", 85000, 8) };
        Assert.NotEqual(primera, service.SimularArancel(Cambio(null, 1, 11, new DateTime(2026, 11, 1), TipoCambioArancel.Porcentaje)).Huella);
    }

    // ===== HU-106: confirmación =====

    [Fact]
    public void ProgramarArancel_RecalculatesTheImpactAndSavesTheApprovedAmountWithItsImpactForTheAudit()
    {
        var (service, aranceles, cuotas) = Create();
        aranceles.MontoVigente = 85000;
        aranceles.Lineas = new() { Linea(1, "Sub 17", 85000, 8) };

        var resultado = SimularYConfirmar(service, cuotas, Cambio(null, 1, 5000, new DateTime(2026, 11, 1), TipoCambioArancel.MontoFijo));

        var programado = Assert.Single(aranceles.Programados);
        Assert.Equal(90000, programado.Monto);
        Assert.NotNull(programado.Impacto);
        Assert.Equal(TipoCambioArancel.MontoFijo, programado.Impacto!.Tipo);
        Assert.Equal(5000, programado.Impacto.Valor);
        Assert.Equal(85000, programado.Impacto.MontoBase);
        Assert.Equal(8, programado.Impacto.Jugadores);
        Assert.Equal(40000, programado.Impacto.DiferenciaMensual);
        Assert.Same(programado.Impacto, resultado.Impacto);
    }

    [Fact]
    public void ProgramarArancel_WhenTheImpactChangedSinceTheSimulation_IsRefusedWithTheNewImpactAndChangesNothing()
    {
        var (service, aranceles, cuotas) = Create();
        aranceles.MontoVigente = 85000;
        aranceles.Lineas = new() { Linea(1, "Sub 17", 85000, 8) };
        var cambio = Cambio(null, 1, 10, new DateTime(2026, 11, 1), TipoCambioArancel.Porcentaje);
        ImpactoArancel aprobado = service.SimularArancel(cambio);

        // Entre la simulación y la confirmación entran dos jugadores a la categoría.
        aranceles.Lineas = new() { Linea(1, "Sub 17", 85000, 10) };
        var ex = Assert.Throws<ImpactoDesactualizadoException>(() => service.ProgramarArancel(Confirmacion(cambio, aprobado.Huella, 8)));

        Assert.Equal(10, ex.ImpactoActual.Jugadores);
        Assert.NotEqual(aprobado.Huella, ex.ImpactoActual.Huella);
        Assert.Empty(aranceles.Programados);
        Assert.DoesNotContain(cuotas.Llamadas, l => l.Operacion is "eliminar" or "generar");
    }

    [Fact]
    public void ProgramarArancel_WhenTheCurrentTariffChangedSinceTheSimulation_IsRefused()
    {
        var (service, aranceles, _) = Create();
        aranceles.MontoVigente = 85000;
        aranceles.Lineas = new() { Linea(1, "Sub 17", 85000, 8) };
        var cambio = Cambio(null, 1, 10, new DateTime(2026, 11, 1), TipoCambioArancel.Porcentaje);
        string huella = service.SimularArancel(cambio).Huella;

        aranceles.MontoVigente = 87000; // el 10 % ahora daría otro monto

        var ex = Assert.Throws<ImpactoDesactualizadoException>(() => service.ProgramarArancel(Confirmacion(cambio, huella)));
        Assert.Equal(95700, ex.ImpactoActual.MontoNuevo);
        Assert.Empty(aranceles.Programados);
    }

    [Fact]
    public void ProgramarArancel_ForMoreThanOneCategory_RequiresTypingTheExactNumberOfPlayers()
    {
        var (service, aranceles, _) = Create();
        aranceles.MontoVigente = 85000;
        aranceles.Lineas = new() { Linea(2, "Sub 15", 85000, 20), Linea(1, "Sub 17", 85000, 22) };
        var cambio = Cambio("Masculino", null, 10, new DateTime(2026, 11, 1), TipoCambioArancel.Porcentaje);
        string huella = service.SimularArancel(cambio).Huella;

        foreach (int? escrito in new int?[] { null, 0, 41, 43 })
        {
            var ex = Assert.Throws<ArancelInvalidoException>(() => service.ProgramarArancel(Confirmacion(cambio, huella, escrito)));
            Assert.Contains("(42)", ex.Message);
        }
        Assert.Empty(aranceles.Programados);

        service.ProgramarArancel(Confirmacion(cambio, huella, 42));
        Assert.Single(aranceles.Programados);
    }

    [Fact]
    public void ProgramarArancel_ForASingleCategory_DoesNotRequireTypingTheNumber()
    {
        var (service, aranceles, _) = Create();
        aranceles.Lineas = new() { Linea(1, "Sub 17", 85000, 8) };
        var cambio = Cambio(null, 1, 90000, new DateTime(2026, 11, 1));
        string huella = service.SimularArancel(cambio).Huella;

        service.ProgramarArancel(Confirmacion(cambio, huella));

        Assert.Single(aranceles.Programados);
    }

    // ===== Un solo arancel por mes y destino =====

    [Fact]
    public void ProgramarArancel_WhenTheSameDestinationAlreadyHasOneThatMonth_IsRefusedAndChangesNothing()
    {
        var (service, aranceles, cuotas) = Create();
        aranceles.ExistenteEnMes = new DateTime(2026, 10, 2);

        var ex = Assert.Throws<ArancelInvalidoException>(() => service.SimularArancel(Cambio("Masculino", null, 11000, new DateTime(2026, 10, 20))));
        Assert.Throws<ArancelInvalidoException>(() => service.ProgramarArancel(Confirmacion(Cambio("Masculino", null, 11000, new DateTime(2026, 10, 20)), "x")));

        Assert.Contains("02/10/2026", ex.Message);
        Assert.Contains("octubre de 2026", ex.Message);
        Assert.Contains("Cancelalo primero", ex.Message);
        Assert.Empty(aranceles.Programados);
        Assert.Empty(cuotas.Llamadas);
    }

    [Fact]
    public void ProgramarArancel_NamesTheCategoryWhenTheDestinationIsACategory()
    {
        var (service, aranceles, _) = Create();
        aranceles.ExistenteEnMes = new DateTime(2026, 10, 3);

        var ex = Assert.Throws<ArancelInvalidoException>(() => service.SimularArancel(Cambio(null, 1)));

        Assert.Contains("La categoría Sub 17", ex.Message);
    }

    // ===== Qué pasa con las cuotas al programar =====

    // Desde el día 10 la cuota del mes ya se generó: se vuelve a emitir con el monto nuevo.
    [Fact]
    public void ProgramarArancel_ForTheCurrentMonthFromThe10th_ReissuesThePendingCuotasWithoutPaymentsAndKeepsThePaidOnes()
    {
        var (service, aranceles, cuotas) = Create(new DateTime(2026, 10, 10));
        cuotas.ConPagos = 2;
        cuotas.Pendientes = 6;

        var resultado = SimularYConfirmar(service, cuotas, Cambio(null, 1, 100000, new DateTime(2026, 10, 10)));

        Assert.Single(aranceles.Programados);
        Assert.Equal(6, resultado.CuotasReemitidas);
        Assert.Equal(2, resultado.CuotasConPagosConservadas);
        Assert.Null(resultado.SeEmiteElDia);
        // Primero se recalcula el impacto (cuenta con y sin pagos), después se cuentan las que tienen pagos, se borran las
        // pendientes y recién entonces se vuelve a emitir.
        Assert.Equal(new[] { "contar", "contarPendientes", "contar", "eliminar", "generar" }, cuotas.Llamadas.Select(l => l.Operacion));
        Assert.All(cuotas.Llamadas, l => Assert.Equal(1, l.IdCategoria));
        var eliminar = cuotas.Llamadas.Single(l => l.Operacion == "eliminar");
        // Alcanza a toda la categoría y a todos los montos: no se filtra por un monto en particular.
        Assert.Null(eliminar.MontoBase);
        Assert.Equal(new DateTime(2026, 10, 1), eliminar.Desde);
        Assert.Equal(new DateTime(2026, 11, 1), eliminar.Hasta);
    }

    // Antes del 10 la cuota del mes todavía no existe: no se emite nada, el generador la crea el 10 con el monto
    // nuevo. Las ya pagadas por adelantado se conservan y las pendientes sin pagos se quitan (el 10 vuelven).
    [Fact]
    public void ProgramarArancel_ForTheCurrentMonthBeforeThe10th_EmitsNothingNowAndTellsWhenItWillBe()
    {
        var (service, aranceles, cuotas) = Create();
        cuotas.ConPagos = 3;
        cuotas.Pendientes = 1;

        var resultado = SimularYConfirmar(service, cuotas, Cambio(null, 1, 100000, new DateTime(2026, 10, 3)));

        Assert.Single(aranceles.Programados);
        Assert.Equal(new[] { "contar", "contarPendientes", "contar", "eliminar" }, cuotas.Llamadas.Select(l => l.Operacion));
        Assert.Equal(3, resultado.CuotasConPagosConservadas);
        Assert.Equal(0, resultado.CuotasReemitidas);
        Assert.Equal(new DateTime(2026, 10, 10), resultado.SeEmiteElDia);
    }

    [Fact]
    public void ProgramarArancel_ForAFutureMonth_EmitsNothingNow()
    {
        var (service, aranceles, cuotas) = Create();

        var resultado = SimularYConfirmar(service, cuotas, Cambio("Femenino", null, 90000, new DateTime(2026, 11, 1)));

        Assert.Single(aranceles.Programados);
        Assert.Empty(cuotas.Llamadas); // las cuotas de noviembre las crea el generador mensual el 10 de noviembre
        Assert.Equal(0, resultado.CuotasReemitidas);
        Assert.Equal(new DateTime(2026, 11, 10), resultado.SeEmiteElDia);
    }

    // Regla del club: no se programan aranceles con fecha pasada, ni de un mes anterior ni de un día anterior
    // del mes en curso. Se rechaza antes de abrir la transacción: no se graba ni se emite nada.
    [Theory]
    [InlineData(2026, 9, 21)]
    [InlineData(2026, 10, 2)]
    [InlineData(2020, 10, 3)]
    public void ProgramarArancel_WithAPastDate_IsRefusedAndChangesNothing(int anio, int mes, int dia)
    {
        var (service, aranceles, cuotas) = Create();

        var ex = Assert.Throws<ArancelInvalidoException>(() =>
            service.SimularArancel(Cambio("Masculino", null, 80000, new DateTime(anio, mes, dia))));
        Assert.Throws<ArancelInvalidoException>(() =>
            service.ProgramarArancel(Confirmacion(Cambio("Masculino", null, 80000, new DateTime(anio, mes, dia)), "x")));

        Assert.Contains("no puede ser anterior a hoy", ex.Message);
        Assert.Empty(aranceles.Programados);
        Assert.Empty(cuotas.Llamadas);
    }

    [Fact]
    public void ProgramarArancel_ForToday_IsAccepted()
    {
        var (service, aranceles, cuotas) = Create();

        SimularYConfirmar(service, cuotas, Cambio("Masculino", null, 80000, Hoy));

        Assert.Single(aranceles.Programados);
    }

    // ===== Cancelar =====

    [Fact]
    public void CancelarArancel_ThatDoesNotExist_IsRefused()
    {
        var (service, _, _) = Create();

        var ex = Assert.Throws<ArancelInvalidoException>(() => service.CancelarArancel(77));

        Assert.Contains("no existe", ex.Message);
    }

    [Fact]
    public void CancelarArancel_FromAPastMonth_IsRefusedBecauseItIsHistory()
    {
        var (service, aranceles, cuotas) = Create();
        aranceles.Agregar(new Arancel { IdArancel = 5, Genero = "Femenino", Monto = 70000, VigenteDesde = new DateTime(2026, 9, 21) });

        var ex = Assert.Throws<ArancelInvalidoException>(() => service.CancelarArancel(5));

        Assert.Contains("historial", ex.Message);
        Assert.Empty(aranceles.Eliminados);
        Assert.Empty(cuotas.Llamadas);
    }

    [Fact]
    public void CancelarArancel_WhenSomeOfItsCuotasAlreadyHavePayments_IsRefusedAndDeletesNothing()
    {
        var (service, aranceles, cuotas) = Create();
        aranceles.Agregar(new Arancel { IdArancel = 3, Genero = "Masculino", Monto = 11000, VigenteDesde = new DateTime(2026, 10, 20) });
        cuotas.ConPagos = 2;

        var ex = Assert.Throws<ArancelInvalidoException>(() => service.CancelarArancel(3));

        Assert.Contains("2 cuotas", ex.Message);
        Assert.Contains("pagos registrados", ex.Message);
        Assert.Empty(aranceles.Eliminados);
        Assert.DoesNotContain(cuotas.Llamadas, l => l.Operacion == "eliminar");
    }

    [Fact]
    public void CancelarArancel_UsesSingularWhenOnlyOneCuotaHasPayments()
    {
        var (service, aranceles, cuotas) = Create();
        aranceles.Agregar(new Arancel { IdArancel = 3, Genero = "Masculino", Monto = 11000, VigenteDesde = new DateTime(2026, 10, 20) });
        cuotas.ConPagos = 1;

        var ex = Assert.Throws<ArancelInvalidoException>(() => service.CancelarArancel(3));

        Assert.Contains("1 cuota emitida con él ya tiene pagos", ex.Message);
    }

    [Fact]
    public void CancelarArancel_RemovesTheFeeAndItsPendingCuotasMatchedByDestinationPeriodAndAmount()
    {
        var (service, aranceles, cuotas) = Create();
        aranceles.Agregar(new Arancel { IdArancel = 3, Genero = "Masculino", Monto = 11000, VigenteDesde = new DateTime(2026, 10, 20) });
        cuotas.Pendientes = 5;

        var resultado = service.CancelarArancel(3);

        Assert.Equal(5, resultado.CuotasEliminadas);
        Assert.Equal(new[] { 3 }, aranceles.Eliminados);

        var eliminar = cuotas.Llamadas.Single(l => l.Operacion == "eliminar");
        Assert.Equal("Masculino", eliminar.Genero);
        Assert.Null(eliminar.IdCategoria);
        Assert.Equal(11000, eliminar.MontoBase); // solo las emitidas con este arancel, no las de otro monto
        Assert.Equal(new DateTime(2026, 10, 1), eliminar.Desde);
        Assert.Null(eliminar.Hasta); // nadie lo reemplazó: cubre de octubre en adelante
    }

    [Fact]
    public void CancelarArancel_OnlyCoversUntilTheMonthOfTheNextFeeOfTheSameDestination()
    {
        var (service, aranceles, cuotas) = Create();
        aranceles.Agregar(new Arancel { IdArancel = 4, IdCategoria = 1, Monto = 100000, VigenteDesde = new DateTime(2026, 10, 3) });
        aranceles.Siguiente = new DateTime(2026, 12, 1);

        service.CancelarArancel(4);

        var eliminar = cuotas.Llamadas.Single(l => l.Operacion == "eliminar");
        Assert.Equal(1, eliminar.IdCategoria);
        Assert.Null(eliminar.Genero);
        Assert.Equal(new DateTime(2026, 12, 1), eliminar.Hasta);
    }

    // ===== Fakes =====

    // Ejecuta la operación enseguida y reparte cada llamada al fake que guarda su estado. Las lecturas del impacto son
    // las mismas que las de la simulación (FakeArancelesDao), como en la base.
    private class FakeTariffTransactionDao : ITariffTransactionDao, ITariffTransaction
    {
        private readonly FakeArancelesDao _aranceles;
        private readonly FakeCuotasDao _cuotas;

        public FakeTariffTransactionDao(FakeArancelesDao aranceles, FakeCuotasDao cuotas)
        {
            _aranceles = aranceles;
            _cuotas = cuotas;
        }

        public T ExecuteInTransaction<T>(Func<ITariffTransaction, T> operation) => operation(this);

        public T ExecuteRead<T>(Func<ILecturaImpactoArancel, T> operation) => operation(this);

        public void ProgramarArancel(string? genero, int? idCategoria, decimal monto, DateTime vigenteDesde, ImpactoArancel? impacto = null)
            => _aranceles.Programados.Add((genero, idCategoria, monto, vigenteDesde, impacto));

        public Arancel? ObtenerPorId(int idArancel) => _aranceles.ObtenerPorId(idArancel);

        public DateTime? ObtenerVigenteDesdeEnMes(string? genero, int? idCategoria, int anio, int mes) => _aranceles.ExistenteEnMes;

        public DateTime? ObtenerSiguienteVigenteDesde(string? genero, int? idCategoria, DateTime desde) => _aranceles.Siguiente;

        public decimal? ObtenerMontoVigenteDelDestino(string? genero, int? idCategoria, DateTime fecha)
            => _aranceles.ObtenerMontoVigenteDelDestino(genero, idCategoria, fecha);

        public ImpactoArancelConsulta ConsultarImpacto(string? genero, int? idCategoria, decimal montoNuevo, DateTime vigenteDesde)
            => _aranceles.ConsultarImpacto(genero, idCategoria, montoNuevo, vigenteDesde);

        public void EliminarArancel(int idArancel) => _aranceles.Eliminados.Add(idArancel);

        public void GenerarCuotasPendientesDelMes(string? genero, int? idCategoria, int mes, int anio)
            => _cuotas.GenerarCuotasPendientesDelMes(genero, idCategoria, mes, anio);

        public int ContarCuotasConPagos(string? genero, int? idCategoria, DateTime desde, DateTime? hasta, decimal? montoBase)
            => _cuotas.ContarCuotasConPagos(genero, idCategoria, desde, hasta, montoBase);

        public int ContarCuotasPendientesSinPagos(string? genero, int? idCategoria, DateTime desde, DateTime? hasta)
            => _cuotas.ContarCuotasPendientesSinPagos(genero, idCategoria, desde, hasta);

        public int EliminarCuotasPendientesSinPagos(string? genero, int? idCategoria, DateTime desde, DateTime? hasta, decimal? montoBase)
            => _cuotas.EliminarCuotasPendientesSinPagos(genero, idCategoria, desde, hasta, montoBase);
    }

    private class FakeCategoriasDao : ICategoriasDao
    {
        public IReadOnlyList<Categoria> ObtenerTodas() => new List<Categoria>
        {
            new() { IdCategoria = 1, Nombre = "Sub 17" },
            new() { IdCategoria = 2, Nombre = "Sub 15" }
        };
    }

    private class FakeArancelesDao : IArancelesDao
    {
        private readonly Dictionary<int, Arancel> _porId = new();
        private readonly FakeCuotasDao _cuotas;

        public FakeArancelesDao(FakeCuotasDao cuotas) => _cuotas = cuotas;

        public DateTime? ExistenteEnMes { get; set; }
        public DateTime? Siguiente { get; set; }
        // Arancel vigente del destino (la base de un porcentaje o monto fijo).
        public decimal? MontoVigente { get; set; }
        // Lo que devuelve la consulta de impacto: categorías, montos actuales y jugadores (el monto nuevo lo pone la consulta).
        public List<ImpactoArancelLinea> Lineas { get; set; } = new() { new() { IdCategoria = 1, NombreCategoria = "Sub 17", Genero = "Masculino", MontoActual = 50000, Jugadores = 1 } };
        public decimal? ConsultadoConMonto { get; private set; }
        public List<(string? Genero, int? IdCategoria, decimal Monto, DateTime Desde, ImpactoArancel? Impacto)> Programados { get; } = new();
        public List<int> Eliminados { get; } = new();

        public void Agregar(Arancel arancel) => _porId[arancel.IdArancel] = arancel;

        public Arancel? ObtenerPorId(int idArancel)
            => _porId.TryGetValue(idArancel, out var arancel) ? arancel : null;

        public decimal? ObtenerMontoVigenteDelDestino(string? genero, int? idCategoria, DateTime fecha) => MontoVigente;
        public DateTime? ObtenerVigenteDesdeEnMes(string? genero, int? idCategoria, int anio, int mes) => ExistenteEnMes;
        public DateTime? ObtenerSiguienteVigenteDesde(string? genero, int? idCategoria, DateTime desde) => Siguiente;

        public ImpactoArancelConsulta ConsultarImpacto(string? genero, int? idCategoria, decimal montoNuevo, DateTime vigenteDesde)
        {
            ConsultadoConMonto = montoNuevo;
            return new ImpactoArancelConsulta
            {
                Lineas = Lineas.Select(l => new ImpactoArancelLinea
                {
                    IdCategoria = l.IdCategoria, NombreCategoria = l.NombreCategoria, Genero = l.Genero,
                    MontoActual = l.MontoActual, MontoNuevo = montoNuevo, Jugadores = l.Jugadores
                }).ToList()
            };
        }

        public int ContarCuotasConPagos(string? genero, int? idCategoria, DateTime desde, DateTime? hasta, decimal? montoBase)
            => _cuotas.ContarCuotasConPagos(genero, idCategoria, desde, hasta, montoBase);

        public int ContarCuotasPendientesSinPagos(string? genero, int? idCategoria, DateTime desde, DateTime? hasta)
            => _cuotas.ContarCuotasPendientesSinPagos(genero, idCategoria, desde, hasta);

        public decimal? ObtenerMontoVigente(string genero, DateTime fecha) => throw new NotSupportedException();
        public decimal? ObtenerMontoVigente(string genero, int idCategoria, DateTime fecha) => throw new NotSupportedException();
        public IReadOnlyList<ArancelHistorialItem> ObtenerHistorial() => throw new NotSupportedException();
        public ArancelResumen ObtenerResumen() => throw new NotSupportedException();
        public void ProgramarArancel(string? genero, int? idCategoria, decimal monto, DateTime vigenteDesde) => throw new NotSupportedException();
    }

    private class FakeCuotasDao
    {
        public record Llamada(string Operacion, string? Genero, int? IdCategoria, DateTime Desde, DateTime? Hasta, decimal? MontoBase, int Mes);

        public int ConPagos { get; set; }
        public int Pendientes { get; set; }
        public List<Llamada> Llamadas { get; } = new();

        public int ContarCuotasConPagos(string? genero, int? idCategoria, DateTime desde, DateTime? hasta, decimal? montoBase)
        {
            Llamadas.Add(new Llamada("contar", genero, idCategoria, desde, hasta, montoBase, 0));
            return ConPagos;
        }

        public int ContarCuotasPendientesSinPagos(string? genero, int? idCategoria, DateTime desde, DateTime? hasta)
        {
            Llamadas.Add(new Llamada("contarPendientes", genero, idCategoria, desde, hasta, null, 0));
            return Pendientes;
        }

        public int EliminarCuotasPendientesSinPagos(string? genero, int? idCategoria, DateTime desde, DateTime? hasta, decimal? montoBase)
        {
            Llamadas.Add(new Llamada("eliminar", genero, idCategoria, desde, hasta, montoBase, 0));
            return Pendientes;
        }

        public void GenerarCuotasPendientesDelMes(string? genero, int? idCategoria, int mes, int anio)
            => Llamadas.Add(new Llamada("generar", genero, idCategoria, new DateTime(anio, mes, 1), null, null, mes));
    }
}
