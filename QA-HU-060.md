# QA HU-060: las altas del sistema deportivo impactan correctamente en el Sistema Administrativo

## Revisión de code review: 29/10/2026, noche

- **Regresión tras las correcciones de HU-055, HU-056 y HU-059:** **43 PASS · 0 FAIL · 0 BLOCKED** (`salida_revision.txt`, `evidencia_revision.json`).
- **LOW: reloj de negocio.** La fecha de nacimiento se valida contra `RelojNegocio.Hoy` y la de alta contra `Pruebas:FechaNegocio`. En Producción son la misma fecha; solo difieren con la fecha fija de pruebas. Decisión del usuario (opción A): el nacimiento se sigue validando contra la fecha real. La regla quedó fijada con 3 pruebas (QA-HU-056).

## Actualización 29/10/2026, tarde: cierre técnico

**Estado: TÉCNICAMENTE TERMINADA, LISTA PARA REVISIÓN.** **43 PASS · 0 FAIL · 0 BLOCKED**, igual en dos corridas.

| Corrida | Comando | Resultado | Evidencia |
|---|---|---|---|
| 3 | `pwsh -File qa\HU-060\e2e_hu060.ps1 -Evidencia qa\HU-060\evidencia_ejecucion3.json` | 43 / 0 / 0, salida 0 | `qa/HU-060/salida_ejecucion3.txt`, `evidencia_ejecucion3.json` |
| 4 | Ídem, `evidencia_ejecucion4.json` | 43 / 0 / 0, salida 0 | `qa/HU-060/salida_ejecucion4.txt`, `evidencia_ejecucion4.json` |

**Qué cambió desde la corrida de la mañana (14/0/29):**

- **HU-056 ya está implementada en el Administrativo** (alta y cambio de categoría). Los 27 casos que bloqueaba BUG-060-1 se ejecutaron y pasaron. ALT-0 ya no corre: solo aplica sin HU-056.
- **DEP-2 pasa:** `docs/integracion/README.md` documenta el alta y el cambio de categoría (BUG-060-2 resuelto).
- **CUO-2 dejó de estar BLOCKED (MEJ-060-1 resuelta).**
  - La API acepta `Pruebas:FechaNegocio` (`yyyy-MM-dd`), que fija la fecha de negocio del alta de jugadores. Solo funciona en **Development o Testing**; con cualquier otro entorno la API no arranca.
  - La suite reinicia el Administrativo con una fecha del otro lado del día 10 (hoy, 29/10, la otra rama es el 05/10) y hace un alta por el Deportivo (DNI 98600009).
  - Resultado: `201`, `fecha_alta` 2026-10-05, **sin cuota del mes** e inscripción 500 con esa fecha. Después vuelve al reloj real.
  - No se cambia la hora de la máquina.
- **REL-1 (nuevo):** con `Pruebas:FechaNegocio` y `ASPNETCORE_ENVIRONMENT=Production`, la API termina con error y muestra el mensaje de la guarda. La fecha fija no puede llegar a Producción (`docker-compose.yml` usa `Production`).
- **Las suites exigen PowerShell 7 (`#Requires -Version 7.0`).** Una corrida con Windows PowerShell 5.1 dio NOJ-1 y NOJ-4 en FAIL **falsos**:
  - 5.1 lee el `.ps1` UTF-8 sin BOM como ANSI, así que "Médico" llega roto al Deportivo;
  - además, `ConvertFrom-Json "[]"` no se enumera y `@(...).Count` da 1.

  Con `pwsh` 7.6 los dos pasan. Esa corrida no cuenta.

**Pendiente, sin bloquear:** OBS-060-1 (los rechazos que el propio Deportivo valida no llegan a `INTEGRACION_RECHAZOS`) y OBS-060-2 (el staff que crea el Deportivo queda auditado con origen NULL). Las dos son del Deportivo, ver el informe de cierre.

Lo que sigue abajo es la corrida de la mañana, que se conserva como historial.

---

**Estado: NO TERMINADA.** 14 PASS · 0 FAIL · 29 BLOCKED, igual en las dos corridas: los mismos 43 casos y los mismos valores.

- **El bloqueo:** el Portal Administrativo todavía no implementa el alta de HU-056 (`POST /api/integracion/v1/jugadores`). Sin ella, ninguna alta del Deportivo llega al Administrativo y los casos que dependen del alta quedan BLOCKED, nunca PASS.
- **Lo que ya se pudo comprobar pasó:**
  - autenticación;
  - que un alta fallida no deje datos a medias;
  - rechazos sin DNI y sin categoría en el Deportivo;
  - perfiles no jugadores.

