# QA HU-064: el pago de un jugador con deuda actualiza su habilitación en tiempo real

**Fecha:** 04/11/2026 · **Entorno:** HU-115 (local combinado, LocalDB) · **Ejecución:** 20:59 UTC

## Resultado

| Qué | Resultado |
|---|---|
| Comportamiento funcional (9 casos, en los dos sistemas) | **PASS 9/9.** Repetido completo en una segunda corrida con fixtures nuevos (regresión) |
| Integración tal como está en las ramas (`FramD` + fork `feat/base-unificada` + `cacc-unificacion`) | **FAIL**: dos bugs bloqueantes (BUG-1 y BUG-2). Los casos pasan solo con los workarounds de §1.3 |
| **HU-064** | **No se puede cerrar todavía.** La regla y la inmediatez funcionan, pero la DoD exige que "Administrativo y el fork funcionen juntos", y sin los workarounds no funcionan. Falta corregir BUG-1 (SCRUM-804) y BUG-2 (SCRUM-805) y volver a correr `hu064-e2e.ps1` sin workarounds |

---

## 1. Entorno HU-115

### 1.1 Repositorios

| Pieza | Repositorio y rama | Commit |
|---|---|---|
| Portal Administrativo | `Atzur1/Sistema-Administrativo---CACC`, `FramD` | `f9d4514`, **más cambios sin commitear** de HU-096, HU-090 y HU-087 (copiados tal cual a la carpeta de trabajo) |
| Portal Deportivo (fork) | `Atzur1/App-Club-Atletico-Camioneros`, **`feat/base-unificada`** | `4421b1d` (04/11/2026) |
| Unificación | `Atzur1/cacc-unificacion`, `feat/HU-062-estado-deportivo` (la más nueva; `main` está en `c6e423f` del 21/10) | `61e1030` |

Clones nuevos en `C:\Users\FramD\Documents\Tesis\HU-064-entorno\`. No se tocaron los clones que ya había (el del Deportivo apunta a `NejoCHZ`, está en otra rama y tiene cambios sin commitear).

### 1.2 Configuración

- **Armado:** `cacc-unificacion\scripts\armar.ps1 -Servidor "(localdb)\MSSQLLocalDB" -Recrear -Iniciar`. Copia los backends a `trabajo/`, que es descartable; los repos no se modifican.
- **Base:** `ClubCamionerosUnificadaV2_Prueba` en **LocalDB**. En esta máquina no hay SQL Server como servicio y Docker está detenido: es un desvío del README, que pide SQL Server local.
- **APIs:** Administrativo en `http://localhost:5318` y Deportivo en `http://localhost:5319`, en `Development`. Comparten una clave JWT aleatoria por armado. El Deportivo llama al Administrativo por `AdministrativoApi:BaseUrl`, con timeout de 3 s.
- **Herramientas:** .NET SDK 10.0.400 (compila `net9.0`), `sqlcmd` (ODBC 17) y `patch.exe` de Git.
- **Cuentas:** SuperAdmin `20111222` (opera en el Administrativo) y Director Técnico `30000004`, de la categoría 3 (consulta en el Deportivo).
- **Línea base** (`probar.ps1` antes de los workarounds): **79 pasan y 47 fallan**. La mayoría de las fallas son 401 de integración (BUG-2). Hay 2 de aranceles porque `probar.ps1` no está al día con HU-106 (O-2).

### 1.3 Workarounds aplicados (solo en la copia `trabajo/`)

| # | Workaround | Motivo |
|---|---|---|
| W-1 | Se renombró `V20261031_01__historial_exportaciones_reportes.sql` a `V20261031_02__…` y se recompiló | BUG-1: con la versión duplicada la API del Administrativo no arranca |
| W-2 | Se aplicó `ellos-credencial-integracion.patch` (de la copia local de `cacc-unificacion` del 28/10) al Deportivo y se configuró una clave de integración compartida, aleatoria por armado | BUG-2: sin eso el Deportivo informa "SIN VERIFICAR" para todos |
| W-3 | La clave se pasa por **variables de entorno** (`Integracion__ClienteDeportivo__Clave` y `AdministrativoApi__ClaveIntegracion`), no por `appsettings` | El `ApiGestion` copiado conserva su `UserSecretsId`, y en `Development` los user-secrets locales pisan `appsettings.Development.json` (la clave no coincidía y respondía "Credencial de sistema inválida") |

## 2. Dependencias: estado real

