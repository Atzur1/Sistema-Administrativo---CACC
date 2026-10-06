# QA — HU-024: Estado de cuenta y cuotas del alumno

Pruebas de HU-024 en la rama `feature/HU-024-estado-de-cuenta-alumno`, creada desde `main`
(`ea54da8`, HU-023). Se ejecutaron junto con Claude Code el 2026-10-05:
- pruebas unitarias (xUnit y Vitest);
- pruebas HTTP contra la API real, sobre una copia descartable de la base con un escenario
  armado a propósito.

La prueba visual en navegador queda pendiente (sección 6).

---

## 1. Análisis previo: qué existía y qué faltaba

Casi toda la HU ya estaba resuelta por la ficha `jugadores/:id/deuda` (HU-025/026), a la que
lleva la búsqueda de HU-023.

| Requerimiento | Encontrado | Acción en HU-024 |
|---|---|---|
| Estado de cada cuota (Pagado / Pendiente / Vencido) | `PagosService.ConstruirEstadoDeCuotas` (HU-025): agrupa las filas de `PAGOS` por período y toma el monto congelado (`monto_base`). Aplica beneficios y abonos parciales. Vencido = pasó el último día del mes que cubre la cuota. | Se reutiliza tal cual, sin duplicar SQL ni reglas. |
| Endpoint de estado de cuenta | No había. La ficha armaba la vista con 3 llamadas: `/jugadores/{id}`, `/pagos/deuda/{id}` y `/pagos/cuotas/{id}`. | `GET /api/pagos/jugador/{id}/estado-de-cuenta` → `PlayerStatementDto`. La ficha toma de ahí el total y la tabla. |
| Total a abonar | "Total adeudado" se calculaba en el navegador sumando `/pagos/deuda`, que **incluye la inscripción**. | Lo calcula el backend (`TotalDebtAmount`) con **solo cuotas mensuales**. |
| Tarjeta de resumen | "Total adeudado" y "$ 0" al saldar todo. | "Total a abonar". Con deuda $0 muestra "Sin deuda" en verde. |
| Chips por estado | Pagado verde y Vencido rojo. **Pendiente en gris.** | Pendiente en ámbar (`.estado-pendiente`). |
| Carga y errores | "Cargando cuotas..." y un error en la sección, sin toast. | Se suma un toast en español: conexión, permisos o servidor. |
| Selección desde HU-023 | "Ver cuotas" ya abre esta ficha. | Sin cambios. |

### Decisiones

- **Ruta `api/pagos/jugador/{id}/estado-de-cuenta` en `PagosController`**, según la convención
  del repo y la decisión del usuario. La HU sugería `/api/payments/...` en un `PaymentsController`,
  que habría dejado dos controladores de pagos en paralelo.
- **Roles `"1,2"`** (atributo de la clase `PagosController`), no `"Admin"`: el claim de rol del JWT es
  el id numérico, como en HU-022 y HU-023.
- **La inscripción queda fuera** (decisión del usuario): se abona por separado y es otro ingreso.
  El estado de cuenta solo muestra y suma `concepto = 'Cuota'`. La inscripción pendiente sigue
  visible en el panel "Deuda Pendiente" de la misma ficha.
- **`TotalDebtAmount`** es el saldo de las cuotas Pendientes y Vencidas, con beneficios y abonos
  parciales ya aplicados. Es el mismo valor que la columna "A cobrar".
- **`DueDate`** es el último día del mes que cubre la cuota. Coincide con el criterio de Vencido
  y con la columna "Vence" de la ficha.
- **La tabla de cobro de HU-025 no cambió.** Un adaptador (`feeToCuotaJugador`) convierte cada
  `FeeItem` a la fila que la tabla ya usaba, así la selección, la confirmación y la relectura de
  HU-025/026 siguen iguales.
- **La tabla "TARIFAS" de la HU no existe en el modelo:** los montos salen de `PAGOS.monto_base`,
  congelado al emitirse la cuota. `ARANCELES` solo interviene al emitirla.

## 2. Cambios

**Backend**
- `EntityLibrary/PlayerStatement.cs` (nuevo): `PlayerStatementAccount` (lo que lee el DAO) y
  `PlayerStatement` (el estado de cuenta).
- `DaoLibrary/PagosDao.cs`:
  - `GetPlayerStatementAccount(playerId)`: lee los datos del jugador y las filas de sus cuotas con
    una sola conexión, con `SqlCommand` parametrizado. Devuelve null si el jugador no existe.
  - La lectura de filas se extrajo a `LeerMovimientosCuotas`, que comparte con
    `ObtenerMovimientosCuotas` (HU-025).
- `ServiceLibrary/PagosService.cs`: `GetPlayerStatement` y `BuildPlayerStatement` (estático,
  testeable).
