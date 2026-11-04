# QA HU-056: alta de jugadores y cambio de categoría informados por el sistema deportivo

**Fecha:** 29/10/2026 · **Rama:** `FramD` (`cc5e0d8` + cambios sin commitear) · **Bases:** solo de prueba (LocalDB):
`CACC_HU056_IT_20261029_1340_Prueba` (xUnit) y `ClubCamionerosUnificadaV2_HU059_Prueba` (E2E, recreada en cada corrida).
No se tocó producción ni las bases de origen.

**Estado: TÉCNICAMENTE TERMINADA, LISTA PARA REVISIÓN.** Falta el code review humano, y la adopción del parche
`ellos-alta-por-integracion` por el equipo Deportivo (ver más abajo).

---

## 0. Revisión de code review: 29/10/2026, noche

**MEDIUM: normalización del DNI. CONFIRMADO y corregido.**

- **El problema:** el alta integrada buscaba la persona con `WHERE Dni = @dni` (literal), mientras que `ExistsPersonByDni` y el alta del portal comparan con `LTRIM(RTRIM(Dni))`.
- **Alcance real:** en la base unificada no se manifiesta, porque `CK_PERSONAS_dni_digitos` está habilitado y es confiable: solo admite dígitos. Sí se manifiesta en una base sin ese CHECK, por ejemplo el esquema viejo.
- **Archivos:** `PlayerDao.cs` y `PlayerDAO.cs` son el mismo archivo (git lo tiene con una capitalización y el disco con la otra).
- **Corrección:**
  - la búsqueda usa la misma comparación canónica que el resto;
  - un DNI histórico con espacios es la misma persona: se reutiliza sin modificar el dato guardado;
  - si dos personas comparten el DNI canónico, el alta responde **409 "requiere revisión manual"** (estado nuevo `VariasPersonas`), sin elegir ni fusionar;
  - no se modifica ningún registro existente.
- **Pruebas:** 2 nuevas, que deshabilitan el CHECK solo para sembrar y lo vuelven a habilitar `WITH CHECK` (verificado: `is_not_trusted = 0` y sin restos).
  - Con la búsqueda literal anterior **fallan por sus aserciones**: `PersonaExistente` en la primera, `Exito` en la segunda.
  - Con la corrección pasan.
  - Una primera "prueba en rojo" no valía: fallaba por un error del helper (`OUTPUT` sin `INTO` en una tabla con trigger). Se corrigió el helper y se repitió.

**LOW: reloj de negocio. CONFIRMADO; resuelto por decisión del usuario (29/10, opción A).**

- **La regla:** la fecha de nacimiento "no futura" se valida contra la **fecha real** del club (`RelojNegocio.Hoy`), no contra la fecha de negocio del alta. En producción son la misma fecha.
- **Cambios:** la lógica no cambió. Se agregó un comentario en el servicio y 3 pruebas que fijan la regla:
  - reloj del alta en 2000 con nacimiento en 2012: se acepta;
  - reloj en 2030 con nacimiento mañana (fecha real): se rechaza con `field=fechaNacimiento` y no se crea nada;
  - nacimiento hoy: se acepta.
- **Verificación:** con la opción B aplicada temporalmente, fallan las dos primeras.

**Regresión:** backend **745/745**; E2E del PUT 14/0/0; E2E de altas (HU-060) 43/0/0.

## 1. Historia de la implementación

| Fecha | Quién | Qué |
|---|---|---|
| 28/10 | Claude Code + usuario | Decisiones (alta por `POST /api/integracion/v1/jugadores` en una transacción; cambio de categoría por `PUT .../{id}/categoria`; tabla `INTEGRACION_RECHAZOS` sin DNI ni nombres; DNI con jugador activo → idempotente 200; con jugador dado de baja → 409). Migración `V20261028_02`, tipos, **37 pruebas de integración en rojo** (TDD) y el parche del Deportivo `ellos-alta-por-integracion` (137/137 pruebas del Deportivo). |
| 29/10, 13:46–13:52 | GitHub Copilot | Implementación del lado Administrativo: `JugadoresAltaService.AltaDesdeIntegracion` / `CambiarCategoria`, `PlayerDAO.AltaDesdeIntegracion` / `CambiarCategoria`, rutas POST/PUT en `IntegracionJugadoresController`, DTOs, `RegistroRechazosIntegracion`, registro en DI, pruebas de controller, contrato en `docs/integracion/README.md` y `openapi-v1.json`. |
| 29/10, tarde | Claude Code | Cierre técnico: corrección del rango de DNI (sección 2), suite E2E reproducible del PUT (sección 4) y re-ejecución de todas las pruebas. |

**Revisión del código:** la implementación de Copilot se verificó **por comportamiento** (las pruebas de abajo). **No tuvo code review línea por línea.** Es lo primero que falta para cerrar.

## 2. Discrepancia del DNI (6–9 vs. 7–9): resuelta

| Dónde | Regla | Origen |
|---|---|---|
| `CK_PERSONAS_dni_digitos`, login, habilitar cuenta, `GET estado-deuda/dni` | 6 a 9 dígitos | Identidades que la base **admite** (HU-053 / V20261004_01) |
| Alta de jugador del portal (`PlayerRequestDTO`, en `HEAD`) | **7 u 8** | Regla existente |
| Alta del Deportivo (`JugadoresController.DniValido`, parche HU-056) | **7 u 8** | "La misma regla que aplica el Portal Administrativo" |
| Alta por integración (Copilot) | 7 a 9 | **Sin fuente**: no coincidía con ninguna de las dos |