| Dependencia | Estado |
|---|---|
| HU-115 entorno combinado | Operativo **con W-1, W-2 y W-3** |
| HU-028 servicio de habilitación | Funciona (§3) |
| HU-025 / HU-063 pagos | `POST api/pagos/cobro` y `POST api/pagos/registrar` funcionan y el estado se refleja enseguida |
| Anulación | `POST api/pagos/{id}/anular` funciona |
| HU-049 / HU-091 cuotas faltantes | Funciona, pero necesita un arancel vigente en el mes. La base unificada se entrega **sin aranceles** (§5) |
| HU-083 vencimiento | Comprobado en vivo: una cuota emitida y no vencida no restringe (caso 4, estado inicial) |
| Beneficios | Funciona con el criterio de prevención acordado (§4) |
| Autenticación entre sistemas | JWT compartido: OK. Credencial de sistema HU-055: **rota** en el fork (BUG-2) |

## 3. Regla efectiva y fuente única

- **Fuente única:** `DaoLibrary/EstadoDeudaDao` → `EstadoDeudaJugador.Estado` → `GET/POST api/integracion/v1/estado-deuda`. El Deportivo (`EstadoDeudaClient`) solo traduce el texto: `habilitado` → "AL DÍA", `solo_entrenamientos` → "SOLO ENTRENAMIENTOS", `inhabilitado` → "INHABILITADO".
- **Regla:** se cuentan las cuotas (no la inscripción) pendientes, con saldo > 0 ya ajustado por el beneficio y los abonos, y **vencidas**. Una cuota vence el día 10 del mes siguiente (HU-083). Con 0 → habilitado, 1 → solo entrenamientos, 2 o más → inhabilitado.
- **Caché:** no hay en ningún backend ni en el front deportivo (verificado en el código). **Confirmado en ejecución:** la consulta inmediata siempre devolvió el estado nuevo, sin limpiar caché, sin reiniciar y sin ningún job.
- **Consulta individual y por categoría:** `GET /api/jugadores/{id}` (→ `estado-deuda/dni`) y `GET /api/jugadores?categoriaId=3` (→ `estado-deuda/lote`). En los 9 casos coincidieron entre sí y con la fuente.
- **Privacidad (verificado en ejecución):** la fuente devuelve solo `dni, idJugador, estado, accesoPermitido, fechaBaja`, y la ficha que recibe el DT no trae montos, saldos, cuotas ni medios de pago.

## 4. Decisión sobre el caso de la beca

El criterio original ("una beca deja al jugador restringido en Al día") **no se puede cumplir con el flujo real**: `BeneficiosService` no deja becar cuotas ya vencidas ("becarlas sería condonar deuda"), y son justamente las vencidas las que restringen. La prueba equivalente de HU-062 inserta la cuota en $0 por SQL.

**Criterio acordado con el responsable: prevención a tiempo.** Una beca asignada por la API sobre la cuota emitida y todavía no vencida la deja en $0 a cobrar, el jugador sigue "Al día" y, por la regla (saldo > 0), esa cuota no va a contar cuando venza. **Lo que no se puede observar hoy** es el día del vencimiento (10/11/2026): el reloj de negocio es real y no se puede configurar.

## 5. Fixtures (corrida de regresión, DNI con sufijo aleatorio `897`)

- Alta real por el Deportivo (`POST /api/jugadores`, que pasa por la API de integración, HU-056), en la categoría 3, masculino.
- Por SQL, solo como preparación: se borran los cargos del alta, se cargan las cuotas **ya vencidas** (jul, ago, sep 2026 de $85.000), se retrotrae la `fecha_alta` de E al 01/07 y se carga un arancel histórico masculino ($85.000 desde 01/01/2026) para que el generador pueda emitir.
- **Las operaciones bajo prueba van siempre por la API real.**

| Jugador | DNI | Cuotas que cuentan | Estado inicial observado |
|---|---|---|---|
| A | 41897001 | ago, sep | INHABILITADO |
| B | 41897002 | sep | SOLO ENTRENAMIENTOS |
| C | 41897003 | jul, ago, sep | INHABILITADO |
| D | 41897004 | ninguna (oct emitida, no vencida, $85.000 pendiente) | AL DÍA |
| E | 41897005 | ninguna (alta 01/07, sin cuotas) | AL DÍA |
| F | 41897006 | ago, sep | INHABILITADO |
| P | 41897007 | sep | SOLO ENTRENAMIENTOS |

## 6. Casos ejecutados

Tiempos en UTC: T op = inicio de la operación; T conf = respuesta del Administrativo; T cons = envío de la siguiente consulta del Deportivo (sin pausa); T resp = estado recibido en la ficha. "Reflejo" es lo que tardó esa siguiente consulta; el listado se pidió justo después. En los casos con pago, `fecha_hora_registro` en la base cae siempre entre T op y T conf.

