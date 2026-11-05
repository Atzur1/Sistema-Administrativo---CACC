# QA HU-064: el pago de un jugador con deuda actualiza su habilitación en tiempo real

**Ticket:** SCRUM-340 · **Fecha:** 04/11/2026 · **Entorno:** HU-115 (local combinado, LocalDB) · **Última corrida:** 23:35 UTC (20:35 ART)

> **Estado: APROBADA FUNCIONALMENTE (PASS 9/9). CIERRE PENDIENTE DE MERGE.**
> Los fixes de BUG-1 y BUG-2 ya existen. Falta subir BUG-2 a los repositorios remotos y repetir la corrida con un armado nuevo (§8).
> Cuando §8 quede completa, cambiar esta línea a **TERMINADA / APROBADA (PASS 9/9)** y completar las celdas marcadas con ⏳.

## Resultado

| Qué | Resultado |
|---|---|
| Comportamiento funcional (9 casos, en los dos sistemas) | **PASS 9/9** (corrida del 04/11, 23:35 UTC, fixtures nuevos con DNI `41236xxx`) |
| Inmediatez | Consulta individual: **18 a 22 ms**. Listado de la categoría: **53 a 72 ms**. Sin job, sin limpiar caché y sin reiniciar |
| Privacidad | **Cumple.** El DT no recibe montos, medios de pago, saldos ni cuotas (§3) |
| BUG-1 (SCRUM-804) | **Corregido** en `cca4abf`, ya en `main` |
| BUG-2 (SCRUM-805) | **Corregido en ramas locales** (`a9e690f5` en el fork y `523c3a6` en `cacc-unificacion`). **Falta push y PR** |
| Integración desde las ramas, sin intervención manual | ⏳ Pendiente de repetir con un armado nuevo (§8) |

---

## 1. Entorno HU-115

### 1.1 Repositorios

| Pieza | Repositorio y rama | Commit | Estado |
|---|---|---|---|
| Portal Administrativo | `Atzur1/Sistema-Administrativo---CACC`, `main` | `cca4abf` (fix de BUG-1) | En remoto |
| Portal Deportivo (fork) | `Atzur1/App-Club-Atletico-Camioneros`, `fix/SCRUM-805-credencial-integracion` (sobre `feat/base-unificada` `4421b1d8`) | `a9e690f5` | **Solo local** (push rechazado con 403: falta permiso de escritura) |
| Unificación | `Atzur1/cacc-unificacion`, `fix/SCRUM-805-credencial-integracion` (sobre `feat/HU-062-estado-deportivo` `61e1030`) | `523c3a6` | **Solo local** (push rechazado con 403) |

Clones en `C:\Users\FramD\Documents\Tesis\HU-064-entorno\`.

### 1.2 Configuración

- **Armado:** `cacc-unificacion\scripts\armar.ps1 -Servidor "(localdb)\MSSQLLocalDB" -Recrear -Iniciar`. Copia los backends a `trabajo/`, que es descartable; los repos no se modifican.
- **Base:** `ClubCamionerosUnificadaV2_Prueba` en **LocalDB**. En esta máquina no hay SQL Server como servicio y Docker está detenido: es un desvío del README, que pide SQL Server local.
- **APIs:** Administrativo en `http://localhost:5318` y Deportivo en `http://localhost:5319`, en `Development`. La clave JWT es compartida y aleatoria por armado. El Deportivo llama al Administrativo por `AdministrativoApi:BaseUrl`, con timeout de 3 s.
- **Credencial de sistema (HU-055):** con `523c3a6`, `armar.ps1` genera una credencial aleatoria en `trabajo\.credencial-integracion` (ignorada por git). `iniciar.ps1`, `manual.ps1` y `probar.ps1` la pasan por variable de entorno del proceso (`Integracion__ClienteDeportivo__Clave` y `AdministrativoApi__ClaveIntegracion`), que prevalece sobre los user-secrets del desarrollador. Nunca se escribe en un `appsettings`.
- **Cuentas:** SuperAdmin `20111222` (opera en el Administrativo) y Director Técnico `30000004`, de la categoría 3 (consulta en el Deportivo).

### 1.3 Corrección de los bugs (reemplaza los workarounds W-1, W-2 y W-3)