Fecha de ejecución: 29/10/2026. Rama `FramD`, HEAD `cc5e0d8` más cambios sin commitear.

---

## 1. Objetivo

Verificar con las APIs reales y SQL Server real que un alta hecha en el Portal Deportivo deja a la persona y al jugador listos para cobranzas en el Administrativo. Eso incluye:

- identidad única y datos correctos;
- cuota del mes según el calendario vigente (HU-083);
- monto según la jerarquía de aranceles (HU-048);
- inscripción masculina (HU-033);
- sin cargos para altas incompletas, perfiles no jugadores o jugadores sin tarifa.

HU-060 es de QA. **No se reimplementó HU-056.**

## 2. Dependencias (estado real, verificado)

| HU | Estado | Evidencia |
|---|---|---|
| HU-053 base unificada | **Disponible** | El reset crea el esquema unificado y aplica las 25 migraciones; ENV-1 a ENV-4 PASS. |
| HU-054 contrato | **Incompleta** | `docs/integracion/README.md` no documenta `POST /api/integracion/v1/jugadores` (DEP-2). Sí documenta cargos-alta, baja y reincorporación. |
| HU-055 autenticación | **Disponible** | AUT-1 y AUT-2 PASS; la suite de HU-059 también la prueba. |
| HU-056 alta integrada | **Bloqueada en el Administrativo** | Ver el detalle abajo. |
| HU-059 entorno E2E | **Disponible** | El entorno corre completo; HU-059 da 32/1/5, sin cambios por este trabajo. |
| HU-048 jerarquía de aranceles | **Disponible** | `ArancelesDao.ObtenerMontoVigente`: categoría y después género, vigente al último día del mes. 23 tests en verde. |
| HU-033 inscripción | **Disponible** | `JugadoresAltaService`: varón con inscripción vigente; mujer o $0 no paga. 65 tests en verde (EnrollmentFee 23, Inscripción 21, PlayerEnrollment 21) y 17 de cargos de alta (`CargosDeAlta`). |
| HU-083 calendario (día 10) | **Disponible** | `CalendarioCuotas`, 13 tests en verde. Limitación: la API no admite un reloj de negocio inyectable (ver CUO-2). |
| HU-084 pago adelantado | Disponible; no afecta los datos | Ningún fixture de HU-060 tiene pagos adelantados. |
| Auditoría / HU-058 | **Disponible** | `AUDITORIA_CAMBIOS.origen_sistema` (V20261028_01) y la tabla `INTEGRACION_RECHAZOS` (V20261028_02) existen y están migradas. |

Detalle de HU-056:
- **Administrativo:** `JugadoresAltaService.AltaDesdeIntegracion` y `CambiarCategoria` lanzan `NotImplementedException`, y no hay ruta, así que `POST /api/integracion/v1/jugadores` da 404 (DEP-1).
- **Deportivo:** su lado está hecho, con el parche `ellos-alta-por-integracion` del banco.

Suite xUnit completa contra `ClubCamionerosUnificadaV2_HU057_Prueba`: **716 tests, 679 PASS, 37 FAIL.** Los 37 son todos de `AltaJugadoresIntegracionIntegrationTests` (HU-056) y fallan por `System.NotImplementedException`. No hay ninguna otra falla.

## 3. Entorno (HU-059)

| Pieza | Valor |
|---|---|
| Administrativo | `ApiGestion.dll` de este repo (working tree), `http://localhost:5418`, migraciones y generador apagados al arrancar (el generador se enciende para NOJ-3) |
| Deportivo | `CACC.API.dll` de `NejoCHZ/App-Club-Atletico-Camioneros@afa4e60` con los 5 parches del banco `cacc-unificacion` (incluido `ellos-alta-por-integracion`), `http://localhost:5419` |
| SQL Server | LocalDB 17.0.4025.3, base `ClubCamionerosUnificadaV2_HU059_Prueba` |
| Credenciales | Clave JWT y credencial de integración al azar en memoria en cada corrida (`qa/HU-059/comun.ps1`). Contraseña del staff creado: al azar, no se imprime. |
| Correo | SMTP apuntado a `127.0.0.1:9`, un puerto cerrado: ningún correo sale de las pruebas. |
| Herramientas | .NET SDK 10.0.400, PowerShell 7.6, sqlcmd 15 |

No se usó producción ni `CCACUnificada`. El reset solo acepta bases cuyo nombre termine en `Prueba` o `_E2E`.

## 4. Versión y base

- Administrativo: `cc5e0d8` más cambios sin commitear, incluidos HU-055, HU-057 y HU-056 en rojo.
- Deportivo: `afa4e60` parcheado.
- Esquema: `database/esquema/ClubCamioneros_unificada_crear_base.sql` más 25 migraciones de `database/migrations`, aplicadas con el migrador de la API.

