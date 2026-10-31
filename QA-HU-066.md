# QA HU-066: concurrencia e integridad de datos con los dos sistemas escribiendo en la base

**Fecha:** 31/10/2026 · **Rama:** `FramD` (cambios sin commitear) · **Base:** `ClubCamionerosUnificadaV2_HU059_Prueba` (LocalDB, recreada en cada corrida)
**Fecha de negocio:** 2026-10-31 (día ≥ 10: la cuota de octubre ya corresponde, ver §6)

**Resultado: PASS.** **51 PASS · 0 FAIL · 0 BLOCKED** en 4 corridas completas, cada una desde un reset de HU-059:

- 3 corridas antes de corregir el runner;
- 1 de regresión, después de corregirlo.

En total son 2240 operaciones HTTP en 352 ráfagas simultáneas, más las pruebas técnicas de SQL.

- No se encontraron defectos nuevos en el Administrativo.
- Las garantías de unicidad las da la base (índices únicos), no un `if (!exists) insert`.
- Lo único que falla en la regresión es el bug conocido BUG-059-1 (auditoría de la baja del Deportivo). Ver §15.

```
pwsh -File qa\HU-066\concurrencia_hu066.ps1 -Evidencia qa\HU-066\evidencia_ejecucionN.json
```

Código de salida: 0 = todo PASS · 1 = algún FAIL o BLOCKED · 2 = el entorno no se pudo preparar.

---

## 1. Objetivo

Comprobar, con operaciones **realmente simultáneas** y por las **APIs reales**, que el Administrativo y el Deportivo pueden escribir a la vez en la base unificada sin:

- IDs repetidos;
- personas o jugadores duplicados;
- cuotas emitidas de más o de menos;
- pagos perdidos o cobrados dos veces;
- errores de trigger;
- auditoría con el origen equivocado;
- huérfanos.

No se rediseñó nada. Primero se midió el comportamiento actual.

## 2. Arquitectura de la prueba

```
                 concurrencia_hu066.ps1  (orquesta, verifica por SQL, informa)
                   |  Concurrencia.cs (Add-Type): Rafaga + Barrier, Monitor, Compuerta, Tecnicas
     HTTP + JWT    |                                 HTTP + JWT
        v          |                                     v
 API Administrativa :5428 <--- HTTP + X-Integration-Key --- API Deportiva :5429
 (Application Name=CACC-Administrativo)                  (Application Name=CACC-Deportivo)
 + 1 o 2 generadores mensuales :5441/5442 (mismo ApiGestion.dll, GeneracionCuotas:Habilitada=true,
   Application Name=CACC-Administrativo-Generador)
        \                                                   /
         +---------------- SQL Server LocalDB ----------------+
          ClubCamionerosUnificadaV2_HU059_Prueba (HU-053 + 25 migraciones + fixtures HU-059 + 04_fixtures_hu066.sql)
```

- Las dos APIs son los binarios reales:
  - `ApiGestion.dll` de este repo. La suite se niega a correr si la DLL es más vieja que el código.
  - `CACC.API.dll` del clon de HU-059 (afa4e60 + parches del banco `cacc-unificacion`).
- Cada sistema se conecta con su propio `Application Name`, así el monitor sabe quién bloquea a quién.
- Los puertos son por parámetro. Las corridas usaron 5428/5429/5441 porque 5418/5419/5431 seguían tomados por APIs de una corrida anterior que había fallado (ver §12, R-2).

## 3. Versión de SQL Server

`Microsoft SQL Server 2025 (RTM-CU3) 17.0.4025.3 Express Edition (64-bit)`, LocalDB `MSSQLLocalDB`. LocalDB arranca solo con la primera conexión y se detiene cuando queda sin uso, así que "Detenido" fuera de una corrida es normal. Si no hay SQL Server, la suite corta en el reset con exit 2 y nunca informa PASS. Suite: `Microsoft.Data.SqlClient` 7.0.0.0, el mismo que usa la API.

## 4. Base HU-059

`qa/HU-059/reset_base.ps1` recrea la base en cada corrida con lo siguiente:

- esquema unificado;
- 25 migraciones;
- cuentas ficticias;
- fixtures 9859xxxx.

Después, `qa/HU-066/datos/04_fixtures_hu066.sql` agrega 20 personas sin jugador (DNI 9869xxxx) para el Caso 3.

Estado inicial verificado: `5|4|5|0|1|1`, más 20 personas sin jugador.

## 5. Dependencias (estado real al 31/10)