| Bug | Fix oficial | Qué reemplaza |
|---|---|---|
| **BUG-1 (SCRUM-804)** | Commit `cca4abf` en `Sistema-Administrativo---CACC` (`main`): renombra `V20261031_01__historial_exportaciones_reportes.sql` a **`V20261031_02__…`** | W-1 (renombrado a mano en `trabajo/`) |
| **BUG-2 (SCRUM-805)** | Commit `a9e690f5` del fork deportivo: `AddClienteAdministrativo` manda `X-Integration-Key` en cada pedido al Administrativo y valida la clave con el mismo contrato (32 bytes, ASCII visible, sin espacios en los extremos). Trae 147 líneas de pruebas (`ClienteAdministrativoTests.cs`) | W-2 (parche local del 28/10) |
| **BUG-2, parte de tooling** | Commit `523c3a6` en `cacc-unificacion`: `armar.ps1` aplica el parche de la credencial solo si falta (`aplicar-si-falta.ps1`, no hace nada si el fork ya trae `a9e690f5`), genera la credencial y los scripts de arranque la inyectan por variable de entorno | W-3 (clave cargada a mano en el entorno) |

Pruebas del tooling, corridas el 04/11: `pruebas\credencial-helper.ps1` y `pruebas\parche-credencial.ps1` pasan todas. Comprueban que el parche aplicado sobre `4421b1d8` queda idéntico, archivo por archivo, a `a9e690f5`; que es idempotente; y que un proceso hijo hereda la credencial.

> **Aclaración sobre la evidencia.** La corrida de 23:35 UTC se hizo sobre una carpeta `trabajo/` armada a las 17:34 (hora local) con W-1 a W-3, es decir, **antes** de los commits de los fixes. Por eso valida el comportamiento de HU-064, pero no la integración de los fixes oficiales. Esa validación es el paso 3 de §8.

## 2. Dependencias

| Dependencia | Estado |
|---|---|
| HU-115 entorno combinado | Operativo. Con los fixes oficiales: ⏳ (§8) |
| HU-028 servicio de habilitación | Funciona (§3) |
| HU-025 / HU-063 pagos | `POST api/pagos/cobro` y `POST api/pagos/registrar` funcionan y el estado se refleja enseguida |
| Anulación | `POST api/pagos/{id}/anular` funciona |
| HU-049 / HU-091 cuotas faltantes | Funciona. Necesita un arancel vigente en el mes, y la base unificada se entrega sin aranceles (O-3) |
| HU-083 vencimiento | Una cuota emitida y todavía no vencida no restringe (caso 4, estado inicial) |
| HU-011 beneficios | Funciona; homologación del caso 4 en §4 |
| Autenticación entre sistemas | JWT compartido: OK. Credencial de sistema HU-055: corregida (BUG-2) |

## 3. Regla efectiva, fuente única y privacidad

- **Fuente única:** `DaoLibrary/EstadoDeudaDao` → `EstadoDeudaJugador.Estado` → `GET/POST api/integracion/v1/estado-deuda`. El Deportivo (`EstadoDeudaClient`) solo traduce el texto: `habilitado` → "AL DÍA", `solo_entrenamientos` → "SOLO ENTRENAMIENTOS", `inhabilitado` → "INHABILITADO".
- **Regla:** se cuentan las cuotas pendientes (no la inscripción) que tienen saldo mayor a 0, ya descontados el beneficio y los abonos, y que **ya vencieron**. Una cuota vence el día 10 del mes siguiente (HU-083). Con 0 cuotas → habilitado; con 1 → solo entrenamientos; con 2 o más → inhabilitado.
- **Caché:** no hay en ningún backend ni en el front deportivo (verificado en el código). En ejecución, la consulta inmediata siempre devolvió el estado nuevo.
- **Consulta individual y por categoría:** `GET /api/jugadores/{id}` (→ `estado-deuda/dni`) y `GET /api/jugadores?categoriaId=3` (→ `estado-deuda/lote`). En los 9 casos coincidieron entre sí y con la fuente.
- **Política de privacidad, cumplida y verificada en ejecución:** la fuente devuelve solo `dni, idJugador, estado, accesoPermitido, fechaBaja`. La ficha que recibe el DT no trae montos, saldos, cuotas ni medios de pago (`FichaConDatosFinancieros = false` en `hu064-resultados.json`).

## 4. Homologación del caso 4 (beca)

El criterio original ("una beca deja al jugador restringido en Al día") **no se puede cumplir con el flujo real**. `BeneficiosService` no deja becar cuotas ya vencidas ("becarlas sería condonar deuda"), y son justamente las vencidas las que restringen.

**Criterio homologado (prevención a tiempo), según las reglas conjuntas de HU-011, HU-083 y HU-064:** la asignación real de una beca del 100% por la API (`POST /api/players/{id}/discount`, HTTP 201) deja en **$0** el saldo de la cuota emitida y todavía no vencida. El alumno sigue **"AL DÍA"**, y como la regla solo cuenta saldos mayores a 0, esa cuota **no lo va a inhabilitar** cuando llegue el vencimiento del día 10.

Límite conocido: el día del vencimiento (10/11/2026) no se puede observar en vivo, porque el reloj de negocio es real y no se puede configurar. Queda cubierto por la regla de §3, que exige saldo mayor a 0.

