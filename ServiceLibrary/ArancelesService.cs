using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DaoLibrary;
using DaoLibrary.Exceptions;
using EntityLibrary;

namespace ServiceLibrary
{
    public class ArancelesService : IArancelesService
    {
        // HU-069: el porcentaje se escribe con hasta 3 cifras enteras y 2 decimales.
        public const decimal PorcentajeMaximo = 999.99m;

        private readonly IArancelesDao _arancelesDao;
        private readonly ITariffTransactionDao _tariffTransactionDao;
        private readonly ICategoriasDao _categoriasDao;
        private readonly Func<DateTime> _hoy;

        // reloj: solo para las pruebas; en la aplicación se usa la fecha real.
        public ArancelesService(IArancelesDao arancelesDao, ITariffTransactionDao tariffTransactionDao, ICategoriasDao categoriasDao, Func<DateTime>? reloj = null)
        {
            _arancelesDao = arancelesDao;
            _tariffTransactionDao = tariffTransactionDao;
            _categoriasDao = categoriasDao;
            _hoy = reloj ?? (() => RelojNegocio.Hoy);
        }

        public IReadOnlyList<ArancelHistorialItem> ObtenerHistorial() => _arancelesDao.ObtenerHistorial();

        public ArancelResumen ObtenerResumen() => _arancelesDao.ObtenerResumen();

        // HU-106: el impacto de un cambio sin grabar nada. Solo lecturas, sin transacción ni bloqueo (ExecuteRead): las
        // mismas que la confirmación vuelve a hacer dentro de su transacción.
        public ImpactoArancel SimularArancel(CambioArancel cambio)
        {
            string? nombreCategoria = Validar(cambio);
            return _tariffTransactionDao.ExecuteRead(lector => CalcularImpacto(lector, cambio, nombreCategoria));
        }

        public ProgramarArancelResultado ProgramarArancel(ProgramarArancelRequest request)
        {
            string? nombreCategoria = Validar(request);
            if (string.IsNullOrWhiteSpace(request.Huella))
            {
                throw new ArancelInvalidoException("Revisá el impacto del cambio antes de confirmarlo.");
            }

            string? genero = string.IsNullOrWhiteSpace(request.Genero) ? null : request.Genero;
            DateTime vigenteDesde = request.VigenteDesde.Date;
            DateTime mesDelArancel = CalendarioCuotas.PrimerDiaDelMes(vigenteDesde);
            DateTime mesActual = CalendarioCuotas.PrimerDiaDelMes(_hoy());

            return _tariffTransactionDao.ExecuteInTransaction(transaccion =>
            {
                // HU-106: el impacto se vuelve a calcular acá, con la misma lógica que la simulación y con el bloqueo de
                // escritura de cobros ya tomado (TariffTransactionDao), así nada lo cambia entre esta lectura y la grabación.
                // Si no es el que el SuperAdmin aprobó (otro arancel, altas, bajas o cobros en el medio), no se graba.
                ImpactoArancel impacto = CalcularImpacto(transaccion, request, nombreCategoria);
                if (!string.Equals(impacto.Huella, request.Huella, StringComparison.Ordinal))
                {
                    throw new ImpactoDesactualizadoException(impacto);
                }
                if (impacto.RequiereConfirmacionReforzada && request.JugadoresConfirmados != impacto.Jugadores)
                {
                    throw new ArancelInvalidoException(
                        $"El cambio alcanza a más de una categoría: para confirmarlo escribí la cantidad de jugadores afectados ({impacto.Jugadores}).");
                }

                transaccion.ProgramarArancel(genero, request.IdCategoria, impacto.MontoNuevo, vigenteDesde, impacto);

                var resultado = new ProgramarArancelResultado { Impacto = impacto };

                // Mes futuro: no se emite nada ahora. GeneradorCuotasMensuales crea las cuotas de ese mes
                // el día 10, con el arancel que corresponda a cada jugador.
                if (mesDelArancel > mesActual)
                {
                    resultado.SeEmiteElDia = mesDelArancel.AddDays(CalendarioCuotas.DiaDeCorte - 1);
                    return resultado;
                }

                // Mes en curso (un mes pasado no llega hasta acá: lo rechaza la validación de la fecha). Las
                // cuotas que ya recibieron algún pago (por ejemplo, pagadas por adelantado) no se tocan; las
                // pendientes sin pagos se quitan para volver a emitirlas con el monto nuevo.
                DateTime mesSiguiente = mesDelArancel.AddMonths(1);
                resultado.CuotasConPagosConservadas = transaccion.ContarCuotasConPagos(
                    genero, request.IdCategoria, mesDelArancel, mesSiguiente, null);
                int quitadas = transaccion.EliminarCuotasPendientesSinPagos(
                    genero, request.IdCategoria, mesDelArancel, mesSiguiente, null);

                // Antes del día 10 la cuota del mes todavía no se generó: no se emite nada ahora, el generador
                // la crea el 10 ya con este monto (también para las que se acaban de quitar).
                if (!CalendarioCuotas.YaSeGeneraElMesEnCurso(_hoy()))
                {
                    resultado.SeEmiteElDia = mesDelArancel.AddDays(CalendarioCuotas.DiaDeCorte - 1);
                    return resultado;
                }

                // Desde el 10: vuelve a emitir las que se quitaron (y las que faltaban) con el arancel que le
                // corresponde a cada jugador: el de su categoría si lo tiene vigente, si no el de su género.
                resultado.CuotasReemitidas = quitadas;
                transaccion.GenerarCuotasPendientesDelMes(genero, request.IdCategoria, vigenteDesde.Month, vigenteDesde.Year);
                return resultado;
            });
        }