## 5. Fixtures

Se cargan en dos capas, sin pasos manuales:
1. El reset de HU-059: cuentas ficticias más los fixtures 9859xxxx.
2. `qa/HU-060/datos/03_fixtures_hu060.sql`.

Son solo **precondiciones**. Las altas, el staff y las habilitaciones se hacen siempre por las APIs.

| # pedido | Fixture | Dato |
|---|---|---|
| 1 | Jugador completamente nuevo | DNI 98600001, Masculino, cat. 4 (no existe antes) |
| 2 | PERSONA existente sin JUGADOR | 98600002 (`Previa Sin Jugador`), PK 11 |
| 3 | DNI ya usado (PERSONA + JUGADOR) | 98600003, jugador 5, cat. 3, alta 01/08/2026 |
| 4 | Jugador masculino | 98600001 (cat. 4) y 98600004 (cat. 3) |
| 5 | Jugadora | 98600005, Femenino, cat. 13 |
| 6 | Categoría con arancel propio | cat. 4: $1500 desde 01/01/2026 y $1800 desde 01/01/2027 (futuro); cat. 13: $800 |
| 7 | Categoría sin arancel propio, con tarifa de género | cat. 3, que cae en Masculino general: $1000 |
| 8 | Sin categoría | alta 98600007 sin `categoria` |
| 9 | Sin DNI | alta sin `dni` |
| 10 | Perfiles no jugadores | staff 98600010 (DT), 98600011 (Médico), 98600012 (Administrador); cuentas de portal 20111222, 30000002 a 30000005 |
| 11 | Sin arancel aplicable | 98600006, Femenino, cat. 12: sin arancel de categoría ni arancel general femenino |
| — | Inscripción | Masculino $500 desde 01/01/2026; sin inscripción femenina |

**La prueba no escribe importes.** Los esperados salen de la base:
- la cuota, con la jerarquía de HU-048 aplicada al último día del mes;
- la inscripción, con el arancel vigente de su género.

ENV-4 verifica que los fixtures cubren todas las ramas de la jerarquía (`1500.00|1000.00|800.00||500.00|`).

## 6. Fecha de negocio

La API usa `RelojNegocio.Hoy`, la hora real de Argentina, y **no admite un reloj inyectable**. El reloj inyectable (`Func<DateTime>`) existe solo dentro de `JugadoresAltaService`, para las pruebas de servicio. No se cambió la fecha de la máquina.

La suite lee la fecha de negocio de SQL Server con la misma regla y calcula lo esperado para ese día:

| Corrida | Fecha de negocio | Rama HU-083 |
|---|---|---|
| 1 y 2 | **29/10/2026** (día ≥ 10) | El alta debe emitir la cuota de octubre de 2026 |

La rama "alta antes del día 10, sin cuota" queda **BLOCKED** (CUO-2). La regla existe y está cubierta por tests de servicio de HU-056, que hoy están en rojo por `NotImplementedException`.

## 7. Casos

Pasos comunes a todos:
1. Reset: HU-059 más HU-060.
2. Levantar las dos APIs y esperar el healthcheck, que confirma que cada una llega a SQL.
3. Dar el alta por el flujo real: `POST /api/jugadores` del Deportivo con el token de un usuario administrador. El Deportivo llama a `POST /api/integracion/v1/jugadores` del Administrativo con la credencial.
4. Validar la respuesta, las APIs del Administrativo y SQL, este último solo para verificar.

Si un caso depende de HU-056 y el Administrativo no la tiene, se ejecuta igual el pedido real y se registra BLOCKED con la respuesta obtenida.