| Dependencia | Estado | Evidencia |
|---|---|---|
| HU-053 unificación + SEQUENCE | **Disponible** | `SEQ-0`: `JUGADORES.PK_id_jugador` y `PAGOS.PK_id_pago` con `DEFAULT NEXT VALUE FOR`, `cache=0`. El resto de las PK, IDENTITY. No queda ningún `MAX+1` en el código de las dos APIs (búsqueda en `DaoLibrary` y `CACC.DAO`). |
| HU-055 credencial API→API | **Disponible** | `DEP-2`: sin `X-Integration-Key` → 401. Todas las altas del Deportivo usan el flujo autenticado real. |
| HU-056 alta desde Deportes | **Disponible** | `DEP-1`: `POST /api/integracion/v1/jugadores` responde 400 con `errorCode`. Casos 1 a 3. |
| HU-057 baja | **Disponible en el Administrativo; el Deportivo no lo usa** | El Deportivo da de baja con `UPDATE` directo (BUG-059-1). Se incluye igual en C6, como escritura concurrente sin bloqueo de cobros. |
| HU-059 entorno compartido | **Disponible** | Regresión 35/1/0 (§16). |
| HU-049 generación automática | **Disponible** | `GeneradorCuotasMensuales` en procesos reales (C4, C5, C6). |
| HU-009 / cuotas faltantes (generación manual) | **Disponible** | `POST /api/cuotas-faltantes/{año}/{mes}/generar` (C5, C6). |
| HU-025 registro de pagos | **Disponible** | `POST /api/pagos/registrar` y `/api/pagos/cobro` (C4, C6, C7). |
| HU-063 consistencia de pagos | **Incompleta en esta rama**: está en `main` (PR #103, `8036a81`) pero no en `FramD` | No bloquea: HU-066 prueba los pagos por la API directamente. |
| HU-040 / HU-041 auditoría | **Disponible** | 14 triggers activos; `origen_sistema` (V20261028_01). |
| HU-083 calendario (corte día 10) | **Disponible** | `CalendarioCuotas.DiaDeCorte`, `SqlCalendarioCuotas`. |
| HU-084 / `BloqueoEscrituraPagos` | **Disponible** | `DaoLibrary/BloqueoEscrituraPagos.cs`, usado en 13 transacciones. |
| Triggers / migraciones | **Disponible** | `triggers=14`, `triggersDeshabilitados=0`, `fkDeshabilitadasONoConfiables=0`, antes y después de cada caso. |

**Bloqueadas:** ninguna.

## 6. Nivel de aislamiento y fecha de negocio

- **Base:** `is_read_committed_snapshot_on = 0`, `snapshot_isolation_state = OFF` → **READ COMMITTED con bloqueos**.
- **Sesiones:** el monitor leyó `transaction_isolation_level = 2` (READ COMMITTED) en todas las sesiones: Administrativo, generadores, Deportivo y la suite.
- Las transacciones que lo necesitan escalan de forma **local**:
  - `UPDLOCK, HOLDLOCK` en `PlayerDao`, `EnrollmentDao` y `ArancelesDao`;
  - `Serializable` en `AccountAccessDao` y `DiscountDao`;
  - el applock `CACC_ESCRITURA_PAGOS`.
- **No se cambió ningún nivel de aislamiento.**
- **Fecha de negocio:**
  - `RelojNegocio` y `SqlReloj` usan la hora de Argentina. La suite la lee de SQL Server (`SYSDATETIMEOFFSET() AT TIME ZONE 'Argentina Standard Time'`).
  - El 31/10 es posterior al corte del día 10 (HU-083): la cuota de octubre es exigible y el escenario alta + generación del mismo período tiene sentido sin tocar el reloj. **No se modificó la fecha de la máquina.**
  - **Si se corre entre el día 1 y el 9,** los casos que esperan la cuota del mes en curso darían FAIL por la regla vigente, no por un defecto.

## 7. Mecanismos que evitan la duplicación (documentados y después probados)

| Riesgo | Mecanismo | Dónde |
|---|---|---|
| ID repetido | `SEQUENCE NO CACHE` (JUGADORES, PAGOS) + IDENTITY (resto) | V20261011_04/05 |
| Dos PERSONAS con el mismo DNI | `UX_PERSONAS_dni` (único filtrado) + `UPDLOCK, HOLDLOCK` en la búsqueda del alta | esquema; `PlayerDao.cs:180`, `:263` |
| Dos JUGADORES por persona | `UX_JUGADORES_persona` | esquema |
| Cuota pendiente duplicada | `UX_PAGOS_CuotaPendiente_Jugador_Periodo` (jugador, vencimiento, concepto; `estado=0`) | esquema |
| Inscripción duplicada | `UX_PAGOS_InscripcionPendiente_Periodo` | esquema |
| Dos períodos abiertos | `UX_JUGADORES_PERIODOS_ABIERTO` | esquema |
| Deadlock entre escrituras de PAGOS | `BloqueoEscrituraPagos`: `sp_getapplock` exclusivo, dueño = transacción, 30 s, **primer paso** de cada transacción que escribe cuotas o pagos | `DaoLibrary/BloqueoEscrituraPagos.cs` |
| Cobro doble | applock + revalidación del estado dentro de la transacción ("ya fueron abonadas") | `PagosDao`, `PaymentTransactionDao` |
| Email repetido | `UX_USUARIOS_email` | esquema |

Ninguna protección depende del frontend: todas las pruebas van directo a las APIs.

## 8. Fixtures y precondiciones

- **Fijas:** `04_fixtures_hu066.sql`.
- **Por iteración** (`Precondicion` en el runner): jugadores sin la cuota del mes y cuotas pendientes para cobrar.
  - Se insertan por SQL, porque son el estado **antes** de la prueba, con el mismo criterio que los fixtures de HU-059.
  - **Todas las operaciones que se prueban van por las APIs.**
- DNI de prueba: 9866xxxx y 9869xxxx. Ningún dato es de una persona real.

## 9. Mecanismo de paralelismo

- **`Hu066.Rafaga`:**
  - un hilo por pedido, con el `HttpRequestMessage` armado de antemano;
  - todos esperan en una `Barrier` y salen juntos;
  - se registran inicio, fin, duración, código y cuerpo.
  - **Dispersión de arranque máxima: 2,1 ms.** En las 352 ráfagas, todas las operaciones estuvieron en vuelo a la vez: la última arrancó antes de que terminara la primera.
- **`Hu066.Compuerta`** (ventana de carrera reproducible):
  - la suite toma el **mismo applock** que las APIs (`CACC_ESCRITURA_PAGOS`);
  - lo suelta cuando todas las operaciones que lo usan están esperando, o a los 3 s;
  - el orden lo decide SQL Server al liberarlo, no la prueba;
  - se usa en las iteraciones pares o impares de C2 a C7; las otras corren libres.
  - `-RetenerMs` (nuevo, §12 R-4) la sostiene un mínimo para que entre antes una operación que no toma el bloqueo.
- **`Hu066.Monitor`:** cada ~20 ms muestrea `sys.dm_exec_requests`, `sys.dm_exec_sessions` y `sys.dm_tran_locks`. Registra:
  - la cola en el applock;
  - las sesiones bloqueadas;
  - la espera más larga;
  - los tipos de espera;
  - quién bloquea a quién;
  - el aislamiento.
- **Generadores mensuales:** procesos reales del Administrativo con el generador habilitado. Se lanzan antes de la ráfaga y, con compuerta, se espera a que estén en la cola del applock.

## 10. Iteraciones y cargas

| Escenario | Iteraciones por corrida | Cargas | Total en 4 corridas |
|---|---|---|---|
| C1 altas distintas | 10 (4 operaciones c/u) | ráfagas de 10, 20 y 50 | 40 iter. + 12 ráfagas |
| C2 mismo DNI | 10 × 3 variantes | ráfagas de 10 y 20 del mismo DNI | 120 iter. + 8 ráfagas |
| C3 persona existente → jugador | 10 (3 operaciones c/u) | — | 40 |
| C4 alta + generación | 10 (8 operaciones + generador) | — | 40 |
| C5 generación + generación | 5 (2 generadores + 3 manuales) | — | 20 |
| C6 pago + generación | 5 (13 operaciones + generador) | — | 20 |
| C7 pago + pago | 10 (8 operaciones c/u) | 10, 20 y 50 abonos sobre una cuota | 40 iter. + 12 ráfagas |
| Técnicas SQL | SEQUENCE 16×100 ×2; contexto 16 hilos × 50 | — | 4 |

**Repetibilidad:**

- Cada corrida parte de un reset y de las mismas precondiciones.
- El **estado final de la base es idéntico** en las corridas 1, 2 y 3: personas 485, jugadores 434, pagos 790.
- En la 4 quedan 5 jugadores y 10 pagos menos. Son exactamente las 5 iteraciones de C2-3 en que ahora gana el staff: una persona sin jugador, así que no hay cuota ni inscripción.

## 11. Casos y resultados

Resultados iguales en las 4 corridas, salvo donde se indica.

| ID | Operaciones simultáneas | Resultado esperado | Obtenido | Estado |
|---|---|---|---|---|
| SEQ-TS ×2 | 16 sesiones × 100 `NEXT VALUE FOR` (SEQ_JUGADORES, SEQ_PAGOS) | 1600 distintos | 1600/1600 en las 4 | PASS |
| C1-1 | alta portal + alta Deportivo + staff Deportivo + staff que revierte | 201×3; una persona por DNI; jugador con categoría 3, cuota e inscripción; staff sin jugador; el que revierte no deja nada | 10/10 | PASS |
| C1-10/20/50 | 10/20/50 altas (portal / Deportivo / staff) | todas 201; N personas distintas | 10, 20 y 50 ×201; 0 estados incorrectos | PASS |
| SEQ-1 | — | `PK_id_jugador` distintos y ≤ `current_value` | **75 distintos de 75** (1605..1679) | PASS |
| C2-1 | portal + Deportivo, mismo DNI | una persona, un jugador, una cuota, una inscripción | 10/10. Ganó el Deportivo 10/10 (corrida 1), 8/10 (2), 1/10 (3), 10/10 (4); el otro: portal 400 "Ya hay una persona registrada" o Deportivo 200 `"creado": false` | PASS |
| C2-2 | staff Deportivo (SQL directo) + alta Deportivo | una persona, un jugador, un usuario | 10/10 (201/201: el alta reutiliza la persona del staff) | PASS |
| C2-3 | alta portal + staff Deportivo | una persona; si gana el portal, jugador; si gana el staff, 400 y persona sin jugador | 10/10. Corridas 1 a 3: siempre ganó el portal. **Corrida 4: 5 gana el staff, 5 gana el portal** (§12 R-4) | PASS |
| C2-10/20 | 10/20 altas del mismo DNI (mitad y mitad) | `1\|1\|0\|1\|1\|3` y **un solo 201** | un 201; el resto, 200 (reutiliza) o 400 (rechazo controlado); 0 5xx | PASS |
| C3-1 | persona existente sin jugador: 2 altas del Deportivo + 1 del portal | un jugador de ESA persona, categoría 3, una cuota y una inscripción | 10/10; códigos 200/201/400 | PASS |
| C4-1 | generador mensual + 3 altas portal + 3 Deportivo + 2 pagos adelantados del mes | exactamente UNA cuota de octubre para cada uno (6 sin cuota, 6 altas); los adelantados, abonados sin pendiente | 10/10 | PASS |
| C4-2 | — | superposición real con el generador | 6, 7, 7 y 5 de 10 (umbral ≥ 5; ver nota) | PASS |
| C5-1 | 2 generadores mensuales + 3 generaciones manuales del mes anterior | una cuota por jugador y período; suma de `creadas` = faltantes | 5/5; creadas 6/6, 4/4, 4/4, 4/4, 4/4 | PASS |
| C6-1 | generador + generación manual + 4 registrar + 3 cobros + 3 adelantados + 1 pago de cuota en emisión + baja del Deportivo | ningún pago perdido, ninguna cuota duplicada, ningún error | 5/5 en cada corrida; las 65 operaciones respondieron 200 | PASS |
| C6-2 | — | ningún pago llega al límite técnico más bajo (5 s) | máx. 257 / 93 / 284 / 369 ms | PASS |
| C7-1 | total ×2 · cobro ×2 · registrar + cobro · dos parciales de 400 | un 200 y un 400; cobrada UNA vez; parciales → saldo 200 | 10/10 | PASS |
| C7-10/20/50 | 10/20/50 abonos parciales de 1000/N sobre una cuota de $1000 | todos 200; saldada exacta | `0:0\|N:1000\|1000` | PASS |
| C*-INT | integridad después de cada caso | 0 problemas nuevos | 0 en los 7 casos × 4 corridas | PASS |

**Nota C4-2:**

- Con compuerta (5 de 10 iteraciones), el generador y las altas estuvieron en la cola del applock **a la vez** en todos los casos: ahí la superposición está garantizada.
- En las iteraciones libres, la cuota del generador cayó dentro de la ventana de la ráfaga solo en 0 a 2 de 5, porque el proceso tarda en arrancar.
- La evidencia fuerte de alta + generación son las 5 iteraciones con compuerta por corrida (20 en total).

## 12. Tiempos y bloqueos

**Duración por caso (ms)**, corridas 1 / 2 / 3 / 4:

| Caso | p50 | p95 | máx. |
|---|---|---|---|
| C1 | 198 / 142 / 118 / 117 | 461 / 466 / 470 / 414 | 490 / 500 / 501 / 497 |
| C1-carga (10+20+50) | 521 / 324 / 186 / 256 | 2101 / 1721 / 375 / 435 | **2337** / 1741 / 394 / 497 |
| C4 | 53 / 285 / 187 / 43 | 112 / 470 / 539 / 101 | 142 / 642 / 665 / 135 |
| C5 | 91 / 66 / 146 / 93 | 103 / 88 / 206 / 148 | 104 / 89 / 206 / 150 |
| C6 | 111 / 52 / 84 / 181 | 251 / 74 / 275 / 360 | 257 / 93 / 284 / 369 |
| C7 | 29 / 69 / 23 / 148 | 64 / 177 / 62 / 252 | 68 / 204 / 66 / 256 |
| C7-carga | 36 / 67 / 38 / 228 | 83 / 98 / 90 / 281 | 88 / 109 / 98 / 446 |
| C3 | 10 / 14 / 110 / 15 | 3050 / 3051 / 3140 / 3040 | 3063 / 3060 / 3148 / 3046 |

- **Los ~3 s de C3 son de la prueba, no del sistema.**
  - En las 5 ráfagas de C3 con compuerta, el monitor muestra como bloqueador a `HU066-Tecnica` (la compuerta).
  - Solo llegaron 2 de las 3 sesiones previstas a la cola del applock, así que la compuerta esperó su límite de 3 s.
  - Lo mismo pasa con los 1,5 s de C2-3 en la corrida 4: es la retención a propósito.
- **Bloqueo real del sistema más largo:**
  - **2337 ms** en la ráfaga de 50 altas (corrida 1): cola en el applock más range locks cruzados.
  - En ráfagas sin compuerta, la espera de bloqueo más larga fue de 1046 / 828 / 625 / 226 ms.
- **Límites técnicos de referencia** (no hay SLA de negocio):
  - el timeout del Deportivo hacia el Administrativo es de **5 s** (`AdministrativoApi__TimeoutSegundos`);
  - el applock y el `SqlCommand` esperan 30 s.
  - Ninguna operación llegó a 5 s. El margen más chico es de **2,1×**, en la ráfaga de 50 altas simultáneas (ver OBS-066-1).
- **Bloqueos entre sistemas observados** (son esperas, no errores):
  - Administrativo ← Deportivo (`LCK_M_RS_U`, `LCK_M_X`): range locks de la búsqueda del DNI contra los `INSERT PERSONAS` del Deportivo.
  - Deportivo ← Administrativo (`LCK_M_RIn_NL`, `LCK_M_X`, `LCK_M_S`).
- **`LCK_M_SCH_M`** (corridas 1 y 3, una vez cada una):
  - el monitor extendido lo identificó como `METADATA: METADATA_CACHE` sobre la consulta de validación del JWT (`SELECT 1 FROM USUARIOS WHERE PK_id_usuario = @id AND … token_version = @version`).
  - Es SQL Server refrescando metadatos o estadísticas de una base recién creada (también aparece `WAIT_ON_SYNC_STATISTICS_REFRESH`). Ninguna de las dos APIs hace DDL (búsqueda de `ALTER`, `TRUNCATE`, `CREATE INDEX` y `DISABLE TRIGGER`: 0).
- **Toda la instancia, por corrida:** 1439 / 1985 / 1849 / 1583 esperas `LCK_M_*`, por 79 / 84 / 75 / 77 s acumulados entre todas las sesiones.
- **Cola máxima en el applock:** 17 / 13 / 17 / 19.

## 13. Errores, deadlocks y triggers

| Búsqueda | Resultado (4 corridas) |
|---|---|
| Deadlocks: `xml_deadlock_report` en `system_health` desde el inicio de la corrida | **0** |
| Deadlocks en los logs (`1205`, `deadlock`, `interbloqueo`) | **0** |
| Política de reintentos ante deadlock | **No existe** en ninguna de las dos APIs. Por eso 0 significa **que nunca ocurrió**, no que se recuperó. |
| Timeouts | 0 |
| Vencimientos del applock ("Hay otra operación de cobro en curso") | 0 |
| Respuestas 5xx o sin respuesta | 0 de 2240 |
| Error 334 / `OUTPUT` sin `INTO` / "desencadenador habilitado" | **0** en los logs; **0** `OUTPUT INSERTED/DELETED` sin `INTO` en el código de las dos APIs (`TRG-2`) |
| `THROW 51010/51011` de los triggers de género | 0 |
| Violaciones de unicidad o de FK sin manejar | 0 |
| Secretos en los logs (credencial, clave JWT, contraseñas, tokens) | 0 |

**Triggers revisados** (14, todos habilitados antes y después):

- `ARANCELES`: TR_AUDIT_ARANCELES
- `ARANCELES_INSCRIPCION`: TR_AUDIT_ARANCELES_INSCRIPCION
- `AUDITORIA_CAMBIOS`: TR_AUDITORIA_IDENTIDAD_ACTOR
- `CATEGORIAS`: TR_AUDIT_CATEGORIAS
- `JUGADORES`: TR_AUDIT_JUGADORES, TR_JUGADORES_GENERO_OBLIGATORIO, TR_JUGADORES_PERIODOS
- `JUGADORES_DESCUENTOS`: TR_AUDIT_JUGADORES_DESCUENTOS
- `PAGOS`: TR_AUDIT_PAGOS
- `PERSONAS`: TR_AUDIT_PERSONAS, TR_PERSONAS_GENERO_DE_JUGADOR
- `TIPOS_DESCUENTO`: TR_AUDIT_TIPOS_DESCUENTO
- `TOKEN_ACCESO_CUENTA`: TR_AUDIT_TOKEN_ACCESO_CUENTA
- `USUARIOS`: TR_AUDIT_USUARIOS

Las escrituras de los dos sistemas sobre PERSONAS, JUGADORES, PAGOS y USUARIOS los atravesaron sin errores.

**Transacciones (granularidad revisada; no se cambió ninguna):**

- **Alta de jugador** (`PlayerDao`): una transacción con el applock como primer paso, que cubre:
  - la búsqueda del DNI con `UPDLOCK, HOLDLOCK`;
  - PERSONAS, JUGADORES, JUGADORES_CATEGORIAS y el período;
  - la inscripción y la cuota del mes.
- **Emisión de cuotas** (`CuotasFaltantesDao`, generador): una transacción con applock.
- **Pago** (`PagosDao`, `PaymentTransactionDao` vía `SqlTransactionRunner`): una transacción con applock que revalida el estado adentro.
- **Auditoría:** la escriben los triggers, dentro de la misma transacción que el cambio. Si la operación revierte, el evento revierte con ella (AUD-6).
- Son atómicas donde tienen que serlo. Agrandarlas no aportaría nada y dividirlas rompería la atomicidad.

## 14. Integridad referencial y de negocio

Se ejecuta antes y después de cada caso (`$sqlIntegridad` en el runner). Los huérfanos se cuentan por **cada FK de una columna del esquema real** con SQL dinámico (30 FK; 0 FK multicolumna). Además se verifica:

- `jugadoresSinPersona`, `jugadoresCategoriaInvalida`, `pagosSinJugador`, `usuariosSinPersona`;
- `dniDuplicado`, `jugadorDuplicadoPorPersona`;
- `cuotaPendienteDuplicada`, `cuotaSobreemitida` (suma de `monto_final` > `monto_base` por jugador y mes), `inscripcionSobreemitida`;
- `periodoAbiertoDuplicado`, `activoSinPeriodo`;
- `auditoriaFantasma`: un INSERT auditado de una entidad que no existe y no tiene DELETE;
- `fkDeshabilitadasONoConfiables`, `triggersDeshabilitados`;
- secuencias por delante del máximo de cada PK.

**Línea de base (INT-0):** 0 en todo. No hay huérfanos históricos en la base de prueba.

**Después de cada uno de los 7 casos, en las 4 corridas:** **0 problemas nuevos**, 0 huérfanos de FK y secuencias OK.

**FK:**

- **No se deshabilitó ninguna** (`NOCHECK` no aparece en el runner).
- `fkDeshabilitadasONoConfiables = 0` al final.

**Pérdida de registros:** no alcanza con los códigos HTTP. Se verifican los valores finales:

- cada DNI: personas, jugadores, usuarios, cuota del mes, inscripción y categoría;
- cada cuota: pendientes y saldo, abonos y suma, y base;
- `saldo + abonos = base` en todos los pagos parciales simultáneos. En C7-50, 50 abonos de $20 dejan la cuota saldada exacta: no se perdió ninguna actualización.

## 15. Auditoría

| ID | Verificación | Resultado |
|---|---|---|
| AUD-1 | Ningún evento con origen `PortalDeportivo` lleva otro operador que el del token del Deportivo | 0 incorrectos |
| AUD-2 | Los pagos del Administrador 30000005 quedan con origen `PortalAdministrativo` y ese operador, aunque corrieron mezclados con pedidos de integración | 0 \| 0 |
| AUD-3 | Las altas por integración quedan `PortalDeportivo`; las del portal, `PortalAdministrativo` + SuperAdmin | 0 \| 0 |
| AUD-4 | Cuotas que emitió SOLO el generador (100 jugadores): un evento cada una, `PortalAdministrativo`, sin operador (no heredan el operador de otra conexión) | 0 incorrectos; 100 eventos |
| AUD-5 | Las escrituras directas del Deportivo (staff) no heredan un origen de otra conexión | 0 (quedan `NULL`, ver abajo) |
| AUD-6 | 10 staff que insertan PERSONAS y fallan en USUARIOS (`UX_USUARIOS_email`) → rollback: no queda ni la persona ni su evento | 0 fantasmas; 0 personas |
| CTX-1 | La misma sesión física (mismo SPID) reutilizada del pool no conserva `idUsuario` ni `origen` | `NULL\|NULL` |
| CTX-2 | Pool de 4 conexiones, 16 hilos, la mitad setea operador y origen y la otra mitad no | 800 aperturas, 796 reutilizaciones, **0 contaminadas** |

**Cómo se setea el contexto:**

- **Administrativo:** `SqlConnectionFactory.Open` escribe **siempre** `idUsuario` y `origen` en cada apertura, aunque sean NULL.
- **Deportivo:** `ConexionAuditada.OpenAuditadaAsync` solo escribe `idUsuario` cuando hay usuario y nunca `origen`. Depende de que `sp_reset_connection` limpie el `SESSION_CONTEXT` de la conexión que se reutiliza, y CTX-1 y CTX-2 demuestran que lo hace.

**AUD-6 es un rollback real:**

- El 409 del Deportivo sale de su `catch (SqlException 2627/2601)` en `StaffController`.
- Llega **después** de que el `INSERT PERSONAS` pasó y el `INSERT USUARIOS` chocó con el índice.
- El log del Deportivo no registra la excepción, por eso el patrón `UX_USUARIOS_email` cuenta 0.

**Resumen de eventos** (corrida 1): `PortalAdministrativo`/SuperAdmin 348, `PortalAdministrativo`/Administrador 394, `PortalAdministrativo`/sin operador 198 (generador), `PortalDeportivo`/SuperAdmin 386, `NULL`/sin operador 589, `NULL`/SuperAdmin 217.

- Los `NULL` son las precondiciones que inserta la suite por SQL y las escrituras directas del Deportivo: staff y baja.
- `NULL` significa "no pasó por la API del Administrativo" (V20261028_01). **No se mezcló ningún origen entre operaciones concurrentes.**

## 16. Bugs y observaciones

**Defectos del producto encontrados por HU-066: ninguno.**

| ID | Tipo | Descripción | Vincular a |
|---|---|---|---|
| BUG-059-1 (existente) | Bug abierto | El Deportivo escribe directo en la base y no sella `origen`: la baja (`UPDATE JUGADORES SET fecha_baja`) y el alta de staff quedan con origen `NULL`. HU-066 lo confirma bajo concurrencia (AUD-5, BJD-2 de HU-059). No hay contaminación, pero el origen no es positivo. Se resuelve en el código del Deportivo. | **HU-066** (nuevo vínculo), HU-057, HU-040 |
| OBS-066-1 | Observación (rendimiento, no defecto) | `PlayerDao.cs:180` y `:263` buscan `WHERE LTRIM(RTRIM(Dni)) = @dni WITH (UPDLOCK, HOLDLOCK)`. La función sobre la columna impide el seek en `UX_PERSONAS_dni`: el range lock cubre el índice entero y serializa todas las altas contra los `INSERT PERSONAS` del Deportivo. Con 50 altas simultáneas hubo 2,3 s de espera (margen de 2,1× sobre el timeout de 5 s del Deportivo). Opción: guardar el DNI ya normalizado y buscar `dni = @dni`, sin función sobre la columna. **No se cambió**: el resultado es correcto y el cambio corresponde a HU-053/HU-056. | HU-066, HU-056 |
| OBS-066-2 | Observación (política) | Asimetría con un DNI que ya existe: el portal **rechaza** (400 "Ya hay una persona registrada"), mientras que el Deportivo **reutiliza** la persona (201 o 200 `"creado": false`). Las dos respuestas son controladas y respetan la unicidad. Se documenta para que sea una decisión explícita. | HU-056 |

**Los bugs no se cargaron en Jira desde acá: no hay acceso a Jira en esta sesión.** Pendiente para el equipo:

1. vincular BUG-059-1 a HU-066;
2. registrar OBS-066-1 y OBS-066-2 si se quieren seguir como tareas.

**Correcciones del runner** (herramienta de prueba, no producto). Hechas el 31/10, antes de dar por buena cualquier evidencia:

- **R-1 · PowerShell 7.6.0:** `@($lista)` sobre una `List[object]` tira `Argument types do not match`.
  - Cortaba la corrida en C4 (`TerminarGeneradores`) y en el `finally`.
  - Es la causa de que la corrida del 30/10 no terminara.
  - Se reemplazó por `.ToArray()`.
- **R-2 · procesos vivos:**
  - por R-1, las APIs y el generador quedaban corriendo en 5418/5419/5431;
  - la corrida siguiente le hablaba al proceso viejo (un PASS de ENV-1 contra otro binario).
  - Ahora el `finally` detiene cada proceso aunque falle otro, y la suite **no arranca** (exit 2) si un puerto de prueba está ocupado.
- **R-3 · DL-1:** la XPath `""xml_deadlock_report""` se rompía al pasarla a `sqlcmd -Q` en pwsh 7.6 (FAIL falso). Ahora usa `''xml_deadlock_report''`.
- **R-4 · C2-3:**
  - suponía que el portal rechaza con 409; el portal responde 400. Si el staff hubiera ganado, la prueba daba un FAIL falso.
  - Además, el staff hashea la contraseña antes de escribir, así que con la compuerta normal **nunca** ganaba: la rama "gana el staff" no se ejercitaba.
  - Se agregó `-RetenerMs` (1,5 s en las iteraciones con compuerta). Desde la corrida 4 se ejercitan las dos ramas: 5 y 5.
- **R-5 · diagnóstico:**
  - el `catch` y un `trap` imprimen el tipo, la línea y el stack;
  - el monitor registra el recurso y la sentencia de los locks de esquema (así se identificó el `SCH_M` de §12).

## 17. Regresión

| Suite | Resultado | Evidencia |
|---|---|---|
| HU-066 completa, después de R-1 a R-5 | **51 PASS · 0 FAIL · 0 BLOCKED** | `qa/HU-066/salida_ejecucion4.txt`, `evidencia_ejecucion4.json` |
| HU-066 completa, confirmación (corrida 5, 12:57, con LocalDB recién arrancado a mano) | **51 PASS · 0 FAIL · 0 BLOCKED**; estado final idéntico al de la corrida 4 (485 / 429 / 780); 0 deadlocks, 0 errores SQL | `qa/HU-066/salida_ejecucion5.txt`, `evidencia_ejecucion5.json` |
| HU-059 E2E (`-PuertoAdministrativo 5438 -PuertoDeportivo 5439`) | **35 PASS · 1 FAIL · 0 BLOCKED**, igual que la línea de base del 29/10. El FAIL es BJD-2 = BUG-059-1 | `qa/HU-066/salida_regresion_hu059.txt` |

- En un primer intento de HU-059 con `-PuertoDeportivo 5429`, AUT-6 falló porque su Deportivo de credencial equivocada usa `PuertoDeportivo + 2` = 5431, ocupado por el generador que había quedado vivo (`Failed to bind … 5431: address already in use`). No es una regresión: con puertos libres pasa.
- **No se modificó código de producción.** Por eso no hay suites unitarias de funcionalidades modificadas para correr.
- La prueba técnica de HU-053 (`database/unificacion/pruebas/concurrencia_ids.ps1`, 30/10) queda como evidencia complementaria en `qa/HU-066/salida_tecnica_hu053_ids.txt`:
  - con SEQUENCE o IDENTITY, 8 sesiones × 50 altas → 400 IDs distintos en 9 tablas;
  - con `MAX+1`, **349 de 400 altas rechazadas** por PK duplicada.

## 18. Criterios de aceptación

| Criterio | Resultado |
|---|---|
| Todos los casos sobre HU-059 (SQL Server real, APIs reales, contratos reales) | ✅ |
| Operaciones realmente concurrentes | ✅ Barrier + compuerta; dispersión ≤ 2,1 ms; 352/352 ráfagas superpuestas |
| Altas simultáneas sin IDs duplicados | ✅ 75/75 por corrida; 1600/1600 por secuencia |
| SEQUENCE funcionando en los puntos de HU-053 | ✅ SEQ-0, SEQ-TS, SEQ-1 |
| Mismo DNI concurrente sin identidades duplicadas | ✅ C2 (3 variantes + ráfagas de 20), `dniDuplicado = 0` |
| Alta + generación sin cuotas duplicadas ni faltantes | ✅ C4-1 40/40 |
| Generación + generación | ✅ C5-1 20/20 |
| Pago + generación sin pagos perdidos ni errores | ✅ C6-1 20/20 |
| Tiempos de bloqueo registrados | ✅ §12 |
| Sin errores de trigger 334 | ✅ 0 en logs y 0 en código |
| La auditoría distingue el origen | ✅ AUD-1 a AUD-4; las escrituras directas del Deportivo quedan `NULL` (BUG-059-1, existente) |
| Sin cuotas, pagos ni jugadores huérfanos | ✅ 0 por FK y por chequeo explícito |
| Sin registros perdidos | ✅ valores finales verificados; estado final determinista |
| Escenarios repetidos varias veces | ✅ 4 corridas × 5 a 10 iteraciones + cargas de 10, 20 y 50 |
| Bugs vinculados | ⚠️ Preparados (§16); falta cargar el vínculo en Jira |
| QA documentado | ✅ este archivo |

## 19. Conclusión

**HU-066: PASS.** La base unificada aguanta escrituras simultáneas de los dos sistemas sin IDs repetidos, sin identidades duplicadas, sin cuotas de más ni de menos, sin cobros dobles, sin pérdidas, sin deadlocks y sin errores de trigger. El origen de la auditoría no se contamina entre conexiones del pool.

La robustez viene de la base:

- índices únicos filtrados;
- SEQUENCE;
- un applock que ordena las escrituras de PAGOS;
- transacciones atómicas que revalidan dentro.

No depende de chequeos previos en el código ni del frontend.

Quedan tres pendientes, ninguno bloqueante para HU-066:

1. **BUG-059-1** en el Deportivo (origen `NULL`);
2. el **margen de 2,1×** con 50 altas simultáneas (OBS-066-1);
3. cargar los vínculos en Jira.