        // HU-069 en el backend: el monto que se programa según lo que escribió el SuperAdmin. Es la única definición de
        // la regla; la usan la simulación y la confirmación (antes vivía en el frontend). montoBase: arancel vigente
        // del destino. Redondeo del porcentaje: al peso, las mitades hacia arriba (como Math.round del portal).
        public static decimal CalcularMontoNuevo(TipoCambioArancel tipo, decimal valor, decimal? montoBase, string destino = "Este destino")
        {
            switch (tipo)
            {
                case TipoCambioArancel.Absoluto:
                    if (valor <= 0)
                    {
                        throw new ArancelInvalidoException("El monto debe ser mayor a cero.");
                    }
                    ExigirCentavos(valor, "El monto");
                    return valor;

                case TipoCambioArancel.Porcentaje:
                    if (valor <= 0)
                    {
                        throw new ArancelInvalidoException("El porcentaje tiene que ser mayor a 0.");
                    }
                    if (valor > PorcentajeMaximo || decimal.Round(valor, 2) != valor)
                    {
                        throw new ArancelInvalidoException("El porcentaje admite hasta 999,99 con dos decimales como máximo.");
                    }
                    return Math.Round(ExigirBase(montoBase, destino) * (1m + valor / 100m), 0, MidpointRounding.AwayFromZero);

                case TipoCambioArancel.MontoFijo:
                    if (valor <= 0)
                    {
                        throw new ArancelInvalidoException("El monto a sumar tiene que ser mayor a $0.");
                    }
                    ExigirCentavos(valor, "El monto a sumar");
                    return ExigirBase(montoBase, destino) + valor;

                default:
                    throw new ArancelInvalidoException("Tipo de cambio inválido.");
            }
        }