| ID | Pedido del enunciado | Pasos / fixture | Esperado |
|---|---|---|---|
| DEP-1 | Dependencias | `POST /api/integracion/v1/jugadores` con credencial y cuerpo vacío | Ruta existente (400 con `errorCode`) |
| DEP-2 | Dependencias (HU-054) | Buscar la sección del alta en `docs/integracion/README.md` | Documentada |
| AUT-1 | HU-055 | Alta del contrato sin credencial y con credencial inválida | 401/401; ninguna persona nueva |
| AUT-2 | HU-055 (no exponer al navegador) | Buscar `X-Integration-Key`, `ClaveIntegracion` e `integracion/v1` en `frontend-cacc/src` y en el FrontEnd del Deportivo | 0 archivos |
| ALT-0 | Rollback | Alta 98600001 cuando el Administrativo no la acepta | ≥ 400 y nada creado (`0\|0\|0\|0`) |
| ALT-1 | Caso 1 | Alta 98600001 "Alta Completa Hu", Masculino, cat. 4 | 201 con `idJugador` y `creado` |
| ALT-2 | Caso 1, SQL | PERSONA y JUGADOR | 1 PERSONA, 1 JUGADOR, nombre/apellido/DNI/género/cat. 4/fecha de alta = hoy, `JUGADORES_CATEGORIAS`, FK correcta |
| ALT-3 | Identidad compartida | `GET /api/jugadores/{id}` en las dos APIs; personas con el mismo nombre | El mismo id y DNI en las dos; 1 sola persona |
| ALT-4 | Padrón | `GET /api/jugadores` (Administrativo) | Activo, DNI, Masculino |
| ALT-5 | Búsqueda | `GET /api/players/search?term=98600001` | 1 resultado con el id |
| ALT-6 | Cobranzas | `GET /api/pagos/player-accounts` y `GET /api/pagos/jugador/{id}/estado-de-cuenta` | Aparece; 200 |
| CUO-1 | Caso 2, calendario | Cuotas del jugador | 1 cuota del mes (día ≥ 10) |
| CUO-2 | Caso 2, la otra rama | Alta antes del día 10 | Sin cuota: necesita controlar la fecha de negocio |
| CUO-3 | HU-048, caso A | 98600001, cat. 4 con arancel propio | Monto = arancel de la cat. 4 vigente (sale de la base: 1500.00) |
| CUO-4 | HU-048, caso B | Alta 98600004, cat. 3, sin arancel propio | Monto = arancel Masculino general (1000.00) |
| CUO-5 | Monto histórico | Comparar con el último arancel cargado de la cat. 4 (1800.00, futuro) | Distinto de 1800.00 |
| CUO-6 | Monto histórico / sin duplicar | Después de la generación manual (septiembre) y el generador automático | 1 sola cuota, con el mismo monto |
| INS-1 | Caso 3 | Inscripciones de 98600001 | Una sola, por el arancel vigente (500.00), con fecha = alta; separada de la cuota |
| INS-2 | Caso 3 | Inscripciones de 98600004 | Una sola (500.00) |
| REI-1 | Inscripción no duplicada / reintentos | La misma alta de 98600001 dos veces más | 200/200 y `1\|1\|1\|1` sin cambios |
| FEM-1 | Caso 4 | Alta 98600005, Femenino, cat. 13 | 201; persona Femenino, cat. 13 |
| FEM-2 | Caso 4 | Inscripciones de la jugadora | Ninguna |
| FEM-3 | Caso 4 | Cuota de la jugadora | Arancel de la cat. 13 (800.00) |
| DNI-1 | Caso 5 | Alta 98600002 con OTRO nombre | Misma PK (11), datos sin pisar, JUGADOR → PK 11 |
| DNI-2 | DNI con jugador existente | Reenviar el alta de 98600003 | 200 con el id 5; personas, jugadores, cuotas e inscripciones sin cambio |
| REI-2 | Reintentos | Repetir el alta de 98600002 | 200 y nada duplicado |
| CAT-1 | Caso 6 (Deportivo) | Alta sin `categoria` | 400 `field: idCategoria`; sin persona ni cargos |
| CAT-2 | Caso 6 (contrato) | Contrato sin `idCategoria`, con credencial | 400 `INVALID_INPUT` `field: idCategoria`; rechazo `AltaJugador`, origen PortalDeportivo, motivo sin DNI; sin datos |
| SDN-1 | Caso 7 (Deportivo) | Alta sin `dni` | 400; ninguna persona nueva ni cargos |
| SDN-2 | Caso 7 (contrato) | Contrato sin `dni` | 400 `INVALID_INPUT` `field: dni`; +1 rechazo; sin datos |
| SAR-1 | Caso 9 | Alta 98600006, Femenino, cat. 12 | Política acordada para HU-056: el alta se registra (201) **sin cuota ni cargos en $0** (`1\|1\|0\|0`) |
| SAR-2 | No afectar el lote | Alta válida 98600008, cat. 3, después de SAR-1 | 201 con su cuota (1000.00) |
| AUD-1 | Auditoría (alta exitosa) | `AUDITORIA_CAMBIOS` | INSERT de PERSONAS y de JUGADORES, origen `PortalDeportivo`, operador del token, fecha de hoy |
| AUD-2 | Auditoría (rechazo) | INSERT auditados con el DNI rechazado | 0 |
| NOJ-1 | Caso 8 | `POST /api/staff` del Deportivo: DT, Médico, Administrador | 201 ×3; PERSONA + USUARIO con rol 4/3/1 |
| NOJ-2 | Caso 8 | Administrativo: `GET /api/usuarios/candidatos` y `POST /api/usuarios/{id}/habilitar` | Candidato; 200; `acceso_portal 1`, `rol_portal 2` |
| NOJ-3 | Caso 8 | Generación manual de septiembre y generador automático (polling hasta que emite al jugador de control) | 0 JUGADORES y 0 cargos para los 8 perfiles |
| NOJ-4 | Caso 8 | Padrón, búsqueda y cobranzas | No aparecen (`0\|0\|0`) |
| ENV-1 a ENV-4 y LOG-1 | Entorno | Healthchecks, fixtures, logs | Listos; logs sin credencial, clave JWT, contraseñas ni tokens |

