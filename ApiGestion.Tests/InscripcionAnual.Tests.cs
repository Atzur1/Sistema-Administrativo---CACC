namespace ApiGestion.Tests;

using EntityLibrary;

// La inscripción es anual: el año de cada jugador empieza el día en que se le asignó y se renueva en cada aniversario.
public class InscripcionAnualTests
{
    private static readonly DateTime Alta = new(2025, 3, 5);

    [Fact]
    public void ElPrimerAniversarioLlegaAlCumplirseElAnio()
    {
        Assert.Null(InscripcionAnual.UltimoAniversario(Alta, new DateTime(2026, 3, 4)));
        Assert.Equal(new DateTime(2026, 3, 5), InscripcionAnual.UltimoAniversario(Alta, new DateTime(2026, 3, 5)));
    }

    [Fact]
    public void ElUltimoAniversarioEsElMasRecienteQueYaLlego()
    {
        Assert.Equal(new DateTime(2026, 3, 5), InscripcionAnual.UltimoAniversario(Alta, new DateTime(2027, 3, 4)));
        Assert.Equal(new DateTime(2027, 3, 5), InscripcionAnual.UltimoAniversario(Alta, new DateTime(2027, 3, 5)));
        Assert.Equal(new DateTime(2028, 3, 5), InscripcionAnual.UltimoAniversario(Alta, new DateTime(2028, 12, 31)));
    }

    [Fact]
    public void UnAltaEnElFuturoTodaviaNoTieneAniversario()
    {
        Assert.Null(InscripcionAnual.UltimoAniversario(new DateTime(2030, 1, 1), new DateTime(2026, 10, 21)));
    }

    [Fact]
    public void ElAnioDelBisiestoSeAjustaAlUltimoDiaDeFebrero()
    {
        var bisiesto = new DateTime(2024, 2, 29);

        Assert.Equal(new DateTime(2025, 2, 28), InscripcionAnual.UltimoAniversario(bisiesto, new DateTime(2025, 6, 1)));
        // Y se calcula siempre desde el alta original: el 29 vuelve en el siguiente bisiesto.
        Assert.Equal(new DateTime(2028, 2, 29), InscripcionAnual.UltimoAniversario(bisiesto, new DateTime(2028, 3, 1)));
    }

    [Fact]
    public void LaInscripcionCubreHastaLaViSperaDeSuAniversario()
    {
        var emision = new DateTime(2026, 10, 21);

        Assert.True(InscripcionAnual.Cubre(emision, new DateTime(2026, 10, 21)));
        Assert.True(InscripcionAnual.Cubre(emision, new DateTime(2027, 10, 20)));
        Assert.False(InscripcionAnual.Cubre(emision, new DateTime(2027, 10, 21)));
    }
}