        // El impacto de un cambio, leído con `lector`: la simulación (ArancelesDao) y la confirmación (la transacción
        // que graba) pasan por acá, así nunca calculan distinto.
        private ImpactoArancel CalcularImpacto(ILecturaImpactoArancel lector, CambioArancel cambio, string? nombreCategoria)
        {
            string? genero = string.IsNullOrWhiteSpace(cambio.Genero) ? null : cambio.Genero;
            DateTime vigenteDesde = cambio.VigenteDesde.Date;
            DateTime hoy = _hoy().Date;
            DateTime mesDelArancel = CalendarioCuotas.PrimerDiaDelMes(vigenteDesde);

            // Un solo arancel por mes y destino: dos en el mismo mes se pisarían (la cuota del mes
            // toma el último) y dejarían cuotas emitidas con un monto que ya no figura vigente.
            // Para cambiar el de un mes hay que cancelar el existente y programar el nuevo.
            DateTime? existente = lector.ObtenerVigenteDesdeEnMes(genero, cambio.IdCategoria, vigenteDesde.Year, vigenteDesde.Month);
            if (existente.HasValue)
            {
                throw new ArancelInvalidoException(
                    $"{Destino(genero, nombreCategoria)} ya tiene un arancel que rige desde el {existente.Value:dd/MM/yyyy}, " +
                    $"en {NombreDelMes(mesDelArancel)}. Cancelalo primero para poder cargar uno nuevo para ese mes.");
            }

            decimal? montoBase = lector.ObtenerMontoVigenteDelDestino(genero, cambio.IdCategoria, hoy);
            decimal montoNuevo = CalcularMontoNuevo(cambio.Tipo, cambio.Valor, montoBase, Destino(genero, nombreCategoria));
            ImpactoArancelConsulta consulta = lector.ConsultarImpacto(genero, cambio.IdCategoria, montoNuevo, vigenteDesde);

            var impacto = new ImpactoArancel
            {
                Tipo = cambio.Tipo,
                Valor = cambio.Valor,
                Genero = genero,
                IdCategoria = cambio.IdCategoria,
                NombreCategoria = nombreCategoria,
                VigenteDesde = vigenteDesde,
                MontoBase = montoBase,
                MontoNuevo = montoNuevo,
                MesDeAplicacion = mesDelArancel,
                ReemplazadoDesde = lector.ObtenerSiguienteVigenteDesde(genero, cambio.IdCategoria, vigenteDesde),
                Lineas = consulta.Lineas,
                Excluidos = consulta.Excluidos
            };

            if (mesDelArancel > CalendarioCuotas.PrimerDiaDelMes(hoy))
            {
                impacto.SeEmiteElDia = mesDelArancel.AddDays(CalendarioCuotas.DiaDeCorte - 1);
            }
            else
            {
                // Mismo alcance que la confirmación: cuotas del mes con pagos (se conservan) y pendientes sin pagos (se
                // quitan y se vuelven a emitir con el monto nuevo). Las de meses anteriores no se tocan nunca.
                DateTime mesSiguiente = mesDelArancel.AddMonths(1);
                impacto.MesEnCurso = true;
                impacto.CuotasDelMesConPagos = lector.ContarCuotasConPagos(genero, cambio.IdCategoria, mesDelArancel, mesSiguiente, null);
                impacto.CuotasDelMesPendientesSinPagos = lector.ContarCuotasPendientesSinPagos(genero, cambio.IdCategoria, mesDelArancel, mesSiguiente);
                if (!CalendarioCuotas.YaSeGeneraElMesEnCurso(hoy))
                {
                    impacto.SeEmiteElDia = mesDelArancel.AddDays(CalendarioCuotas.DiaDeCorte - 1);
                }
            }

            impacto.Huella = Huella(impacto);
            return impacto;
        }