**Corrección:** el alta por integración pasa a **7 u 8**, la regla de alta que ya existía en los dos sistemas. No se cambió
ninguna regla de negocio: el 6 a 9 sigue siendo el rango de identidades.

- Cambios: `ServiceLibrary/JugadoresAltaService.cs`, `docs/integracion/README.md`, `openapi-v1.json` (`^[0-9]{7,8}$`).
- Prueba nueva: `"123456789"` → 400 `field=dni`. Con el código de 7 a 9 ese DNI se aceptaba.
- **Para decidir con el club (HU-053):** la base B tiene 2 jugadores y 1 persona con DNI de 9 dígitos.

Un intento de validar también la fecha de nacimiento contra el reloj inyectado rompió
`SinArancelVigente_NoSeGeneraLaCuota_NiSeGrabaNingunCargoEnCero`. **Se revirtió** el cambio, no se tocó la prueba: no estaba
en el alcance.

## 3. Reloj de negocio para pruebas

`Pruebas:FechaNegocio` (`yyyy-MM-dd`, en `ApiGestion/Program.cs`) fija el "hoy" del servicio de alta.

- Solo se admite en **Development o Testing**; en cualquier otro entorno la API **no arranca**.
- El camino del alta toma todas sus fechas del servicio (`fecha_alta`, período de la cuota, fecha de inscripción) y no del reloj de SQL. Por eso esto alcanza para probar las dos ramas del día 10.
- Evidencia en QA-HU-060 (CUO-2 y REL-1).

## 4. Pruebas ejecutadas

| Prueba | Comando | Resultado |
|---|---|---|
| Integración + controller de HU-056 | `CACC_TEST_DB=<..._Prueba> dotnet test ApiGestion.Tests --filter AltaJugadoresIntegracion` | **43 / 43 PASS** |
| Regresión backend completa | `dotnet test ApiGestion.Tests` con `CACC_TEST_DB` | **720 / 720 PASS**, 0 omitidas |
| Frontend | `ng test --watch=false` | **449 / 449 PASS** |
| E2E del cambio de categoría (nuevo) | `pwsh -File qa\HU-056\e2e_hu056_categoria.ps1` | **14 PASS · 0 FAIL · 0 BLOCKED, dos corridas**, código de salida 0 (`qa/HU-056/salida_categoria{1,2}.txt`, `evidencia_categoria{1,2}.json`) |
| E2E del alta por el Deportivo | `pwsh -File qa\HU-060\e2e_hu060.ps1` | **43 / 0 / 0, dos corridas** (QA-HU-060) |

### El "fallo de limpieza" del PUT

Copilot informó que su prueba del PUT "terminó con código 1 durante la limpieza". Esa prueba **no quedó guardada en
ningún lado**: fue un comando suelto, del que solo sobrevivió la base `CACC_HU056_IT_20261029_1340_Prueba`, y no se puede
repetir. Se reemplazó por `qa/HU-056/e2e_hu056_categoria.ps1`, que reusa el entorno de HU-059:

- recrea la base al empezar (`reset_base.ps1` + `datos/04_fixtures_hu056.sql`, DNI 9856xxxx), así que **no hay una limpieza final que pueda fallar**;
- los procesos se detienen en el `finally`.

| Caso | Qué verifica | Resultado |
|---|---|---|
| PUT-1 | El Deportivo cambia la cat. 3 → 4: 200 `cambiado`; cobro en 4, la pertenencia conserva 3 y suma 4; UPDATE auditado con origen `PortalDeportivo` y el operador | PASS |
| PUT-2 | El historial financiero (cuota pagada + pendiente) queda idéntico | PASS |
| PUT-3 | Repetir el pedido: 200 `cambiado=false`, sin escrituras nuevas | PASS |
| PUT-4 | Categoría inexistente: 422, rechazo `CambioCategoria|UNPROCESSABLE|PortalDeportivo` registrado | PASS |
| PUT-5 | Categoría 0: 400 en el Deportivo y 400 `INVALID_INPUT field=idCategoria` en el contrato | PASS |
| PUT-6 | Jugador inexistente: 404 `NOT_FOUND` en el contrato, **404 en el Deportivo** (no 503) | PASS |
| PUT-7 | Jugador dado de baja: 422, la categoría no cambia | PASS |
| PUT-8 | Sin credencial o con una inválida: 401, nada cambia | PASS |
| PUT-9 | Volver a la cat. 3: 200, pertenencia sin duplicados, historial intacto | PASS |
| ENV-1..4, LOG-1 | Entorno listo; los logs no contienen la credencial, la clave JWT ni tokens | PASS |

## 5. Pendiente para cerrar

1. **Code review humano** de la implementación (servicio, DAO, controller y contrato).
2. **Equipo Deportivo:** adoptar `ellos-alta-por-integracion`, que hoy solo aplica sobre `afa4e60` (BUG-059-2).
3. **Club:** qué hacer con los DNI de 9 dígitos que trae la base B (HU-053).