| ID | Jugador | Operación (API real) | Admin | T op | T conf | T cons | T resp | Reflejo ficha / listado | Persistido antes → después | Esperado | Ficha / listado / fuente | Resultado |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | A | `cobro` 1 de 2 vencidas | 200 | 20:59:04.094 | 04.156 | 04.156 | 04.216 | 60 / 102 ms | 2 pend → 1 pend + 1 pagada | SOLO ENTRENAMIENTOS | SOLO ENTR. / SOLO ENTR. / solo_entrenamientos | **PASS** |
| 2 | C | `cobro` de las 3 | 200 | 20:59:04.798 | 04.874 | 04.874 | 04.932 | 58 / 95 ms | 3 pend → 3 pagadas | AL DÍA | AL DÍA / AL DÍA / habilitado | **PASS** |
| 3 | B | `cobro` de la única | 200 | 20:59:05.520 | 05.586 | 05.586 | 05.658 | 72 / 87 ms | 1 pend → 1 pagada | AL DÍA | AL DÍA / AL DÍA / habilitado | **PASS** |
| 3b | P | `registrar` parcial de $40.000 (sep) | 200 | 20:59:06.198 | 06.253 | 06.253 | 06.323 | 70 / 102 ms | 1 pend → 1 pend (con saldo) + 1 abono | SOLO ENTRENAMIENTOS (queda saldo) | SOLO ENTR. / SOLO ENTR. / solo_entrenamientos | **PASS** |
| 4 | D | Beca 100% oct–dic por `POST api/players/{id}/discount` | 201 | 20:59:06.826 | 06.930 | 06.931 | 06.990 | 60 / 81 ms | Estado de cuenta: oct `amountDue=0`, `coveredByBenefit=True`, total $0 | AL DÍA (prevención, §4) | AL DÍA / AL DÍA / habilitado | **PASS** |
| 5a | E | `cuotas-faltantes/2026/9/generar` | 200 (5 creadas) | 20:59:09.336 | 09.420 | 09.420 | 09.484 | 63 / 86 ms | 0 → 1 pend | SOLO ENTRENAMIENTOS | SOLO ENTR. / SOLO ENTR. / solo_entrenamientos | **PASS** |
| 5b | E | `cuotas-faltantes/2026/8/generar` | 200 (5 creadas) | 20:59:09.977 | 10.052 | 10.052 | 10.116 | 65 / 93 ms | 1 → 2 pend | INHABILITADO | INHAB. / INHAB. / inhabilitado | **PASS** |
| 6 | F | `cobro` con una cuota válida y una inexistente | **400** (buscado) | 20:59:07.615 | 07.701 | 07.701 | 07.764 | 63 / 99 ms | 2 pend → **2 pend, 0 pagadas** (no cobró ninguna: rollback) | INHABILITADO (sin cambio) | INHAB. / INHAB. / inhabilitado | **PASS** |
| 7 | A | `anular` el pago #51 del caso 1 | 200 | 20:59:08.387 | 08.530 | 08.530 | 08.607 | 76 / 103 ms | 1 pend + 1 pagada → 2 pend | INHABILITADO | INHAB. / INHAB. / inhabilitado | **PASS** |

**Otros criterios:**
- **Cuota generada y no vencida:** el generador no emite meses abiertos, así que el caso "5c" no se ejecuta como generación. **Queda cubierto por el estado inicial de D**: tenía la cuota de octubre emitida y pendiente con $85.000 y figuraba AL DÍA. Es la regla de HU-083, no un bug.
- **5a/5b:** "5 creadas" porque el generador emite para todos los jugadores en el club en ese mes que no tienen la cuota. Incluye a los E de corridas anteriores (también con alta retroactiva). Es el comportamiento esperado.

**Primera corrida (descartada como evidencia):** fallaron 3, 3b y 5 por **errores del script**, no del sistema:
- un array de un solo elemento se mandó como número (400);
- el nombre del mes quedó vacío (400, que el script marcó PASS por error: ahora un caso exige también la respuesta esperada del Administrativo);
- el INSERT del arancel falló en silencio (la PK es identity; ahora el script corta si un fixture no queda cargado).

Se corrigió el script y se repitió **toda** la batería con fixtures nuevos (§6).

## 7. Bugs (creados en Jira y vinculados a HU-064 = SCRUM-340 con "bloquea")

### BUG-1 (SCRUM-804, bloquea SCRUM-340): El Administrativo no arranca: versión de migración duplicada `V20261031_01`