- `ApiGestion/Models/PlayerStatementDto.cs` (nuevo): `PlayerStatementDto` y `FeeItemDto`.
- `PagosController.cs`: `GET jugador/{idJugador:int}/estado-de-cuenta`. Responde 404 si el jugador
  no existe y 500 sin detalles internos si falla la base.
- Interfaces `IPagosDao` e `IPagosService`, y sus 4 fakes de test.

**Frontend**
- `services/pagos.ts`: interfaces `PlayerStatement` y `FeeItem`, `getEstadoDeCuenta()` y
  `feeToCuotaJugador()`.
- `deuda-jugador.ts`:
  - La carga inicial y la relectura después de un cobro piden el estado de cuenta (antes pedían
    `/pagos/cuotas`).
  - El total sale de `totalDebtAmount`.
  - `cuotasAdeudadas` cuenta las cuotas con saldo.
  - Los errores de carga avisan con un toast.
- `deuda-jugador.html/.css`: tarjeta "Total a abonar" / "Sin deuda", contador "Cuotas adeudadas" y
  chip `.estado-pendiente` en ámbar.
- `getCuotas()` y `GET /pagos/cuotas/{id}` se mantienen: tienen su test y los citan QA anteriores.

**Postman:** colección nueva `HU-024 - Estado de Cuenta del Alumno` (8 requests).

## 3. Pruebas unitarias

| Comando | Resultado |
|---|---|
| `dotnet test ApiGestion.Tests` | **235 pasan, 11 omitidas** (integración sin `CACC_TEST_DB`), 0 fallas. 15 tests nuevos. |
| `npx ng test --watch=false` | **213/213**: 12 nuevos y los 24 de HU-025/026 adaptados al nuevo origen de datos, todos en verde. |

**Backend: `PagosService.Statement.Tests.cs` (7)**

| # | Caso | Estado |
|---|---|---|
| S1 | El total suma Pendiente y Vencido, no Pagado (70k pagada + 85k vencida + 85k pendiente → 170k) | ✅ |
| S2 | El total usa el saldo tras el beneficio: 50% → 42.500; becado 100% → 0, marcada como cubierta | ✅ |
| S3 | Jugador al día → total 0 y todas las cuotas Pagado | ✅ |
| S4 | Jugador sin cuotas → lista vacía y total 0 | ✅ |
| S5 | Orden cronológico aunque las filas lleguen desordenadas | ✅ |
| S6 | Regla §2.3: cada cuota conserva su monto congelado (70k y 85k) | ✅ |
| S7 | El encabezado sale de la cuenta del jugador | ✅ |

**Backend: `PagosController.Tests.cs` (8)**

| # | Caso | Estado |
|---|---|---|
| C1 | Mapea encabezado ("BAZÁN, NICOLÁS"), total y cada cuota. Una cuota pagada tiene `AmountDue` 0, `PaidAt` y `PaymentMethod` | ✅ |
| C2 | `DueDate` = último día del mes: feb-2026 → 28, feb-2028 → 29, marzo → 31, diciembre → 31 (4 casos) | ✅ |
| C3 | Jugador inexistente → 404 "Jugador no encontrado." | ✅ |
| C4 | Error de base → 500 sin el detalle interno | ✅ |
| C5 | `[HttpGet("jugador/{idJugador:int}/estado-de-cuenta")]`, sin `[AllowAnonymous]` (la clase exige roles 1,2, test de HU-022) | ✅ |

**Frontend: `deuda-jugador.spec.ts`, bloque HU-024 (10)**

| # | Caso | Estado |
|---|---|---|
| F1 | La tabla y el total salen del estado de cuenta (una llamada); ya no pide `getCuotas` | ✅ |
| F2 | "Total a abonar" con el total del backend ($170.000) | ✅ |
| F3 | Una inscripción pendiente en `/pagos/deuda` no suma al total | ✅ |
| F4 | Sin deuda: "$ 0", "Sin deuda" y la clase verde `profile-stat-ok` | ✅ |
| F5 | "2 Cuotas adeudadas": no cuenta las pagadas ni las cubiertas por beneficio | ✅ |
| F6 | Chips: Pagado `.estado-pagado`, Vencido `.estado-vencida`, Pendiente `.estado-pendiente` (ámbar) | ✅ |
| F7 | Orden cronológico tal como lo devuelve el backend | ✅ |
| F8 | "Cargando cuotas..." mientras no llega la respuesta, y la tarjeta todavía no se muestra | ✅ |
| F9 | Sin conexión → toast "No se pudo conectar con el servidor…" y el mismo texto en la sección; la tarjeta no se muestra | ✅ |
| F10 | Error 500 → toast "No se pudo cargar el estado de cuenta de este jugador." | ✅ |

