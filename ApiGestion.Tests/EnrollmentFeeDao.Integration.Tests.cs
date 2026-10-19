namespace ApiGestion.Tests;

using DaoLibrary;
using EntityLibrary;
using Microsoft.Data.SqlClient;

// Aranceles de inscripción por género, contra la base real dentro de una transacción que se revierte. Cada género tiene
// su propia historia: la fecha en que termina un arancel es el día anterior al siguiente del MISMO género.
// Las fechas son de los años 1990: no chocan con los aranceles reales ni dependen del día en que corre la prueba.
[Collection(BaseDeDatosCollection.Nombre)]
public class EnrollmentFeeDaoIntegrationTests
{
    private static string Conexion => Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable)!;

    private static void Insertar(SqlConnection c, SqlTransaction t, string genero, decimal monto, string desde)
    {
        using var cmd = new SqlCommand("INSERT INTO ARANCELES_INSCRIPCION (genero, monto, vigente_desde) VALUES (@g, @m, @d)", c, t);
        cmd.Parameters.AddWithValue("@g", genero);
        cmd.Parameters.AddWithValue("@m", monto);
        cmd.Parameters.AddWithValue("@d", DateTime.Parse(desde));
        cmd.ExecuteNonQuery();
    }

    [IntegracionFact]
    public void CadaGeneroTieneSuPropiaHistoria_YElMontoCeroEsValido()
    {
        using var connection = new SqlConnection(Conexion);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            Insertar(connection, transaction, "Femenino", 30_000m, "1990-01-01");
            Insertar(connection, transaction, "Masculino", 50_000m, "1990-06-01"); // en el medio de la historia femenina
            Insertar(connection, transaction, "Femenino", 0m, "1991-01-01");       // deja de cobrarse

            var todas = EnrollmentFeeDAO.GetAllCore(connection, transaction);
            var femeninas = todas.Where(f => f.Gender == "Femenino" && f.StartDate.Year < 1992).OrderBy(f => f.StartDate).ToList();

            Assert.Equal(2, femeninas.Count);
            // Si la historia no se separara por género, terminaría el 31/5/1990 (el día anterior al arancel masculino).
            Assert.Equal(new DateTime(1990, 12, 31), femeninas[0].EndDate);
            Assert.Equal(EnrollmentFeeStatus.Previous, femeninas[0].Status);
            Assert.Equal(0m, femeninas[1].Amount);
            Assert.Contains(todas, f => f.Gender == "Masculino" && f.StartDate == new DateTime(1990, 6, 1) && f.Amount == 50_000m);
        }
        finally { transaction.Rollback(); }
    }

    [IntegracionFact]
    public void ElArancelVigenteEsElDelGeneroPedido()
    {
        using var connection = new SqlConnection(Conexion);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            // Hoy mismo: queda vigente para su género (los de 1990 de otras pruebas quedan anteriores o no existen).
            string hoy = RelojNegocio.Hoy.ToString("yyyy-MM-dd");
            Insertar(connection, transaction, "Femenino", 33_000m, hoy);

            Assert.Equal(33_000m, EnrollmentFeeDAO.GetCurrentCore(connection, transaction, "Femenino")!.Amount);
            // El masculino es otro: lo que haya vigente para él no se mezcla con el femenino.
            var masculino = EnrollmentFeeDAO.GetCurrentCore(connection, transaction, "Masculino");
            Assert.True(masculino is null || masculino.Gender == "Masculino");
        }
        finally { transaction.Rollback(); }
    }

    [IntegracionFact]
    public void LaBaseRechazaUnArancelSinGeneroOElMismoDiaParaElMismoGenero()
    {
        using var connection = new SqlConnection(Conexion);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            Insertar(connection, transaction, "Femenino", 30_000m, "1990-01-01");

            // Mismo género y mismo día: choca con el índice único. Se usa un punto de guardado para no perder la transacción.
            transaction.Save("antes");
            Assert.Throws<SqlException>(() => Insertar(connection, transaction, "Femenino", 31_000m, "1990-01-01"));
            transaction.Rollback("antes");

            // Otro género el mismo día: permitido.
            Insertar(connection, transaction, "Masculino", 50_000m, "1990-01-01");

            // Sin género: ya no existe "aplica a todos".
            transaction.Save("sinGenero");
            using var sinGenero = new SqlCommand("INSERT INTO ARANCELES_INSCRIPCION (monto, vigente_desde) VALUES (1, '1992-01-01')", connection, transaction);
            Assert.Throws<SqlException>(() => sinGenero.ExecuteNonQuery());
            transaction.Rollback("sinGenero");
        }
        finally { transaction.Rollback(); }
    }
}