Sobre los lotes: el contrato no tiene alta en lote. Cada alta del Deportivo es un pedido independiente. Por eso "no afectar todo el lote" se prueba como SAR-1 seguido de SAR-2: una falla por tarifa no debe impedir la siguiente alta.

## 8. Resultados

Evidencia: `qa/HU-060/evidencia_ejecucion1.json`, `evidencia_ejecucion2.json`, `salida_ejecucion1.txt` y `salida_ejecucion2.txt`. Duración: 9 s por corrida.

| ID | Resultado real (corrida 1 = corrida 2) | Estado | Bug |
|---|---|---|---|
| ENV-1 a ENV-4 | Listos; jerarquía de fixtures `1500.00\|1000.00\|800.00\|\|500.00\|` | PASS | — |
| DEP-1 | `404` sin cuerpo | **BLOCKED** | BUG-060-1 |
| DEP-2 | No documentado | **BLOCKED** | BUG-060-2 |
| AUT-1 | 401/401; personas 12 → 12 | PASS | — |
| AUT-2 | 0 archivos | PASS | — |
| ALT-0 | `503 {"message":"El Portal Administrativo no responde: el jugador no se registró. Intentá de nuevo."}`; `0\|0\|0\|0` | PASS | BUG-059-3 (mensaje engañoso) |
| ALT-1 a ALT-6 | El Deportivo responde 503 porque el Administrativo da 404 | **BLOCKED** | BUG-060-1 |
| CUO-1, CUO-3 a CUO-6 | Ídem | **BLOCKED** | BUG-060-1 |
| CUO-2 | No hay reloj de negocio inyectable en la API | **BLOCKED** | MEJ-060-1 |
| INS-1, INS-2, REI-1, REI-2 | Ídem ALT | **BLOCKED** | BUG-060-1 |
| FEM-1 a FEM-3 | Ídem | **BLOCKED** | BUG-060-1 |
| DNI-1, DNI-2 | Ídem | **BLOCKED** | BUG-060-1 |
| CAT-1 | `400 {"message":"La categoría es obligatoria.","field":"idCategoria"}`; `0\|0\|0\|0`; PAGOS sin cambio | PASS | — |
| CAT-2 | 404 (no hay ruta) | **BLOCKED** | BUG-060-1 |
| SDN-1 | `400 {"message":"El nombre y el DNI son obligatorios."}`; personas 12 → 12 | PASS | OBS-060-1 (sin `field`) |
| SDN-2 | 404 | **BLOCKED** | BUG-060-1 |
| SAR-1, SAR-2 | Ídem ALT | **BLOCKED** | BUG-060-1 |
| AUD-1, AUD-2 | Ídem | **BLOCKED** | BUG-060-1 |
| NOJ-1 | 201/201/201; `98600010:4,98600011:3,98600012:1` | PASS | — |
| NOJ-2 | 200; 200 (`"Se habilitó el acceso, pero no se pudo enviar el correo..."`, esperable: el SMTP de prueba está cerrado); `acceso 1\|2` | PASS | — |
| NOJ-3 | Manual 200 y generador automático (cuota de control = 1); `0\|0` | PASS | — |
| NOJ-4 | `0\|0\|0` | PASS | — |
| LOG-1 | 0 secretos en los logs | PASS | — |

**Totales: 14 PASS · 0 FAIL · 29 BLOCKED**, con las mismas 43 filas y los mismos valores en las dos corridas.

Por grupo:

| Grupo | Estado | PASS | FAIL | BLOCKED |
|---|---|---|---|---|
| Entorno | PASS | 4 | 0 | 0 |
| Dependencias | BLOCKED | 0 | 0 | 2 |
| HU-055 autenticación | PASS | 2 | 0 | 0 |
| Caso 1 alta completa | BLOCKED | 1 | 0 | 6 |
| Caso 2 cuota y aranceles | BLOCKED | 0 | 0 | 6 |
| Caso 3 inscripción masculina | BLOCKED | 0 | 0 | 2 |
| Caso 4 jugadora | BLOCKED | 0 | 0 | 3 |
| Caso 5 DNI repetido | BLOCKED | 0 | 0 | 2 |
| Caso 6 sin categoría | BLOCKED | 1 | 0 | 1 |
| Caso 7 sin DNI | BLOCKED | 1 | 0 | 1 |
| Caso 8 no jugadores | **PASS** | 4 | 0 | 0 |
| Caso 9 sin arancel | BLOCKED | 0 | 0 | 2 |
| Auditoría | BLOCKED | 0 | 0 | 2 |
| Reintentos | BLOCKED | 0 | 0 | 2 |
| Logs | PASS | 1 | 0 | 0 |

### IDs relevantes

| Qué | ID |
|---|---|
| SuperAdmin (operador del token) | USUARIO 1 |
| PERSONA previa 98600002 | 11 |
| JUGADOR previo 98600003 | 5 |
| USUARIO del Administrador creado por el Deportivo (98600012) | 8 |
| Jugadores de las altas | Ninguno: no se crearon (BLOCKED) |

### Importes esperados y obtenidos

| Caso | Esperado (sale de la base) | Obtenido |
|---|---|---|
| Cuota cat. 4 (arancel de categoría) | 1500.00 @ 2026-10-01 | Sin alta (BLOCKED) |
| Cuota cat. 3 (fallback de género) | 1000.00 @ 2026-10-01 | Sin alta (BLOCKED) |
| Cuota cat. 13 (jugadora) | 800.00 @ 2026-10-01 | Sin alta (BLOCKED) |
| Cuota cat. 12 Femenino (sin arancel) | Ninguna | Sin alta (BLOCKED) |
| Inscripción masculina | 500.00 @ fecha del alta | Sin alta (BLOCKED) |
| Inscripción femenina | Ninguna | Sin alta (BLOCKED) |

## 9. Evidencias

- `qa/HU-060/evidencia_ejecucion{1,2}.json` tiene cada caso con su esperado, su obtenido y el detalle de la respuesta HTTP. Incluye además la fecha de negocio, si HU-056 estaba disponible y los IDs.
- `qa/HU-060/salida_ejecucion{1,2}.txt`: la salida de consola.
- Logs de cada API y del reset: `%TEMP%\cacc-hu060\ejecucion_<fecha>\`. No tienen secretos (LOG-1); en la evidencia versionada se buscaron `eyJ` y cadenas largas en base64, sin resultados.

### Autoprueba de las aserciones (no es evidencia de HU-060)

Los casos BLOCKED nunca ejercitaron sus verificaciones. Para no entregar aserciones sin probar, corrí **una sola vez** una copia descartable de la suite, en el scratchpad y sin versionar. Esa copia hace dos cosas distintas:
- da el alta por `POST /api/players`, el alta propia del Administrativo (HU-033), que aplica las mismas reglas de cuota e inscripción;
- fuerza "HU-056 disponible".

Resultado: 31 PASS · 9 FAIL · 2 BLOCKED. **Todos los FAIL tienen una causa conocida del camino sustituto, no de la prueba:**

| FAIL | Por qué |
|---|---|
| ALT-2 | El alta propia no carga `JUGADORES_CATEGORIAS`; HU-056 sí, según su spec |
| REI-1, DNI-1, DNI-2, REI-2 | El alta propia rechaza un DNI existente (400); HU-056 lo reutiliza y responde 200 |
| CAT-1 | El 400 del Administrativo no trae `field`; el del Deportivo sí |
| CAT-2, SDN-2 | La ruta de HU-056 no existe (404) |
| AUD-1 | Audita `origen_sistema = PortalAdministrativo`, no `PortalDeportivo`. Se verificó en SQL que la consulta encuentra los dos INSERT con operador 1 y fecha de hoy: el único campo distinto es el origen |

Lo que sí quedó probado, con importes reales:
- **Cuotas:** 1500.00 para la categoría, 1000.00 por el fallback de género y 800.00 para la jugadora; ninguna por encima del arancel futuro de 1800.00.
- **Inscripciones:** 500.00 por varón; ninguna para la jugadora.
- **Sin arancel aplicable:** `1|1|0|0`, sin cargos en $0.
- **Padrón, búsqueda y cobranzas:** encuentran al jugador.
- **Generaciones:** después de la manual y la automática, la cuota del alta no se duplica y conserva su monto.

Las aserciones de cargos, padrón y cobranzas funcionan. **Esto NO es un PASS de HU-060:** el alta no vino del Deportivo.

### Errores de la propia prueba, corregidos

- El primer fixture cargaba los aranceles de categoría con género (`'Masculino', 4, ...`). La base lo rechazó con `UX_ARANCELES_GENERO_FECHA`, y el esquema exige que un arancel sea de categoría **o** de género (`CK_ARANCELES_GENERO_O_CATEGORIA`). Se corrigió el fixture a `(NULL, 4, ...)`. No es un defecto del producto.

## 10. Bugs (listos para Jira, vinculados a HU-060)

### BUG-060-1: el alta de jugadores desde el Portal Deportivo no llega al Administrativo (falta `POST /api/integracion/v1/jugadores`)

| Campo | Valor |
|---|---|
| HU funcional | **HU-056**; vínculo: HU-060 (bloquea 27 casos) |
| Severidad sugerida | **Bloqueante** |
| Ambiente | Entorno HU-059: Administrativo `cc5e0d8` + working tree; Deportivo `afa4e60` + parches; base `ClubCamionerosUnificadaV2_HU059_Prueba`; fecha de negocio 29/10/2026 |
| Precondiciones | Reset de HU-059 + `qa/HU-060/datos/03_fixtures_hu060.sql`; credencial de integración válida |
| Pasos | 1. Login de un administrador. 2. `POST http://localhost:5419/api/jugadores` con `{dni:"98600001", nombre:"Alta", apellido:"Completa Hu", genero:"Masculino", fechaNacimiento:"2010-06-15", categoria:"4"}`. 3. Directo al Administrativo: `POST http://localhost:5418/api/integracion/v1/jugadores` con `X-Integration-Key` válida y JWT |
| Esperado | 201 con `idJugador`; PERSONA, JUGADOR, cuota e inscripción en una sola transacción (decisión de HU-056) |
| Obtenido | Deportivo: `503 "El Portal Administrativo no responde: el jugador no se registró."`. Administrativo: `404` sin cuerpo. En el código, `JugadoresAltaService.AltaDesdeIntegracion` y `CambiarCategoria` lanzan `NotImplementedException`, no hay acción en `IntegracionJugadoresController` y los 37 tests de `AltaJugadoresIntegracionIntegrationTests` fallan |
| Evidencia | DEP-1, ALT-0 y ALT-1 en `evidencia_ejecucion1.json`; `ServiceLibrary/JugadoresAltaService.cs` (métodos `AltaDesdeIntegracion` y `CambiarCategoria`); suite xUnit 679/716 |

