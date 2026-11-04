// HU-066: piezas de concurrencia de la suite (las compila concurrencia_hu066.ps1 con Add-Type, no forman parte de las APIs).
//
//  - Rafaga: N pedidos HTTP que arrancan JUNTOS. Cada pedido tiene su propio hilo; el mensaje se arma antes y todos esperan en
//    una Barrier, así la superposición no depende de la velocidad de PowerShell ni del pool de hilos. Registra inicio, fin,
//    duración, código y cuerpo de cada uno.
//  - Monitor: mientras corre una ráfaga, muestrea cada ~20 ms sys.dm_exec_requests y sys.dm_tran_locks de la base de prueba:
//    cuántas operaciones esperan a la vez el bloqueo de cobros (BloqueoEscrituraPagos), cuántas están bloqueadas por otra
//    sesión, la espera más larga, los tipos de espera y el nivel de aislamiento de cada sesión de las APIs (por Application Name).
//    Es la evidencia de que las operaciones se superpusieron de verdad y de cuánto se bloquearon.
//  - Compuerta: toma el MISMO bloqueo de aplicación que las APIs (CACC_ESCRITURA_PAGOS) durante unos milisegundos para que todas
//    las operaciones de una ráfaga estén en vuelo a la vez antes de que la primera avance. No ordena nada: el orden lo decide
//    SQL Server al liberarla, igual que en producción.
//  - Pruebas técnicas (SQL directo, separadas de los flujos funcionales): SEQUENCE bajo sesiones paralelas y SESSION_CONTEXT con
//    conexiones reutilizadas del pool.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Hu066
{
    public sealed class Pedido
    {
        public string Etiqueta = "";
        public string Metodo = "GET";
        public string Url = "";
        public string Token;
        public string Clave;
        public string Cuerpo;
    }

    public sealed class Resultado
    {
        public string Etiqueta;
        public string Metodo;
        public string Url;
        public int Codigo;
        public string Cuerpo;
        public DateTime InicioUtc;
        public DateTime FinUtc;
        public double Ms;
        public string Error;
    }

    public static class Rafaga
    {
        private static readonly HttpClient Http = new HttpClient(new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 512,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5)
        }) { Timeout = TimeSpan.FromSeconds(90) };

        public static Task<Resultado[]> Iniciar(Pedido[] pedidos)
            => Task.Factory.StartNew(() => Lanzar(pedidos), TaskCreationOptions.LongRunning);

        public static Resultado[] Lanzar(Pedido[] pedidos)
        {
            var resultados = new Resultado[pedidos.Length];
            using var barrera = new Barrier(pedidos.Length);
            var hilos = new Thread[pedidos.Length];
            for (int i = 0; i < pedidos.Length; i++)
            {
                int k = i;
                hilos[k] = new Thread(() =>
                {
                    var p = pedidos[k];
                    var r = new Resultado { Etiqueta = p.Etiqueta, Metodo = p.Metodo, Url = p.Url };
                    HttpRequestMessage mensaje = Construir(p);
                    barrera.SignalAndWait();
                    var reloj = Stopwatch.StartNew();
                    r.InicioUtc = DateTime.UtcNow;
                    try
                    {
                        using var respuesta = Http.Send(mensaje);
                        r.Codigo = (int)respuesta.StatusCode;
                        r.Cuerpo = respuesta.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    }
                    catch (Exception e)
                    {
                        r.Codigo = 0;
                        r.Error = e.GetBaseException().Message;
                    }
                    reloj.Stop();
                    r.FinUtc = DateTime.UtcNow;
                    r.Ms = reloj.Elapsed.TotalMilliseconds;
                    resultados[k] = r;
                }) { IsBackground = true };
            }
            foreach (var h in hilos) h.Start();
            foreach (var h in hilos) h.Join();
            return resultados;
        }

        private static HttpRequestMessage Construir(Pedido p)
        {
            var m = new HttpRequestMessage(new HttpMethod(p.Metodo), p.Url);
            if (!string.IsNullOrEmpty(p.Token)) m.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", p.Token);
            if (p.Clave != null) m.Headers.TryAddWithoutValidation("X-Integration-Key", p.Clave);
            if (p.Cuerpo != null) m.Content = new StringContent(p.Cuerpo, Encoding.UTF8, "application/json");
            return m;
        }
    }

    public sealed class Monitor : IDisposable
    {
        private readonly string _cadena;
        private readonly string _base;
        private readonly CancellationTokenSource _fin = new CancellationTokenSource();
        private readonly Thread _hilo;
        private readonly object _cerrojo = new object();

        public int Muestras;
        public int MaxEsperandoApplock;
        public int MaxBloqueadas;
        public int MaxEsperaMs;
        public int MaxSesionesActivas;
        public readonly SortedSet<string> TiposEspera = new SortedSet<string>();
        // "programa:nivel" (1 READ UNCOMMITTED, 2 READ COMMITTED, 3 REPEATABLE READ, 4 SERIALIZABLE, 5 SNAPSHOT)
        public readonly SortedSet<string> Aislamiento = new SortedSet<string>();
        // Bloqueos entre sesiones de sistemas distintos: "bloqueado <- bloqueador (tipo de espera)"
        public readonly SortedSet<string> BloqueosCruzados = new SortedSet<string>();
        public readonly List<string> Errores = new List<string>();

        public Monitor(string cadena, string baseDatos)
        {
            _cadena = cadena;
            _base = baseDatos;
            _hilo = new Thread(Bucle) { IsBackground = true };
            _hilo.Start();
        }

        private void Bucle()
        {
            try
            {
                using var c = new SqlConnection(_cadena);
                c.Open();
                const string consulta = @"
SELECT r.session_id, r.blocking_session_id, ISNULL(r.wait_type, ''), r.wait_time, ISNULL(s.program_name, ''),
       ISNULL(bs.program_name, ''), ISNULL(r.wait_resource, ''),
       ISNULL((SELECT LEFT(REPLACE(REPLACE(t.text, CHAR(13), ' '), CHAR(10), ' '), 160) FROM sys.dm_exec_sql_text(r.sql_handle) t), '')
FROM sys.dm_exec_requests r
JOIN sys.dm_exec_sessions s ON s.session_id = r.session_id
LEFT JOIN sys.dm_exec_sessions bs ON bs.session_id = r.blocking_session_id
WHERE r.database_id = DB_ID(@base) AND r.session_id <> @@SPID;
SELECT COUNT(*) FROM sys.dm_tran_locks
WHERE resource_type = 'APPLICATION' AND request_status = 'WAIT' AND resource_database_id = DB_ID(@base);
SELECT ISNULL(program_name, ''), transaction_isolation_level FROM sys.dm_exec_sessions
WHERE database_id = DB_ID(@base) AND is_user_process = 1 AND session_id <> @@SPID;";
                while (!_fin.IsCancellationRequested)
                {
                    using (var cmd = new SqlCommand(consulta, c))
                    {
                        cmd.Parameters.AddWithValue("@base", _base);
                        using var rd = cmd.ExecuteReader();
                        int activas = 0, bloqueadas = 0, esperaMax = 0;
                        var tipos = new List<string>();
                        var cruzados = new List<string>();
                        while (rd.Read())
                        {
                            activas++;
                            int bloqueador = rd.GetInt16(1);
                            string tipo = rd.GetString(2);
                            int espera = rd.GetInt32(3);
                            string programa = rd.GetString(4);
                            string programaBloqueador = rd.GetString(5);
                            if (tipo.Length > 0) tipos.Add(tipo);
                            if (bloqueador != 0)
                            {
                                bloqueadas++;
                                if (espera > esperaMax) esperaMax = espera;
                                // Un bloqueo de esquema no es de una fila: se registra el objeto y la sentencia que espera.
                                string detalle = tipo.StartsWith("LCK_M_SCH") ? $" [{rd.GetString(6)}] {rd.GetString(7)}" : "";
                                if (Sistema(programa) != Sistema(programaBloqueador) || detalle.Length > 0)
                                    cruzados.Add($"{programa} <- {programaBloqueador} ({tipo}){detalle}");
                            }
                        }
                        rd.NextResult();
                        rd.Read();
                        int applock = rd.GetInt32(0);
                        rd.NextResult();
                        var niveles = new List<string>();
                        while (rd.Read()) niveles.Add($"{rd.GetString(0)}:{rd.GetInt16(1)}");
                        lock (_cerrojo)
                        {
                            Muestras++;
                            MaxSesionesActivas = Math.Max(MaxSesionesActivas, activas);
                            MaxBloqueadas = Math.Max(MaxBloqueadas, bloqueadas);
                            MaxEsperaMs = Math.Max(MaxEsperaMs, esperaMax);
                            MaxEsperandoApplock = Math.Max(MaxEsperandoApplock, applock);
                            foreach (var t in tipos) TiposEspera.Add(t);
                            foreach (var x in cruzados) BloqueosCruzados.Add(x);
                            foreach (var n in niveles) Aislamiento.Add(n);
                        }
                    }
                    Thread.Sleep(20);
                }
            }
            catch (Exception e)
            {
                lock (_cerrojo) Errores.Add(e.GetBaseException().Message);
            }
        }

        private static string Sistema(string programa)
            => programa.StartsWith("CACC-Deportivo", StringComparison.Ordinal) ? "D"
             : programa.StartsWith("CACC-Administrativo", StringComparison.Ordinal) ? "A" : "X";

        public void Dispose()
        {
            _fin.Cancel();
            _hilo.Join(5000);
        }
    }

    public static class Compuerta
    {
        // Toma CACC_ESCRITURA_PAGOS (el recurso de DaoLibrary.BloqueoEscrituraPagos) hasta que se señale `liberar` (o pasen
        // `maxMs`, por seguridad). `tomada` se señala apenas SQL Server la concede. Devuelve los ms que estuvo tomada.
        public static Task<double> Sostener(string cadena, int maxMs, ManualResetEventSlim tomada, ManualResetEventSlim liberar)
            => Task.Factory.StartNew(() =>
            {
                using var c = new SqlConnection(cadena);
                c.Open();
                using var tx = c.BeginTransaction();
                using (var cmd = new SqlCommand(
                    "DECLARE @r int; EXEC @r = sys.sp_getapplock @Resource = N'CACC_ESCRITURA_PAGOS', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000; SELECT @r;",
                    c, tx))
                {
                    int r = Convert.ToInt32(cmd.ExecuteScalar());
                    if (r < 0) throw new InvalidOperationException("La compuerta no obtuvo el bloqueo: " + r);
                }
                var reloj = Stopwatch.StartNew();
                tomada.Set();
                liberar.Wait(maxMs);
                tx.Commit();
                return reloj.Elapsed.TotalMilliseconds;
            }, TaskCreationOptions.LongRunning);

        // Cuántas sesiones con ese prefijo de Application Name están conectadas a la base.
        public static int Conectadas(string cadena, string baseDatos, string prefijo)
        {
            using var c = new SqlConnection(cadena);
            c.Open();
            using var cmd = new SqlCommand("SELECT COUNT(*) FROM sys.dm_exec_sessions WHERE database_id = DB_ID(@base) AND program_name LIKE @p + '%';", c);
            cmd.Parameters.AddWithValue("@base", baseDatos);
            cmd.Parameters.AddWithValue("@p", prefijo);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        // Quiénes esperan ahora el bloqueo de cobros: el Application Name de cada sesión en espera, separados por coma.
        public static string Esperando(string cadena, string baseDatos)
        {
            using var c = new SqlConnection(cadena);
            c.Open();
            using var cmd = new SqlCommand(@"SELECT ISNULL(STRING_AGG(s.program_name, ','), '') FROM sys.dm_tran_locks l
                JOIN sys.dm_exec_sessions s ON s.session_id = l.request_session_id
                WHERE l.resource_type = 'APPLICATION' AND l.request_status = 'WAIT' AND l.resource_database_id = DB_ID(@base);", c);
            cmd.Parameters.AddWithValue("@base", baseDatos);
            return Convert.ToString(cmd.ExecuteScalar());
        }
    }

    public static class Tecnicas
    {
        // K sesiones reales arrancan juntas (Barrier) y piden N valores de la secuencia cada una. Devuelve todos los valores.
        public static long[] ValoresDeSecuencia(string cadena, string secuencia, int sesiones, int porSesion)
        {
            var todos = new ConcurrentBag<long>();
            using var barrera = new Barrier(sesiones);
            var hilos = Enumerable.Range(0, sesiones).Select(_ => new Thread(() =>
            {
                using var c = new SqlConnection(cadena + "Pooling=false;");
                c.Open();
                using var cmd = new SqlCommand($"SELECT NEXT VALUE FOR {secuencia};", c);
                barrera.SignalAndWait();
                for (int i = 0; i < porSesion; i++) todos.Add(Convert.ToInt64(cmd.ExecuteScalar()));
            }) { IsBackground = true }).ToList();
            hilos.ForEach(h => h.Start());
            hilos.ForEach(h => h.Join());
            return todos.ToArray();
        }

        // Una conexión setea idUsuario/origen y se cierra; la siguiente apertura con la misma cadena reutiliza la MISMA sesión
        // física del pool (mismo SPID) y lee el contexto sin setearlo. Devuelve "spid1|spid2|idUsuario|origen".
        public static string ContextoTrasReutilizar(string cadena)
        {
            int spid1;
            using (var c = new SqlConnection(cadena))
            {
                c.Open();
                using var cmd = new SqlCommand(
                    "EXEC sys.sp_set_session_context @key=N'idUsuario', @value=777; EXEC sys.sp_set_session_context @key=N'origen', @value=N'PortalDeportivo'; SELECT @@SPID;", c);
                spid1 = Convert.ToInt32(cmd.ExecuteScalar());
            }
            using (var c = new SqlConnection(cadena))
            {
                c.Open();
                using var cmd = new SqlCommand(
                    "SELECT CONCAT(@@SPID, '|', ISNULL(CONVERT(nvarchar(20), SESSION_CONTEXT(N'idUsuario')), 'NULL'), '|', ISNULL(CONVERT(nvarchar(50), SESSION_CONTEXT(N'origen')), 'NULL'));", c);
                return spid1 + "|" + cmd.ExecuteScalar();
            }
        }

        // Muchos hilos sobre un pool CHICO (máx. 4 conexiones, reutilización constante). Cada vuelta: la mitad de las veces setea
        // un operador/origen propio y la otra mitad no setea nada (como el Deportivo sin usuario). Lo leído tiene que ser lo
        // propio o NULL, nunca lo de otra vuelta. Devuelve "vueltas|reutilizaciones|contaminadas|detalle".
        public static string ContextoBajoConcurrencia(string cadena, int hilos, int vueltas)
        {
            int total = 0, contaminadas = 0;
            var spids = new ConcurrentDictionary<int, int>();
            var detalle = new ConcurrentBag<string>();
            using var barrera = new Barrier(hilos);
            var lista = Enumerable.Range(0, hilos).Select(h => new Thread(() =>
            {
                var azar = new Random(h * 7919);
                barrera.SignalAndWait();
                for (int v = 0; v < vueltas; v++)
                {
                    using var c = new SqlConnection(cadena + "Max Pool Size=4;");
                    c.Open();
                    bool setea = azar.Next(2) == 0;
                    int id = 100000 + h * 1000 + v;
                    string origen = "T" + id;
                    if (setea)
                    {
                        using var s = new SqlCommand("EXEC sys.sp_set_session_context @key=N'idUsuario', @value=@id; EXEC sys.sp_set_session_context @key=N'origen', @value=@o;", c);
                        s.Parameters.AddWithValue("@id", id);
                        s.Parameters.AddWithValue("@o", origen);
                        s.ExecuteNonQuery();
                    }
                    using var q = new SqlCommand("SELECT @@SPID, ISNULL(CONVERT(nvarchar(20), SESSION_CONTEXT(N'idUsuario')), ''), ISNULL(CONVERT(nvarchar(50), SESSION_CONTEXT(N'origen')), '');", c);
                    using var rd = q.ExecuteReader();
                    rd.Read();
                    spids.AddOrUpdate(rd.GetInt16(0), 1, (_, n) => n + 1);
                    string leidoId = rd.GetString(1), leidoOrigen = rd.GetString(2);
                    bool ok = setea ? (leidoId == id.ToString() && leidoOrigen == origen) : (leidoId == "" && leidoOrigen == "");
                    Interlocked.Increment(ref total);
                    if (!ok)
                    {
                        Interlocked.Increment(ref contaminadas);
                        if (detalle.Count < 5) detalle.Add($"hilo {h} vuelta {v} setea={setea}: leyó '{leidoId}'/'{leidoOrigen}'");
                    }
                }
            }) { IsBackground = true }).ToList();
            lista.ForEach(t => t.Start());
            lista.ForEach(t => t.Join());
            int reutilizaciones = spids.Values.Sum() - spids.Count;
            return $"{total}|{reutilizaciones}|{contaminadas}|{string.Join("; ", detalle)}";
        }
    }
}
