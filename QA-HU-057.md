# QA HU-057: baja informada por el sistema deportivo y fin de la generación de cuotas

## Revisión de code review: 29/10/2026, noche

No hubo hallazgos propios de esta HU. Regresión tras las correcciones: **E2E 41/41** (`qa/HU-057/salida_e2e_revision.txt`), backend 742/742. La API de baja también queda protegida por HTTPS obligatorio y proxies confiables (QA-HU-055).

## Actualización 29/10/2026, tarde: cierre técnico

**Estado: TÉCNICAMENTE TERMINADA, LISTA PARA REVISIÓN** en el alcance del Administrativo. La baja **iniciada desde el Deportivo** sigue sin usar el contrato (BUG-059-1, ver QA-HU-059): es un cambio del equipo Deportivo.

| Prueba | Resultado |
|---|---|
| Regresión backend completa (`CACC_TEST_DB=<CACC_HU056_IT_20261029_1340_Prueba> dotnet test ApiGestion.Tests`) | **720 / 720 PASS**, 0 omitidas. Los 37 de HU-056 que estaban en rojo ahora pasan. |
| E2E del contrato (`pwsh -File qa/HU-057/e2e_hu057.ps1`) | **41 / 41 PASS en dos corridas seguidas.** Una tercera corrida, lanzada **en paralelo** con la suite de HU-061, se cortó con "You cannot call a method on a null-valued expression" y no se pudo reproducir. No cuenta ni como PASS ni como FAIL. |
| Regresión de "alta integrada (HU-056)" | Antes BLOCKED; ahora **PASS** (QA-HU-056 y QA-HU-060). |

**Pendiente de negocio (sin cambios):** la política de bajas retroactivas, y RIESGO-1 (llamar a `cargos-alta` después de una reincorporación).

---

**Fecha:** 29/10/2026 · **Rama:** `FramD` (cambios sin commitear) · **Base de prueba:** `ClubCamionerosUnificadaV2_HU057_Prueba` (LocalDB),
copia por backup/restore de `ClubCamionerosUnificadaV2_HU056_Prueba`, que tiene el esquema unificado de HU-053 y las migraciones hasta
`V20261028_02`. No se tocó producción, `CCACUnificada` ni las bases de origen.