### BUG-060-2: el contrato de integración no documenta el alta ni el cambio de categoría

| Campo | Valor |
|---|---|
| HU funcional | **HU-054** (y HU-056); vínculo: HU-060 |
| Severidad sugerida | Media |
| Ambiente | Repo del Administrativo, `docs/integracion/README.md` y `openapi-v1.json` |
| Pasos | Buscar `POST /api/integracion/v1/jugadores` y `PUT /api/integracion/v1/jugadores/{id}/categoria` en el contrato |
| Esperado | Ruta, cuerpo, códigos (201/200/400/409/422), errores con `field` y semántica de idempotencia, documentados igual que baja y reincorporación |
| Obtenido | No figuran. El Deportivo ya los consume (`EstadoDeudaClient.CrearJugadorAsync` y `CambiarCategoriaAsync`). El contrato real solo existe en los tests rojos |
| Evidencia | DEP-2 |

### BUG-059-3 (ya registrado en HU-059): el Deportivo informa "no responde" (503) cuando el Administrativo responde 404

Se repite en ALT-0. No lo duplico: vincular el ticket de HU-059 también a HU-060.

### Observaciones (sin ticket de Bug, para que el equipo decida)

- **MEJ-060-1 (testabilidad, HU-083):** la API no permite fijar la fecha de negocio en Testing (`RelojNegocio.Hoy` es estático). Sin eso, la rama "antes del día 10" del alta no se puede probar de punta a punta (CUO-2).
  - Propuesta: una opción `RelojNegocio:FechaFija`, aceptada solo con `ASPNETCORE_ENVIRONMENT=Testing`.
  - Queda fuera del alcance autorizado: **no se implementó.**
- **OBS-060-1:** el Deportivo valida antes de llamar al Administrativo, así que los rechazos sin DNI o sin categoría **no llegan** a `INTEGRACION_RECHAZOS` (HU-058). El motivo sí vuelve al usuario en el 400.
  - El 400 por DNI faltante no trae `field` (`"El nombre y el DNI son obligatorios."`); el de categoría sí.