        // Todo lo que el SuperAdmin ve en el resumen y aprueba: si al confirmar cualquiera de estos datos es distinto,
        // la huella cambia y la confirmación se rechaza. Formato invariante, líneas en el orden de la consulta.
        public static string Huella(ImpactoArancel impacto)
        {
            var c = CultureInfo.InvariantCulture;
            var texto = new StringBuilder()
                .Append(impacto.Tipo).Append('|').Append(impacto.Valor.ToString("0.00", c)).Append('|')
                .Append(impacto.Genero).Append('|').Append(impacto.IdCategoria).Append('|')
                .Append(impacto.VigenteDesde.ToString("yyyy-MM-dd", c)).Append('|')
                .Append(impacto.MontoBase?.ToString("0.00", c)).Append('|').Append(impacto.MontoNuevo.ToString("0.00", c)).Append('|')
                .Append(impacto.ReemplazadoDesde?.ToString("yyyy-MM-dd", c)).Append('|')
                .Append(impacto.MesEnCurso).Append('|').Append(impacto.CuotasDelMesPendientesSinPagos).Append('|')
                .Append(impacto.CuotasDelMesConPagos).Append('|').Append(impacto.SeEmiteElDia?.ToString("yyyy-MM-dd", c)).Append('|');
            foreach (ImpactoArancelLinea linea in impacto.Lineas)
            {
                texto.Append(linea.IdCategoria).Append(':').Append(linea.Genero).Append(':')
                    .Append(linea.MontoActual?.ToString("0.00", c)).Append(':').Append(linea.Jugadores).Append(';');
            }
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto.ToString())));
        }

        public CancelarArancelResultado CancelarArancel(int idArancel)
        {
            DateTime mesActual = CalendarioCuotas.PrimerDiaDelMes(_hoy());

            return _tariffTransactionDao.ExecuteInTransaction(transaccion =>
            {
                Arancel arancel = transaccion.ObtenerPorId(idArancel)
                    ?? throw new ArancelInvalidoException("El arancel no existe o ya fue cancelado.");

                DateTime mesDelArancel = CalendarioCuotas.PrimerDiaDelMes(arancel.VigenteDesde);
                if (mesDelArancel < mesActual)
                {
                    throw new ArancelInvalidoException(
                        "Los aranceles de meses anteriores son parte del historial y no se pueden cancelar: " +
                        "sus cuotas ya se emitieron en períodos cerrados.");
                }

                // Períodos que cubre este arancel: desde su mes hasta el mes del arancel siguiente del mismo
                // destino (sin incluirlo), o sin límite si nadie lo reemplazó.
                DateTime? siguiente = transaccion.ObtenerSiguienteVigenteDesde(
                    arancel.Genero, arancel.IdCategoria, arancel.VigenteDesde);
                DateTime? hasta = siguiente.HasValue ? CalendarioCuotas.PrimerDiaDelMes(siguiente.Value) : null;

                // Las cuotas emitidas con este arancel son las del mismo destino, en esos períodos, por su monto.
                int conPagos = transaccion.ContarCuotasConPagos(
                    arancel.Genero, arancel.IdCategoria, mesDelArancel, hasta, arancel.Monto);
                if (conPagos > 0)
                {
                    throw new ArancelInvalidoException(
                        $"No se puede cancelar este arancel: {conPagos} cuota{(conPagos == 1 ? "" : "s")} emitida{(conPagos == 1 ? "" : "s")} " +
                        $"con él ya tiene{(conPagos == 1 ? "" : "n")} pagos registrados. Un arancel con cuotas cobradas ya no se puede quitar.");
                }

                int eliminadas = transaccion.EliminarCuotasPendientesSinPagos(
                    arancel.Genero, arancel.IdCategoria, mesDelArancel, hasta, arancel.Monto);
                transaccion.EliminarArancel(idArancel);

                return new CancelarArancelResultado { CuotasEliminadas = eliminadas };
            });
        }

        // Destino válido (género O categoría) y fecha no pasada. Devuelve el nombre de la categoría, si es por categoría.
        private string? Validar(CambioArancel cambio)
        {
            // Género O categoría, nunca los dos ni ninguno: si la categoría tiene arancel propio,
            // al sistema no le importa el género de sus jugadores.
            bool tieneGenero = !string.IsNullOrWhiteSpace(cambio.Genero);
            if (tieneGenero == cambio.IdCategoria.HasValue)
            {
                throw new ArancelInvalidoException("El arancel debe ser por género o por categoría (uno solo).");
            }

            if (tieneGenero && !Generos.Validos.Contains(cambio.Genero))
            {
                throw new ArancelInvalidoException(
                    $"Género inválido: '{cambio.Genero}'. Valores permitidos: {string.Join(", ", Generos.Validos)}.");
            }

            if (!Enum.IsDefined(cambio.Tipo))
            {
                throw new ArancelInvalidoException("Tipo de cambio inválido.");
            }

            string? nombreCategoria = null;
            if (cambio.IdCategoria.HasValue)
            {
                nombreCategoria = _categoriasDao.ObtenerTodas().FirstOrDefault(c => c.IdCategoria == cambio.IdCategoria.Value)?.Nombre;
                if (nombreCategoria == null)
                {
                    throw new ArancelInvalidoException($"No existe la categoría con id {cambio.IdCategoria.Value}.");
                }
            }

            // Regla del club: no se programan aranceles con fecha pasada. Un mes cerrado no se reescribe, y una
            // fecha mal tipeada (por ejemplo, un año anterior) generaba deuda retroactiva que después no se podía
            // cancelar porque ese mes ya era historial. Un arancel cargado hoy igual cubre todo el mes en curso.
            if (cambio.VigenteDesde.Date < _hoy().Date)
            {
                throw new ArancelInvalidoException(
                    $"La fecha de vigencia no puede ser anterior a hoy ({_hoy():dd/MM/yyyy}). Los aranceles solo se programan para el mes en curso o meses futuros.");
            }

            return nombreCategoria;
        }

        // ARANCELES.monto guarda centavos: un valor con más decimales se redondearía al grabar y dejaría de coincidir
        // con lo que mostró la simulación.
        private static void ExigirCentavos(decimal valor, string campo)
        {
            if (decimal.Round(valor, 2) != valor)
            {
                throw new ArancelInvalidoException($"{campo} admite como máximo dos decimales.");
            }
        }

        private static decimal ExigirBase(decimal? montoBase, string destino) =>
            montoBase ?? throw new ArancelInvalidoException(
                $"{destino} no tiene un arancel vigente sobre el que calcular el aumento. Usá \"Monto absoluto\" para definir su primer valor.");

        private static string Destino(string? genero, string? nombreCategoria) =>
            genero != null ? $"El género {genero}" : $"La categoría {nombreCategoria}";

        private static string NombreDelMes(DateTime mes) => $"{CalendarioCuotas.NombresMes[mes.Month - 1].ToLowerInvariant()} de {mes.Year}";
    }
}