**Frontend: `pagos.cobro.spec.ts` (2):** `GET /api/pagos/jugador/12/estado-de-cuenta` y el adaptador
`feeToCuotaJugador` campo por campo. ✅

**Regresión HU-025/026:** sus 24 tests ahora reciben los datos de `getEstadoDeCuenta`, con el total
calculado como lo hace el backend. Siguen cubriendo selección, cobro, toast, relectura con fallo y
$0. Todos en verde.

## 4. Pruebas HTTP contra la API real

### Entorno

La base de desarrollo `ClubCamionerosPruebaCuatro` no tiene filas en `PAGOS`. Por eso, como en
QA-HU-025, se trabajó sobre una **copia descartable**. La original no se modificó.

```sql
BACKUP DATABASE ClubCamionerosPruebaCuatro TO DISK = N'...\Backup\qa_hu024.bak' WITH COPY_ONLY, CHECKSUM, INIT;
RESTORE DATABASE ClubCamionerosPruebaCuatro_HU024 FROM DISK = N'...\Backup\qa_hu024.bak' WITH MOVE ..., CHECKSUM;
```

Fixture solo en la copia. Jugador 3, con fecha de prueba 05/10/2026:

| Fila de `PAGOS` | Concepto | Monto | Situación | Esperado |
|---|---|---|---|---|
| 9001 | Cuota enero | $70.000 | Pagada en efectivo el 10/01 | Pagado, no suma |
| 9002 | Cuota julio | $85.000 | Pendiente | Vencido, suma $85.000 |
| 9003 | Cuota septiembre | $85.000 | Pendiente, beneficio "Descuento" 50% en septiembre | Vencido, suma $42.500 |
| 9004 | Cuota octubre | $92.000 (arancel nuevo) | Pendiente | Pendiente, suma $92.000 |
| 9005 | Inscripción | $50.000 | Pendiente | **Fuera** del estado de cuenta |

**Total esperado: $219.500.** Además:
- Jugador 2: una cuota pagada, sin deuda.
- Jugador 5: sin cuotas emitidas.

La API se levantó con `dotnet run` contra la copia, sin migraciones y con una clave JWT temporal.
Los tokens se firmaron con los claims de `AuthController` para el usuario real 1 (rol 1).

### Resultados

| # | Caso | Obtenido | Estado |
|---|---|---|---|
| E1 | Sin token | 401 | ✅ |
| E2 | Token inválido | 401 | ✅ |
| E3 | Token con un rol que el usuario no tiene | 401 (lo rechaza `UsuarioActivoConRol`) | ✅ |
| E4 | Jugador 3 | 200 en 7 ms | ✅ |
| E5 | Encabezado | "PRUEBA, MATIAS", DNI 99000003 | ✅ |
| E6 | Solo cuotas, en orden cronológico | Enero, julio, septiembre y octubre de 2026; sin la inscripción | ✅ |
| E7 | Estados | Pagado, Vencido, Vencido, Pendiente | ✅ |
| E8 | Montos congelados | 70.000 / 85.000 / 85.000 / 92.000 | ✅ |
| E9 | A cobrar | 0 / 85.000 / 42.500 / 92.000 | ✅ |
| E10 | Total a abonar | **219.500** (sin los 50.000 de la inscripción) | ✅ |
| E11 | Vencimientos | 2026-01-31, 2026-07-31, 2026-09-30, 2026-10-31 | ✅ |
| E12 | Cuota pagada | `paidAt` 2026-01-10, `paymentMethod` Efectivo | ✅ |
| E13 | Beneficio | `benefitReason` "Descuento", no cubierta (sigue debiendo el 50%) | ✅ |
| E14 | Tiempo de respuesta | 7 ms (el límite era 2 s) | ✅ |
| E15 | Jugador 2, al día | 200, total 0, una cuota Pagado | ✅ |
| E16 | Jugador 5, sin cuotas emitidas | 200, `fees: []`, total 0 | ✅ |
| E17 | Jugador 999 | 404 "Jugador no encontrado." | ✅ |
| E18 | Id no numérico (`/jugador/abc/...`) | 404 (restricción `:int` de la ruta) | ✅ |
| E19 | Coherencia con `GET /pagos/cuotas/3` (HU-025) | Mismo período, estado y saldo en cada cuota | ✅ |

**Resultado: 19/19.**

**Rendimiento:** la consulta filtra por jugador (`FK_id_jugador`), así que su costo depende de las
cuotas de ese jugador (unas 12 por año), no del tamaño del padrón. No se pudo medir en
`ClubCamionerosPRUEBA` (577 jugadores): tiene un esquema anterior, sin `PAGOS.concepto` ni
`JUGADORES_DESCUENTOS.tipo_valor`, así que el código actual no puede consultarla.