- **Severidad:** Bloqueante. **HU relacionadas:** HU-106, HU-107/HU-108. **Entorno:** HU-115; también afecta a cualquier entorno con la rama `FramD`.
- **Rama/commit:** `FramD` `f9d4514`. Las migraciones las agregaron `d0d3158` (HU-106, `V20261031_01__aranceles_impacto_auditado.sql`) y `13cac87` (HU-107/108, `V20261031_01__historial_exportaciones_reportes.sql`), y quedaron juntas con los merges `ed4e12a` y `4cc101f`.
- **Pasos:** armar el entorno y levantar `ApiGestion`.
- **Esperado:** la API arranca y aplica las migraciones.
- **Obtenido:** `InvalidOperationException: Hay versiones de migración duplicadas.` en `PlanMigraciones.Pendientes` (`Migracion.cs:37`) y el proceso termina.
- **Evidencia:** `trabajo/log_nuestro_err.txt` de la primera corrida.
- **Sugerencia:** renumerar una de las dos (las dos son independientes: una crea `EXPORTACIONES_REPORTES` y la otra altera `ARANCELES`) y revisar bases donde ya se aplicó con el nombre viejo. **No se corrigió en el repo** sin decisión del equipo.

### BUG-2 (SCRUM-805, bloquea SCRUM-340): El fork no envía la credencial de sistema (HU-055) y el Deportivo muestra "SIN VERIFICAR" a todos

- **Severidad:** Crítica (rompe HU-064, el QR y los listados). **HU relacionadas:** HU-055, HU-028, HU-115.
- **Rama/commit:** Deportivo `feat/base-unificada` `4421b1d`; Administrativo `FramD`; `cacc-unificacion` `61e1030`.
- **Pasos:** armar el entorno y consultar `GET /api/jugadores?categoriaId=3` como DT.
- **Esperado:** el estado real de cada jugador.
- **Obtenido:** "SIN VERIFICAR" para los 16 jugadores. En el log del Administrativo: `Error de integración UNAUTHORIZED (401) … Motivo: Credencial de sistema inválida`. El fork no tiene `X-Integration-Key` en ningún lado.
- **Causa:** el parche `ellos-credencial-integracion.patch` existe **solo** en una copia local de `cacc-unificacion` del 28/10; no está en ninguna rama remota ni en el fork, y `armar.ps1` no configura la credencial.
- **Complemento (tooling):** con `UserSecretsId` en `ApiGestion`, los user-secrets del desarrollador pisan la clave del `appsettings.Development.json` que escribe `armar.ps1`. La clave tiene que ir por variable de entorno.
- **Tiempo observado:** la consulta responde en ~60 ms, pero con el estado "SIN VERIFICAR".

## 8. Observaciones

| # | Observación |
|---|---|
| O-1 | Front Deportivo `admin-home.component.ts:140`: `j.estadoCuota \|\| 'AL DÍA'`. Con el Administrativo rechazando, el backend del Deportivo devolvió "SIN VERIFICAR" (visto en la línea base), así que este fallback no se disparó; queda como riesgo bajo |
| O-2 | `cacc-unificacion/probar.ps1` no está al día con HU-106: "arancel programado" falla con 400 "Revisá el impacto del cambio antes de confirmarlo" porque no simula antes ni manda la huella |
| O-3 | La base unificada se entrega sin aranceles y HU-106 no deja programar uno con fecha pasada: para probar cuotas de meses pasados hay que cargar un arancel histórico como dato de prueba |

## 9. Regresión

La segunda corrida completa de `hu064-e2e.ps1` (fixtures nuevos, después de corregir el script) repitió los 9 escenarios de la lista de regresión: inhabilitado paga una, paga todo, solo entrenamientos paga, beca, primera y segunda deuda relevante, pago fallido, consulta individual y consulta por categoría. **9/9 PASS.** Resultados crudos en `HU-064-entorno\hu064-resultados.json`.

## 10. Conclusión

- **La regla y la inmediatez funcionan de punta a punta.**
  - Un pago, una anulación, una beca preventiva o una cuota nueva hechos en el Administrativo se ven en la siguiente consulta del Deportivo, entre 58 y 76 ms después del commit.
  - No hace falta job, limpiar caché ni reiniciar.
  - Coinciden la vista individual, la de categoría y la fuente.
  - El pago fallido no deja rastros.
  - El Deportivo no recibe datos financieros.
- **No se puede cerrar HU-064** mientras los dos sistemas no funcionen juntos sin workarounds:
  1. Corregir BUG-1 (migración duplicada) en el Administrativo.
  2. Corregir BUG-2: integrar la credencial de sistema en el fork (o en `cacc-unificacion`) y que `armar.ps1` la configure por variable de entorno.
  3. Volver a correr `hu064-e2e.ps1` sin W-1 a W-3.

**Para repetir la prueba:**
```powershell
cd C:\Users\FramD\Documents\Tesis\HU-064-entorno
powershell -ExecutionPolicy Bypass -File hu064-e2e.ps1   # con las APIs levantadas y la clave en Integracion__ClienteDeportivo__Clave
cd cacc-unificacion; .\scripts\detener.ps1               # para frenar las APIs
```