## 5. Fixtures (DNI con sufijo aleatorio `236`)

- Alta real por el Deportivo (`POST /api/jugadores`, que pasa por la API de integración, HU-056), en la categoría 3, masculino.
- Por SQL, solo como preparación: se borran los cargos del alta, se cargan las cuotas **ya vencidas** (jul, ago y sep 2026, de $85.000), se retrotrae la `fecha_alta` de E al 01/07 y se carga un arancel histórico masculino ($85.000 desde 01/01/2026) para que el generador pueda emitir (O-3).
- **Las operaciones bajo prueba van siempre por la API real.**

| Jugador | DNI | Estado inicial observado |
|---|---|---|
| A | 41236001 | INHABILITADO (ago y sep vencidas) |
| B | 41236002 | SOLO ENTRENAMIENTOS (sep) |
| C | 41236003 | INHABILITADO (jul, ago y sep) |
| D | 41236004 | AL DÍA (oct emitida, no vencida, $85.000 pendiente) |
| E | 41236005 | AL DÍA (alta 01/07, sin cuotas) |
| F | 41236006 | INHABILITADO (ago y sep) |
| P | 41236007 | SOLO ENTRENAMIENTOS (sep) |

## 6. Casos ejecutados (corrida del 04/11/2026, 23:35 UTC)

Tiempos en UTC:
- **T op:** inicio de la operación en el Administrativo.
- **T conf:** respuesta del Administrativo.
- **T resp:** estado recibido en la ficha del Deportivo, consultada enseguida, sin pausa.

**Reflejo** es lo que tardó esa consulta; el listado de la categoría se pidió justo después. En los casos con pago, `fecha_hora_registro` en la base cae siempre entre T op y T conf.

| ID | Jugador | Operación (API real) | Admin | T op | T conf | T resp | Reflejo ficha / listado | Persistido antes → después | Esperado | Ficha / listado / fuente | Resultado |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | A | `cobro` de 1 de 2 vencidas | 200 | 23:35:59.912 | 59.968 | 59.987 | **19** / 56 ms | 2 pend → 1 pend + 1 pagada | SOLO ENTRENAMIENTOS | SOLO ENTR. / SOLO ENTR. / solo_entrenamientos | **PASS** |
| 2 | C | `cobro` de las 3 | 200 | 23:36:00.382 | 00.433 | 00.455 | **22** / 56 ms | 3 pend → 3 pagadas | AL DÍA | AL DÍA / AL DÍA / habilitado | **PASS** |
| 3 | B | `cobro` de la única | 200 | 23:36:00.830 | 00.849 | 00.868 | **19** / 72 ms | 1 pend → 1 pagada | AL DÍA | AL DÍA / AL DÍA / habilitado | **PASS** |
| 3b | P | `registrar` parcial de $40.000 (sep) | 200 | 23:36:01.214 | 01.242 | 01.260 | **19** / 53 ms | 1 pend → 1 pend (con saldo) + 1 abono | SOLO ENTRENAMIENTOS (queda saldo) | SOLO ENTR. / SOLO ENTR. / solo_entrenamientos | **PASS** |
| 4 | D | Beca 100% desde oct por `POST api/players/{id}/discount` | 201 | 23:36:01.572 | 01.655 | 01.674 | **20** / 56 ms | Cuota oct con saldo a cobrar $0 | AL DÍA (§4) | AL DÍA / AL DÍA / habilitado | **PASS** |
| 5a | E | `cuotas-faltantes/2026/9/generar` | 200 (1 creada) | 23:36:02.972 | 03.019 | 03.037 | **18** / 53 ms | 0 → 1 pend | SOLO ENTRENAMIENTOS | SOLO ENTR. / SOLO ENTR. / solo_entrenamientos | **PASS** |
| 5b | E | `cuotas-faltantes/2026/8/generar` | 200 (1 creada) | 23:36:03.331 | 03.348 | 03.366 | **18** / 56 ms | 1 → 2 pend | INHABILITADO | INHAB. / INHAB. / inhabilitado | **PASS** |
| 6 | F | `cobro` con una cuota válida y una inexistente | **400** (esperado) | 23:36:02.086 | 02.153 | 02.173 | **19** / 55 ms | 2 pend → 2 pend, 0 pagadas (rollback: no cobró ninguna) | INHABILITADO (sin cambio) | INHAB. / INHAB. / inhabilitado | **PASS** |
| 7 | A | `anular` el pago #85 del caso 1 | 200 | 23:36:02.520 | 02.558 | 02.576 | **18** / 57 ms | 1 pend + 1 pagada → 2 pend | INHABILITADO | INHAB. / INHAB. / inhabilitado | **PASS** |

**Resumen de tiempos:** la consulta individual reflejó el cambio entre **18 y 22 ms** después de la confirmación del Administrativo, y el listado de la categoría entre **53 y 72 ms**.