- **OBS-060-2:** el staff que crea el Deportivo (`POST /api/staff`) se escribe directo en la base compartida, así que la auditoría queda con `origen_sistema` NULL. Es el mismo patrón que BUG-059-1. No afecta a HU-060: no genera cargos.

## 11. Regresión

No hubo correcciones de producto, porque no estaban autorizadas: HU-056 no se reimplementó. Cambios hechos y su regresión:

| Cambio | Regresión |
|---|---|
| `qa/HU-059/comun.ps1` (nuevo): la infraestructura de `e2e_hu059.ps1` en un archivo compartido, más SMTP a un puerto cerrado para que ninguna prueba mande correos | `e2e_hu059.ps1` antes y después: **32 PASS · 1 FAIL · 5 BLOCKED**, sin cambios |
| Suite xUnit completa | 716 total: 679 PASS, 37 FAIL (los mismos de HU-056: `NotImplementedException`); 0 regresiones |
| `e2e_hu060.ps1`, dos corridas | Idénticas: 14/0/29 |

Cuando HU-056 esté implementada:
1. Correr `pwsh -File qa\HU-060\e2e_hu060.ps1`. No hay que tocar la suite: los casos BLOCKED pasan a ejecutarse solos.
2. Correr también `e2e_hu059.ps1` y la suite xUnit (`AltaJugadoresIntegracionIntegrationTests`).

Esa es la regresión mínima que pide la HU: alta normal, DNI repetido, masculino, femenino, datos incompletos, sin arancel y suite de HU-056.

## 12. Conclusión

**HU-060 NO está terminada.** El entorno y la suite están listos y son reproducibles. No hay ningún FAIL, pero **29 casos están BLOCKED**:
- 27 por **HU-056 sin implementar** en el Administrativo (BUG-060-1): DEP-1 más los 26 casos que necesitan el alta;
- 1 (DEP-2) porque el contrato de HU-054 no documenta el alta (BUG-060-2);
- 1 (CUO-2) porque la API no tiene un reloj de negocio inyectable (MEJ-060-1).

Ya comprobado sobre HU-059, con APIs y SQL reales:
- la credencial de integración protege el alta y no llega a ningún frontend;
- un alta que el Administrativo no acepta no deja identidad ni cargos a medias;
- las altas sin categoría o sin DNI se rechazan sin crear nada;
- los perfiles no jugadores (DT, Médico, Administrador habilitado y las cuentas de portal) no son jugadores, no aparecen en padrón, búsqueda ni cobranzas, y no reciben cuotas ni inscripción, ni siquiera después de la generación manual y la automática.

| Criterio de "listo" | Estado |
|---|---|
| Todos los casos ejecutados sobre HU-059 | Ejecutados; 29 BLOCKED |
| Un alta válida aparece en el Administrativo | BLOCKED (BUG-060-1) |
| Datos personales correctos | BLOCKED |
| Género correcto | BLOCKED |
| Categoría correcta | BLOCKED |
| La identidad no se duplica | BLOCKED (ALT-0 confirma que una falla no crea identidad) |
| Un DNI existente reutiliza la PERSONA | BLOCKED |
| Cuota solo cuando corresponde por calendario | BLOCKED (CUO-1, CUO-2) |
| Arancel según la jerarquía vigente | BLOCKED (validado solo en la autoprueba y en 23 tests de HU-048) |
| Inscripción masculina correcta | BLOCKED |
| Una jugadora no recibe inscripción masculina | BLOCKED |
| Altas incompletas sin cuotas incorrectas | **PASS** del lado del Deportivo (CAT-1, SDN-1); BLOCKED del lado del contrato (CAT-2, SDN-2) |
| Perfiles no jugadores sin cargos de jugador | **PASS** (NOJ-1 a NOJ-4) |
| Jugador sin tarifa sin monto inventado | BLOCKED (SAR-1) |
| Resultados documentados | **PASS** (este documento + evidencia) |
| Todo defecto con su Bug Jira | **Preparados** (BUG-060-1, BUG-060-2; BUG-059-3 se vincula). Falta cargarlos en Jira |

## Cómo repetirlo

```powershell
# Una vez por versión del código: el Arranque de database/InstructivoEquipo.md §7.2 (HU-059).
pwsh -File qa\HU-060\e2e_hu060.ps1                       # reset + fixtures + 43 casos; sale con 0 solo si todo es PASS
pwsh -File qa\HU-060\e2e_hu060.ps1 -Evidencia <archivo>  # además guarda el JSON de resultados
```

Usa las mismas variables `CACC_E2E_*` que HU-059. Hay una más: `CACC_E2E_DEPORTIVO_FRONTEND`, con las fuentes del frontend del Deportivo para AUT-2.