**403 no probado por HTTP:** la base solo tiene un usuario, con rol 1 (igual que en QA-HU-023).
Queda como caso manual M8.

## 5. Copia de QA

La copia `ClubCamionerosPruebaCuatro_HU024`, con el fixture de la sección 4, **se dejó creada**
para la prueba visual. Para borrarla al terminar:

```sql
DROP DATABASE ClubCamionerosPruebaCuatro_HU024;
-- y borrar ...\MSSQL\Backup\qa_hu024.bak
```

## 6. Pruebas manuales (pendientes)

API y frontend levantados, con login real. Para ver los estados, usar una base con cuotas
emitidas, por ejemplo la copia de la sección 5.

| # | Caso | Pasos | Resultado esperado | Estado |
|---|---|---|---|---|
| M1 | Llegar desde la búsqueda | Cuotas y Pagos → "Buscar alumno" → "Ver cuotas" | Se abre la ficha del alumno | ⏳ |
| M2 | Tarjeta de resumen | Alumno con deuda | "Total a abonar" en rojo con el total, y "N Cuotas adeudadas" | ⏳ |
| M3 | Sin deuda | Alumno al día | "$ 0" en verde y "Sin deuda" | ⏳ |
| M4 | Colores | Alumno con cuotas en los tres estados | Pagado verde, Pendiente ámbar, Vencido rojo | ⏳ |
| M5 | Inscripción aparte | Alumno con inscripción pendiente | No aparece en la tabla "Cuotas" ni suma al total; sí aparece en "Deuda Pendiente" | ⏳ |
| M6 | Carga | DevTools → "Slow 3G" → abrir la ficha | "Cargando cuotas..." y después la tarjeta y la tabla | ⏳ |
| M7 | Sin conexión | Detener la API → abrir la ficha | Toast rojo "No se pudo conectar con el servidor…" y el mismo aviso en la sección | ⏳ |
| M8 | 403 | Token de un usuario real con un rol fuera de 1/2 | `GET /api/pagos/jugador/{id}/estado-de-cuenta` → 403 | ⏳ |
| M9 | Regresión de HU-025/026 | Cobrar 1 y después 2 cuotas desde la ficha | Toast de éxito, filas en verde "Pagado", total actualizado sin recargar | ⏳ |
| M10 | Postman | Correr la colección HU-024 en orden | 8/8 requests en verde | ⏳ |

## 7. Criterios de aceptación

| Criterio | Estado |
|---|---|
| Método DAO con `SqlCommand` parametrizado que lee de `PAGOS` | ✅ `GetPlayerStatementAccount` (E4–E19) |
| Historial de cuotas en orden cronológico | ✅ S5, F7, E6 |
| Estado Pagado / Pendiente / Vencido según el vencimiento | ✅ tests de HU-025, E7, E11 |
| Monto congelado de cada cuota (regla §2.3) | ✅ S6, E8 |
| Endpoint de estado de cuenta protegido por rol | ✅ C5, E1–E3; ⏳ M8 (403) |
| DTO `PlayerStatementDto` / `FeeItemDto` con los campos pedidos | ✅ C1, E4 |
| `TotalDebtAmount` = suma de Pendientes y Vencidas | ✅ S1, S2, E10 |
| Al seleccionar un alumno en HU-023 se muestra su estado de cuenta | ✅ (HU-023); ⏳ M1 |
| Tarjeta "Total a abonar", y $0 / "Sin deuda" | ✅ F2, F4; ⏳ M2, M3 |
| Columnas Período, Monto, Vencimiento y Estado | ✅ (tabla existente); ⏳ M4 |
| Chips verde / ámbar / rojo | ✅ F6; ⏳ M4 |
| Indicador de carga y aviso en español ante fallos de conexión | ✅ F8–F10; ⏳ M6, M7 |
| No se rompe HU-025/026 | ✅ sus 24 tests en verde; ⏳ M9 |

## 8. Observaciones

- **Cuotas antiguas sin `fecha_vencimiento`:** no aparecen en el estado de cuenta, igual que en la
  tabla de cobro de HU-025. Sí figuran en el Historial de Pagos y en `/pagos/deuda`. Si existieran,
  el request de Postman "Solo cuotas mensuales" mostraría la diferencia.
- **"Deuda Pendiente"**, el panel de abonos parciales de la misma ficha, sigue leyendo
  `/pagos/deuda`, que incluye la inscripción. Así, la inscripción se sigue pudiendo cobrar desde la
  ficha.
- **`ClubCamionerosPRUEBA` está desactualizada** respecto de las migraciones del repo. Conviene
  actualizarla o reemplazarla si se la va a seguir usando para QA con los 577 jugadores.