**Otros criterios:**
- **Cuota generada y no vencida:** el generador no emite meses abiertos, así que ese caso no se ejecuta como generación. Lo cubre el estado inicial de D: tenía la cuota de octubre emitida y pendiente, y figuraba AL DÍA. Es la regla de HU-083.
- Resultados crudos: `HU-064-entorno\hu064-resultados.json`. Corridas anteriores (`41897xxx`, reflejo de 58 a 76 ms en otro armado) en `qa\HU-064\revalidacion-20261104\QA-HU-064-historico.md`.

## 7. Bugs vinculados (bloqueaban SCRUM-340)

| Bug | Resumen | Fix | Estado en el código | Estado en Jira |
|---|---|---|---|---|
| **SCRUM-804** (BUG-1) | La API del Administrativo no arrancaba: versión de migración `V20261031_01` duplicada (HU-106 y HU-107/108) | `cca4abf`: renombrada a `V20261031_02__historial_exportaciones_reportes.sql` | En `main` | ⏳ Pasar a **Resolved** |
| **SCRUM-805** (BUG-2) | El fork no enviaba `X-Integration-Key` y el Deportivo mostraba "SIN VERIFICAR" a todos | `a9e690f5` (fork) + `523c3a6` (`cacc-unificacion`, `armar.ps1` con inyección de la credencial) | ⏳ Push y PR pendientes | ⏳ Pasar a **Resolved** después del merge |

## 8. Pendiente para el cierre formal

1. ⏳ Subir `fix/SCRUM-805-credencial-integracion` al fork (`a9e690f5`) y abrir el PR a `feat/base-unificada`.
2. ⏳ Subir `fix/SCRUM-805-credencial-integracion` a `cacc-unificacion` (`523c3a6`) y abrir el PR a `feat/HU-062-estado-deportivo`.
3. ⏳ Con las ramas mergeadas, hacer un armado nuevo (`armar.ps1 -Recrear -Iniciar`, sin pasos manuales) y repetir `hu064-e2e.ps1`. La credencial se lee de `trabajo\.credencial-integracion`. Resultado: ______ (esperado: 9/9 PASS).
4. ⏳ Pasar SCRUM-804 y SCRUM-805 a Resolved, y SCRUM-340 a Done.

## 9. Observaciones

| # | Observación |
|---|---|
| O-1 | Front Deportivo `admin-home.component.ts:140`: `j.estadoCuota \|\| 'AL DÍA'`. El backend devuelve "SIN VERIFICAR" cuando el Administrativo rechaza la consulta, así que este fallback no se disparó; queda como riesgo bajo |
| O-2 | `cacc-unificacion/probar.ps1` no está al día con HU-106: "arancel programado" falla con 400 porque no simula antes ni manda la huella |
| O-3 | La base unificada se entrega sin aranceles y HU-106 no deja programar uno con fecha pasada: para probar cuotas de meses pasados hay que cargar un arancel histórico como dato de prueba |
| O-4 | La rama `FramD` del Administrativo todavía no trae `cca4abf` (sigue en `f9d4514`). Hay que traer `main` antes de armar desde `FramD` |

## 10. Definición de Listo (DoD)

| Criterio | Estado |
|---|---|
| Los 9 escenarios de regresión pasan (inhabilitado paga una, paga todo, solo entrenamientos paga, pago parcial, beca, primera y segunda deuda relevante, pago fallido, anulación) | ✅ 9/9 PASS |
| El cambio se refleja en tiempo real, sin job, caché ni reinicio | ✅ 18 a 22 ms (individual) y 53 a 72 ms (listado) |
| Coinciden la consulta individual, el listado y la fuente única | ✅ |
| El pago fallido no deja rastros | ✅ Rollback verificado (caso 6) |
| Privacidad: el DT no recibe datos financieros | ✅ |
| Bugs bloqueantes corregidos | ✅ en el código (`cca4abf`, `a9e690f5`, `523c3a6`) · ⏳ merge de BUG-2 |
| El Administrativo y el fork funcionan juntos desde las ramas, sin workarounds | ⏳ §8, paso 3 |

**La DoD queda cumplida cuando se completen los pasos de §8.**

**Para repetir la prueba:**
```powershell
cd C:\Users\FramD\Documents\Tesis\HU-064-entorno\cacc-unificacion
.\scripts\armar.ps1 -Servidor "(localdb)\MSSQLLocalDB" -Recrear -Iniciar
. .\scripts\credencial-integracion.ps1; UsarCredencialIntegracion (Resolve-Path .\trabajo)   # para que hu064-e2e lea la credencial
cd ..; powershell -ExecutionPolicy Bypass -File hu064-e2e.ps1
cd cacc-unificacion; .\scripts\detener.ps1
```