**Resultado:** **PASS** para el alcance de HU-057. La regresión de "alta integrada (HU-056)" queda **BLOCKED** porque HU-056 no está
implementada. Detalle en [Dependencias](#1-estado-inicial-y-dependencias) y en [Pendientes](#12-pendientes-reales).

---

## 1. Estado inicial y dependencias

Revisé el código y la base de cada dependencia. No di ninguna por terminada solo porque hubiera código relacionado.

| HU | Estado real encontrado | Impacto en HU-057 |
|---|---|---|
| HU-053 base unificada | Hecha. El esquema `ClubCamioneros_unificada_crear_base.sql` ya modela la baja (ver §2). Las bases `_HU05x_Prueba` existen. | Se usa como base de prueba. |
| HU-054 contrato v1 | `estado-deuda` (dni/lote) y `cargos-alta`. **No había ninguna operación de baja ni de reincorporación.** | Se agregan dos operaciones al mismo controller y a la misma versión (es un cambio compatible). |
| HU-055 autenticación entre sistemas | Implementada, sin commitear: `IntegrationClientAuthenticationMiddleware` (`X-Integration-Key`), más el JWT del usuario y `origen_sistema` en auditoría. | Se reutiliza tal cual. Las rutas nuevas quedan protegidas sin ningún cambio. |
| HU-056 alta integrada | **NO implementada.** `JugadoresAltaService.AltaDesdeIntegracion` / `CambiarCategoria` y `RegistroRechazosIntegracion` lanzan `NotImplementedException`. El DAO (`PlayerDAO.AltaDesdeIntegracion`) existe, pero ningún endpoint lo usa. 37 pruebas en rojo (TDD en curso). | No bloquea la baja. **BLOCKED** como regresión. Por eso los rechazos de baja no se registran en `INTEGRACION_RECHAZOS` (ver §12). |
| HU-049 generación automática | `GeneradorCuotasMensuales` llama a `PagosDao.GenerarCuotasPendientesDelMes`, que llama a `SqlCuotaMensual.Generar`. | Ya excluía a los dados de baja (ver §5). |
| HU-009 generación manual | Hoy hay tres caminos manuales: "generar faltantes" del Resumen (`CuotasFaltantesDao.GenerarFaltantesDelMes`), la emisión al cobrar un mes por adelantado (`PagosDao.EmitirCuotaDeJugador`) y la emisión al programar un arancel (`ArancelesService`). | Los tres usan `SqlCuotaMensual.Generar`. |
| HU-010 conservación de importes | `PAGOS.monto_base` / `monto_final` se graban al emitir y no se recalculan. | Verificado: la baja y el reingreso no tocan importes ya emitidos. |
| HU-083 calendario | `CalendarioCuotas` (C#) y `SqlCalendarioCuotas` (SQL): corte el día 10. | El reingreso usa `CalendarioCuotas.YaSeGeneraElMesEnCurso`. |
| HU-040/041 auditoría | `TR_AUDIT_JUGADORES` (incluye `fecha_baja` desde `V20261011_02`) y `origen_sistema` (`V20261028_01`). | La baja queda auditada sin código nuevo. |
| HU-UNI-07 responsabilidades | No existe en el repo. QA-HU-061 ya registró que las HU-UNI-0x no están en Jira. Rige el encabezado del esquema unificado: *"Baja de jugadores: JAMAS se borra…"*. | Se respeta ese encabezado. |

**Ya existía antes de esta HU:** la baja y la reactivación desde el Portal Administrativo (`POST api/jugadores/{id}/baja` y `/reactivar`,
solo SuperAdmin), sobre `JugadoresDao.DarDeBaja` y `JugadoresDao.Reactivar`.

## 2. Esquema y modelo de baja

**La HU original dice que JUGADORES no tiene marca de baja. Eso ya no es cierto**, y por eso no se creó ninguna columna.

| Concepto | Columna real | Origen |
|---|---|---|
| fecha de baja | `JUGADORES.fecha_baja DATE NULL` (NULL = activo) | `V20261011_01` y el esquema unificado |
| activo / dado de baja | `JUGADORES.activo AS (CASE WHEN fecha_baja IS NULL THEN 1 ELSE 0 END)`. Es **calculada**: nadie la escribe y nunca puede contradecir a `fecha_baja`. | esquema unificado (HU-053) |
| fecha de alta | `JUGADORES.fecha_alta DATE NULL` | existente |
| historial de altas y bajas | `JUGADORES_PERIODOS (fecha_alta, fecha_baja)`. La mantiene `TR_JUGADORES_PERIODOS`, escriba quien escriba. | esquema unificado (HU-053) |

- Ninguna FK hacia `JUGADORES` tiene cascada: las 9 son `NO_ACTION` (PAGOS, PAGOS_ANULADOS, JUGADORES_DESCUENTOS, JUGADORES_CATEGORIAS,
  JUGADORES_PERIODOS y las tablas del Deportivo). Lo consulté en `sys.foreign_keys`.
- **Migración:** no hizo falta ninguna. `V20261011_01` agregó `fecha_baja` como columna NULL, sin default. Así ningún jugador existente
  quedó dado de baja, y nada que deshacer en el rollback: es una columna opcional. Comprobado en la base de prueba: **580/580 jugadores
  importados siguen activos** antes y después de todas las pruebas.

## 3. Contrato

Flujo: Sistema Deportivo → backend del Deportivo → `X-Integration-Key` + JWT del usuario → `IntegracionJugadoresController` →
`JugadoresBajaService` → `JugadoresDao` → base unificada. El frontend deportivo no llama a estas rutas ni escribe tablas financieras.

| Operación | Cuerpo | Respuestas |
|---|---|---|
| `POST /api/integracion/v1/jugadores/{idJugador}/baja` | `{ "fechaBaja": "yyyy-MM-dd" }` (obligatoria, fecha **efectiva**) | 200, 400 (`idJugador`/`fechaBaja`, formato o fecha futura), 404, 409 (ya de baja con otra fecha), 422 (anterior a su ingreso), 401, 403, 429, 500, 503 |
| `POST /api/integracion/v1/jugadores/{idJugador}/reincorporacion` | `{ "idCategoria": n }` (opcional) | 200, 400 (`idJugador`/`idCategoria`), 404, 409 (ya activo), 422 (categoría inexistente), 401, 403, 429, 500, 503 |

- Roles: los mismos que `cargos-alta` (SuperAdmin del Administrativo y `Administrador (Tesorero)`). La credencial técnica sola no alcanza:
  sin JWT responde 401 (caso S3).
- Las respuestas nunca llevan montos ni cantidades de cuotas: `{ idJugador, estado: "baja", fechaBaja, yaEstabaDadoDeBaja }` y
  `{ idJugador, fechaAlta, categoriaCambiada }`.
- Errores con el formato común `{ errorCode, message, field?, exito, mensaje }`, sin stack traces (casos V1–V7, R1–R5 y la prueba
  unitaria del 500).
- Documentación: `docs/integracion/README.md` (secciones nuevas) y `docs/integracion/openapi-v1.json`, **regenerado desde Swagger**:
  el diff solo agrega las dos rutas.

## 4. Baja: reglas implementadas

| Regla | Implementación |
|---|---|
| Baja lógica, sin `DELETE FROM JUGADORES` | `UPDATE JUGADORES SET fecha_baja`. Persona, pagos, deuda y auditoría quedan. |
| Fecha efectiva | La informa el Deportivo. No se usa la hora del pedido. |
| Fecha futura | 400 `fechaBaja` (misma regla que la baja del portal). |
| Fecha anterior al ingreso | 422. El ingreso es `fecha_alta` o, si es posterior, el inicio del período abierto en `JUGADORES_PERIODOS` (ver BUG-1). |
| Reintento con la misma fecha | 200 `yaEstabaDadoDeBaja: true`. No hay segundo UPDATE ni segundo evento. |
| Otra fecha sobre una baja existente | 409. Se conserva la primera. Corregir una fecha de baja no está definido (ver §12). |
| Atomicidad | Una transacción `Serializable` + `BloqueoEscrituraPagos` (el mismo bloqueo que la generación y los cobros) + `UPDLOCK, HOLDLOCK` sobre el jugador. La baja, la limpieza y la cancelación de beneficios se aplican todas o ninguna. |
| Beneficios | **Regla explícita ya existente** (`V20261012_01`, `DarDeBajaCore`): las becas y descuentos vigentes o programados se cancelan con la fecha de baja (`cancelada_por_baja = 1`). La fila no se borra y sigue aplicando a los meses anteriores. No se inventó una regla nueva. |
| Cuotas posteriores a la baja | **Regla ya existente**: las cuotas pendientes **sin ningún pago** de meses posteriores al de la baja se quitan (por ejemplo, un mes emitido por adelantado). Las que tienen algún pago, y todas las de meses anteriores, quedan como deuda. Ver la decisión pendiente en §12. |

## 5. Regla única de exclusión de cuotas

`SqlCuotaMensual.JugadorEnElClub`: `(fecha_alta IS NULL OR fecha_alta <= EOMONTH(mes)) AND (fecha_baja IS NULL OR fecha_baja >= mes)`.
Es la **única** regla y la comparten todos los caminos de emisión:

| Camino | Usa |
|---|---|
| Generador mensual automático (HU-049) | `PagosDao.GenerarCuotasPendientesDelMes` → `SqlCuotaMensual.Generar` |
| Generar faltantes (manual, Resumen) | `CuotasFaltantesDao.GenerarFaltantesDelMes` → `SqlCuotaMensual.Generar` |
| Cobro de un mes por adelantado | `PagosDao.EmitirCuotaDeJugador` → `SqlCuotaMensual.Generar` (si no corresponde, el cobro se rechaza) |
| Programar un arancel | `ArancelesService` → `GenerarCuotasPendientesDelMes` |
| Reingreso | `JugadoresDao.ReincorporarCore` → `SqlCuotaMensual.Generar` |
| Detección de faltantes | `CuotasFaltantesDao` usa `JugadorEnElClub` |

El dado de baja no entra en el conjunto elegible: no se genera nada para esconderlo o borrarlo después. **La cuota del mes de la baja sí
le corresponde**, porque estuvo en el club parte del mes (mismo criterio que el alta, documentado en `V20261011_01`). No se agregó ninguna
regla duplicada.

## 6. Deuda histórica, pagos y Deudas y Morosidad

Caso E2E: alta 01/08. Agosto vencida, septiembre parcial (600 pendientes + abono de 400 en efectivo), octubre pendiente y noviembre
emitida por adelantado. Baja con fecha 15/10:
- Agosto, septiembre (pendiente y abono, con su medio de pago) y octubre: **sin cambios** (B6, comparación fila por fila).
- Noviembre, sin pagos y posterior a la baja: quitada (B7, regla existente).
- `estado-deuda` → `baja` y `accesoPermitido: false` (B4).
- Deudas y Morosidad (`ObtenerPendientesAgrupados`) sigue listándolo con su monto, su cantidad de cuotas y su `fechaBaja` (prueba de
  integración y B8).
- Después del reingreso, `estado-deuda` vuelve a `inhabilitado` (R11), porque agosto y septiembre siguen vencidas. **Inactivo no es lo
  mismo que sin deuda.**

## 7. Reincorporación y HU-083

| Regla | Resultado |
|---|---|
| Misma PERSONA y mismo JUGADOR | Mismo `PK_id_jugador` y mismo `FK_id_persona` (R6). Un solo jugador por DNI. |
| Fecha de alta = fecha del reingreso | `fecha_alta = hoy` (R6). La baja queda en `JUGADORES_PERIODOS` (R9) y en la auditoría (R10). |
| Reingreso **antes del día 10** | No se emite la cuota del mes. El generador la emite desde el 10, **una sola vez** (prueba de integración, reingreso el 05/05). |
| Reingreso **desde el día 10** | La cuota del mes se emite en el momento, una sola vez. Si ya existía, no se duplica (R7, reingreso el 29/10). |
| Meses de baja | No se generan, ni al reingresar ni con "generar faltantes" de esos meses (prueba de integración: marzo y abril quedan en 0). |
| Categoría nueva | Opcional. Se actualiza la categoría de cobro y se asegura la pertenencia en `JUGADORES_CATEGORIAS`. Las cuotas anteriores conservan su monto y la nueva usa el arancel de la nueva categoría. |
| Atomicidad | Una transacción con `BloqueoEscrituraPagos` y `UPDLOCK, HOLDLOCK`: reingreso, categoría, cuota del calendario e inscripción anual van todos o ninguno. Se reutiliza `JugadoresDao.ReactivarCore`, que ahora es `ReincorporarCore`. La reactivación del portal pasa por el mismo código. |
| Reintento | 409 "ya está activo". El estado final es el pedido (activo). No se crea otro período ni otra cuota. |

## 8. Idempotencia y concurrencia

| Escenario | Evidencia |
|---|---|
| Baja, se pierde la respuesta, reintento | 200 / 200 `yaEstabaDadoDeBaja: true`. Un evento, un período cerrado, pagos sin cambios (integración y B1–B2). |
| 8 bajas simultáneas (servicio) y 6 simultáneas (HTTP) | Todas OK, **una sola efectiva**, un solo evento de auditoría (integración y C1). |
| Baja mientras corre la generación mensual (marzo y abril en paralelo) | Final consistente en cualquier orden: marzo = 1 y abril = 0. Las operaciones se serializan con `BloqueoEscrituraPagos`. |
| Baja contra emisión manual | `EmitirCuotaDeJugador` y "generar faltantes" para un mes posterior a la baja → no emiten. |
| 4 reincorporaciones simultáneas | Una OK y tres 409. Un solo período abierto y una sola cuota del mes. |

## 9. Auditoría

- Baja: `AUDITORIA_CAMBIOS` registra `entidad = JUGADORES`, `accion = UPDATE`, `datos_antes.fecha_baja = null`,
  `datos_despues.fecha_baja = 2026-10-15` (fecha efectiva), `fecha_utc` (fecha y hora de la operación), `origen_sistema = PortalDeportivo`
  e `id_usuario` = el operador del token (B10 y prueba de integración).
- La operación rechazada o revertida no deja evento (B11 y prueba de integración): el evento se escribe en la misma transacción.
- La reincorporación queda auditada con origen `PortalDeportivo` (R10).
- La credencial y los tokens no aparecen en el log (L1). Los rechazos se registran con la plantilla del endpoint, no con sus valores (L2).

## 10. Pruebas ejecutadas

TDD: las pruebas se escribieron antes que el código. Primero se vieron en rojo (29/29 de integración contra el servicio stub; 1 de
contrato por el `field` que faltaba) y después en verde.

| Suite | Comando | Resultado |
|---|---|---|
| Integración HU-057 (base real, servicio + DAO + triggers) | `CACC_TEST_DB=<HU057_Prueba> dotnet test ApiGestion.Tests --filter BajaJugadoresIntegracion` | **29/29 PASS** |
| Contrato HTTP HU-057 (controller) | `--filter BajaJugadoresIntegracionControllerTests` | **11/11 PASS** |
| E2E contra la API real | `pwsh qa/HU-057/e2e_hu057.ps1` | **41/41 PASS**: `qa/HU-057/salida_e2e.txt`, `evidencia_e2e.json` |
| Regresión completa | `CACC_TEST_DB=<HU057_Prueba> dotnet test ApiGestion.Tests` | **716 total: 679 PASS, 37 FAIL.** Los 37 son todos `AltaJugadoresIntegracionIntegrationTests` (HU-056 en rojo). **Antes del cambio:** 676 total, 639 PASS y los mismos 37 FAIL. **Cero regresiones.** |

La regresión cubre: alta del portal, cargos-alta, generación automática, generación manual y faltantes, cobros y anulaciones, Deudas y
Morosidad, padrón, categorías, inscripción anual con reingreso (`InscripcionAnual`), auditoría y origen, autenticación y errores de
integración.

Archivos de prueba: `ApiGestion.Tests/BajaJugadoresIntegracion.Integration.Tests.cs`,
`ApiGestion.Tests/BajaJugadoresIntegracionController.Tests.cs` y `qa/HU-057/e2e_hu057.ps1`. Usan DNI `9857xxxx`, una categoría propia y
el reloj en 2030. Limpian todo al empezar y al terminar. Comprobado después de la corrida: 0 restos y 580/580 jugadores importados
activos.

## 11. Bugs encontrados

| Id | Severidad | Descripción | Estado |
|---|---|---|---|
| **BUG-1** | Alta | Los jugadores importados por HU-053 tienen `fecha_alta = NULL` y su período en `JUGADORES_PERIODOS` empieza el día de la importación (2026-10-28). `DarDeBajaCore` solo validaba contra `fecha_alta`, así que una baja con fecha anterior violaba `CK_JUGADORES_PERIODOS_fechas` y terminaba en **500**. Reproducido sobre el jugador importado #1: `ERROR 547 ... CK_JUGADORES_PERIODOS_fechas`. Afectaba **también a la baja del Portal Administrativo**, que usa el mismo DAO. | **Corregido:** la validación usa el inicio del período abierto y responde 422 con la fecha de ingreso. Tiene prueba. |
| **BUG-2** | Media | `IntegrationErrors.FromResult` no pasaba `ResultadoOperacion.Campo` al cuerpo: los 400 de negocio salían sin `field`, en contra del contrato. | **Corregido:** `field` solo en los 400, como dice el README. Tiene prueba. |
| RIESGO-1 | Media | Si el Deportivo llama a `cargos-alta` **después** de una reincorporación, ve un alta de hoy y le genera la inscripción a quien nunca tuvo una, aunque el reingreso haya decidido no cobrarla (seguía en su año). La cuota sí es idempotente. Es código de HU-056, que está en curso. | **Abierto.** Documentado en el contrato ("no llamar a cargos-alta después"). Recomendación: que `GenerarCargosDeAlta` rechace a quien tenga un período cerrado en `JUGADORES_PERIODOS`. |
| REV-1 | Baja | `Reincorporar` hacía `Rollback()` sin chequear si la base ya había revertido la transacción, y se perdía la excepción original (salía 500 en lugar de 503). | **Corregido** (encontrado en code review). |

## 12. Pendientes reales

1. **HU-056 sin implementar (BLOCKED):** no se pudo hacer la regresión del alta integrada, y los rechazos de baja y reincorporación no
   quedan en `INTEGRACION_RECHAZOS`, porque `RegistroRechazosIntegracion` lanza `NotImplementedException`. Cuando HU-056 esté lista, hay
   que agregar `OperacionesDeIntegracion.BajaJugador/Reincorporacion` y registrar los rechazos.
2. **Decisión del club (ambigua, no se inventó una regla):** una baja **retroactiva** (por ejemplo, informada el 29/10 con fecha 15/08)
   quita las cuotas *sin pagos* de septiembre y octubre, aunque ya estén vencidas. Es la regla existente: el jugador no estaba en el
   club esos meses. Pero choca con el espíritu de "la baja no perdona deuda" si el Deportivo informa tarde. Opciones: (a) mantenerla;
   (b) limitar la retroactividad (por ejemplo, no antes del último período ya emitido); (c) no quitar nunca las cuotas ya vencidas.
   Lo tiene que decidir el club.
3. **Corrección de una fecha de baja ya registrada:** no está definida. Hoy responde 409. Si el Deportivo se equivoca de fecha, la
   corrige un SuperAdmin (reactivar y volver a dar de baja).
4. **Jugadores importados sin `fecha_alta`:** no pueden recibir una baja anterior al 28/10/2026, el día de la importación. Si el club
   tiene fechas reales, conviene cargarlas en `JUGADORES.fecha_alta` (el trigger corrige el período abierto).
5. Hallazgos del code review **de otras HU**, que no se tocaron: la política de rate limit no cubre los 401 de la credencial (HU-055),
   y el log con correlación está duplicado (HU-055).
6. Tradeoff consciente: `JugadoresBajaService` depende de `JugadoresDao` concreto, porque agregar `Reincorporar` a `IJugadoresDao`
   rompía cinco fakes de pruebas existentes. La categoría se valida antes de la transacción; si alguien la borra justo en el medio, el
   error de FK se responde 503 (caso improbable).
7. Avisar al equipo del Deportivo de las dos rutas nuevas (README, "Para quien cambie la API", punto 3).

## 13. Criterios de la Definición de Listo

| Criterio | Resultado |
|---|---|
| Marca inequívoca de baja | PASS: `fecha_baja` + `activo` calculada |
| Fecha efectiva de baja | PASS |
| Migración versionada si fue necesaria | N/A: ya existía (`V20261011_01` + esquema unificado) |
| Jugadores existentes no alterados | PASS: 580/580 activos |
| Deportes informa la baja por contrato | PASS: `POST …/baja` |
| Operación autenticada | PASS: S1–S5 |
| Generador automático omite | PASS |
| Generador manual omite | PASS |
| Sin cuotas futuras durante la baja | PASS |
| Deuda anterior visible | PASS |
| Pagos anteriores permanecen | PASS |
| Cuotas históricas permanecen | PASS: hasta el mes de la baja. Las posteriores sin pagos se quitan (regla existente, §12.2) |
| Beneficios históricos permanecen | PASS: filas conservadas; los vigentes se cancelan por la regla existente |
| Sin hard-delete del jugador | PASS |
| Baja auditada con origen Deportivo | PASS |
| Reintentos sin inconsistencias | PASS |
| Concurrencia probada | PASS |
| Reincorporación reutiliza PERSONA y JUGADOR | PASS |
| Conserva historial | PASS |
| Vuelve a generar según el calendario | PASS |
| No genera cuotas del período inactivo | PASS |
| HU-083 respetada | PASS |
| Pruebas de integración ejecutadas | PASS |
| Code review realizado | PASS: 9 hallazgos triados, 1 corregido, 2 de documentación, el resto de otras HU o tradeoffs |

## Archivos

- `DaoLibrary/JugadoresDao.cs`: validación contra la permanencia (BUG-1), `FechaBajaVigente` para la idempotencia, `Reincorporar` y
  `ReincorporarCore` con categoría opcional y el rollback seguro (REV-1). `Reactivar` y `ReactivarCore` delegan en ellos.
- `DaoLibrary/IJugadoresDao.cs`: solo el comentario.
- `DaoLibrary/PlayerDAO.cs`: `AsegurarCategoriaDelJugador` pasa de `private` a `internal`.
- `ServiceLibrary/JugadoresBajaService.cs` (nuevo): validación y respuesta de cada caso.
- `ApiGestion/Controllers/IntegracionJugadoresController.cs`: dos acciones nuevas. El servicio se recibe por `[FromServices]` para no
  cambiar el constructor que usan las pruebas existentes.
- `ApiGestion/Models/BajaIntegracionDTO.cs` (nuevo).
- `ApiGestion/Services/IntegrationErrors.cs`: `field` en los 400 de `FromResult` (BUG-2).
- `ApiGestion/Program.cs`: registro de `IJugadoresBajaService`.
- `docs/integracion/README.md` y `docs/integracion/openapi-v1.json`.
- Pruebas: `ApiGestion.Tests/BajaJugadoresIntegracion.Integration.Tests.cs`, `ApiGestion.Tests/BajaJugadoresIntegracionController.Tests.cs`
  y `qa/HU-057/`.
