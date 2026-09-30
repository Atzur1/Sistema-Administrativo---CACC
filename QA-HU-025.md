# QA — HU-025: Registro y confirmación de pago de cuota

Evidencia de las pruebas de HU-025 en la rama `feature/HU-025-register-payments`, ejecutadas
junto con Claude Code el 2026-09-30. Las pruebas unitarias corrieron sobre el código; las de
integración, contra la API real levantada sobre una **copia** de la base de desarrollo
(`ClubCamionerosPRUEBA_HU025`, restaurada desde un respaldo `COPY_ONLY` de
`ClubCamionerosPRUEBA`). La base de desarrollo no se modificó.

---

## 1. Qué había antes de esta HU

| Componente | Estado encontrado |
|---|---|
| `POST /api/pagos/cobro` | Ya existía: cobro en lote dentro de una transacción, con lectura `UPDLOCK, HOLDLOCK` y `UPDATE ... WHERE estado = 0`. **Ningún frontend lo consumía.** No validaba que las cuotas pertenecieran al jugador, aceptaba inscripciones y deduplicaba en silencio los IDs repetidos. |
| `POST /api/pagos/registrar` | Pago de **una** cuota con monto editable (admite parciales). Lo usan la ficha `deuda-jugador` ("Pagar saldo") y Cuotas y Pagos. |
| Ficha financiera (`deuda-jugador`) | Mostraba solo las cuotas pendientes. No tenía selección múltiple ni modal de confirmación, y las cuotas pagadas desaparecían. |
| `PAGOS` | Tenía `fecha_hora_registro DATETIME2`. **No tenía `FK_id_usuario_registro`.** El operador solo quedaba en `AUDITORIA_CAMBIOS` (trigger). |
| Métodos de pago | `MetodosPago.Validos = { Transferencia, Efectivo }`. Débito no existe en el código, así que no hay contradicción. |
| HU-009 / BE-4 | **No integrada** (`origin/feature/HU-009-generacion-cuotas-mensuales` tiene 4 commits fuera de esta rama). Las cuotas se generan solo al programar un arancel. |

## 2. Cambios realizados

- Migración `database/migrations/V20260930_02__agrega_usuario_registro_pagos.sql`: agrega
  `PAGOS.FK_id_usuario_registro INT NULL` con FK a `USUARIO(PK_id_usuario)`. Las filas
  históricas quedan en NULL.
- `POST /api/pagos/cobro` extendido. Nuevas reglas:
  - `idJugador` obligatorio;
  - se verifica que el jugador exista y que cada cuota le pertenezca;
  - solo se cobran cuotas de concepto `Cuota`;
  - los IDs repetidos se rechazan;
  - el método se normaliza a su valor canónico;
  - el operador se toma del claim `idUsuario` del JWT (401 si falta).
- `PagosDao.MarcarPagosComoAbonados`: el UPDATE registra `fecha_pago`, `metodo_pago`,
  `fecha_hora_registro` (un único `GETDATE()` para todo el lote) y `FK_id_usuario_registro`.
  Repite en el WHERE jugador, concepto y `estado = 0`, y si la cantidad de filas actualizadas no
  coincide con la esperada, lanza una excepción que revierte todo.
- Nuevo `GET /api/pagos/cuotas/{idJugador}`: cuotas del jugador agrupadas por período con estado
  `Pendiente`, `Vencido` o `Pagado`.
- `/registrar` también registra `FK_id_usuario_registro`.
- Frontend (`deuda-jugador`): nueva tabla "Cuotas" con checkboxes, total recalculado, selector
  Efectivo/Transferencia, botón "Registrar Pago" y modal de confirmación. Después de un cobro
  exitoso se vuelven a leer cuotas y deuda desde la API. "Pagar saldo" (parcial) sigue disponible.

## 3. Pruebas unitarias

| Suite | Comando | Resultado |
|---|---|---|
| Backend (xUnit) | `dotnet test ApiGestion.Tests` | **170/170** correctas (incluye 24 casos nuevos en `PagosService.Cobro.Tests.cs` y 6 nuevas en `PagosController.Tests.cs`) |
| Frontend, specs nuevos | `npx ng test --watch=false --include src/app/services/pagos.cobro.spec.ts --include src/app/admin-portal/deuda-jugador/deuda-jugador.spec.ts` | **15/15** correctas |
| Frontend, suite completa | `npx ng test --watch=false` | **129/130**. El fallo es **preexistente y ajeno** a esta HU (ver sección 7, bug 1). |

Se trabajó con TDD: los tests se escribieron primero y se confirmó que fallaban antes de
implementar (el backend no compilaba y el frontend reportaba `getCuotas` inexistente).

## 4. Pruebas de integración (API real + SQL Server)

### Entorno

```powershell
# Copia descartable de la base (la original solo se lee)
BACKUP DATABASE ClubCamionerosPRUEBA TO DISK = N'...\prueba_hu025.bak' WITH COPY_ONLY, CHECKSUM, INIT;
RESTORE DATABASE ClubCamionerosPRUEBA_HU025 FROM DISK = N'...\prueba_hu025.bak' WITH MOVE ..., CHECKSUM;

# API contra la copia, con clave JWT temporal solo para esta instancia
$env:ASPNETCORE_ENVIRONMENT="Development"; $env:ASPNETCORE_URLS="http://localhost:5199"
$env:Jwt__Key="<clave temporal>"
$env:ConnectionStrings__ConexionSQL="Server=(localdb)\MSSQLLocalDB;Database=ClubCamionerosPRUEBA_HU025;Trusted_Connection=True;TrustServerCertificate=True"
dotnet run --project ApiGestion --no-launch-profile
```

Al iniciar, la API aplicó `V20260930_01__verificar_base_equipo.sql` y
`V20260930_02__agrega_usuario_registro_pagos.sql` con el ejecutor oficial. Se verificó en la
copia que existen la columna `FK_id_usuario_registro` y la FK `FK_PAGOS_USUARIO_REGISTRO`.

**Autenticación:** no se conocía la contraseña del administrador. Los tokens se firmaron con la
clave temporal de esa instancia, con los mismos claims que `AuthController` y para usuarios reales
de la copia. Así se ejercita todo el pipeline de validación (firma, issuer/audience, expiración y
`UsuarioActivoConRol` contra la base), **excepto el endpoint de login**.

**Fixture en la copia (no en la base de desarrollo):**

1. Los 577 jugadores tienen `PERSONA.genero = NULL`, por lo que programar un arancel no genera
   ninguna cuota (ver sección 8). Se asignó `genero = 'Masculino'` a los jugadores 9, 10, 11 y 12,
   elegidos por no tener beneficios.
2. Las cuotas se generaron con el flujo real `POST /api/aranceles/programar`: $70.000 desde
   2026-01-01, y $85.000 desde 2026-03-01, 2026-04-01 y 2026-09-01. Los importes son **valores de
   prueba**, no tarifas reales.
3. Para la prueba de rollback se insertaron dos beneficios al jugador 12: 50% en marzo y 100% en
   abril.

El script de integración (Python + `sqlcmd`) está fuera del repositorio. Cada caso compara la
respuesta HTTP con el estado de `PAGOS` y `AUDITORIA_CAMBIOS` antes y después.

### Resultados (corrida 2)

| # | Caso | Resultado esperado | Resultado obtenido | Estado |
|---|---|---|---|---|
| 1 | `GET /pagos/cuotas/9` antes de emitir | 200, `[]` | 200 `[]` | ✅ |
| 2 | Estados al 30/09 | Enero, marzo y abril Vencido; septiembre Pendiente | Vencido, Vencido, Vencido, Pendiente | ✅ |
| 3 | Monto histórico | Enero $70.000 con arancel vigente de $85.000 | `montoCuota` 70000, `saldoPendiente` 70000 | ✅ |
| 4 | Sin token | 401 | 401 | ✅ |
| 5 | Usuario sin acceso al portal (usuario 2, `acceso_portal = 0`) | 401 y no cobra | 401, enero sigue con `estado = 0` | ✅ |
| 6 | Método `Débito` | 400 | 400 "Método de pago inválido..." | ✅ |
| 7 | IDs repetidos | 400 | 400 "La solicitud tiene cuotas repetidas" | ✅ |
| 8 | Cuota de otro jugador en el lote | 400 y ninguna cambia | 400 "no pertenecen al jugador"; ambas siguen con `estado = 0` | ✅ |
| 9 | Cuota inexistente | 400 | 400 "No existen las siguientes cuotas: 999999" | ✅ |
| 10 | Jugador inexistente | 400 | 400 "El jugador #999999 no existe" | ✅ |
| 11 | Lista vacía | 400 | 400 | ✅ |
| 12 | Cobro de enero con `"efectivo"` | 200 por $70.000, método "Efectivo" | 200, `montoTotal` 70000, `metodoPago` "Efectivo" | ✅ |
| 13 | Persistencia y auditoría en PAGOS | `estado = 1`, método, `fecha_pago` = hoy, `fecha_hora_registro`, `FK_id_usuario_registro = 1` | `1\|70000\|70000\|Efectivo\|2026-09-30\|<datetime2>\|1` | ✅ |
| 14 | Doble cobro de enero | 400 y el pago anterior no se modifica | 400 "ya fueron abonadas"; fila idéntica antes y después | ✅ |
| 15 | Lote marzo + enero (enero ya pagado) | 400, todo o nada | 400; marzo sigue con `estado = 0` | ✅ |
| 16 | Cobro múltiple marzo + abril | 200, total $170.000, detalle por cuota | 200, `montoTotal` 170000, cuotas ["Marzo 2026", "Abril 2026"] | ✅ |
| 17 | Registro del lote | Misma `fecha_hora_registro` y `FK_id_usuario_registro = 1` en ambas | Iguales en las dos filas | ✅ |
| 18 | `AUDITORIA_CAMBIOS` | 2 UPDATE con `id_usuario = 1` | 2 eventos, `id_usuario = 1` | ✅ |
| 19 | `GET /pagos/cuotas/9` después del cobro | Enero, marzo y abril Pagado; septiembre Pendiente | Coincide | ✅ |
| 20 | Deuda real del jugador | Solo septiembre ($85.000) | `GET /pagos/deuda/9` suma 85000 | ✅ |
| 21 | **Rollback:** lote marzo (50%) + abril (100%) | 400 al llegar a abril | 400 "La cuota de Abril 2026 está cubierta por un beneficio" | ✅ |
| 22 | **Rollback:** estado final | Marzo sin el ajuste de `monto_final` ni el descuento que ya se habían escrito dentro de la transacción; sin eventos de auditoría | Filas idénticas antes y después; eventos de auditoría sin cambios | ✅ |
| 23 | Marzo solo, con 50% | 200 por $42.500 | 200, `montoTotal` 42500 | ✅ |
| 24 | **Concurrencia, pantalla vieja:** otra sesión SQL marca la cuota pagada y demora el COMMIT 6 s; la API intenta cobrarla | La API espera el lock, rechaza y no pisa el pago | 400 "ya fueron abonadas" después de más de 3 s de espera; la fila conserva método "Efectivo" y `FK_id_usuario_registro` NULL de la otra sesión | ✅ |
| 25 | **Concurrencia:** dos cobros simultáneos de la misma cuota | Exactamente un 200 y un 400 | `[200, 400]` | ✅ |
| 26-31 | Lecturas existentes: `resumen`, `pendientes`, `recientes?top=5`, `resumen-hoy`, `player-accounts`, `deuda/9` | 200 | 200 | ✅ |

**Resultado: 31/31 casos verificados.**

### Corrida 1 (descartada), por transparencia

La primera corrida dio **18/30**. Las causas fueron del armado de la prueba, no del código:

- Los jugadores 1 a 4 elegidos al principio **ya tenían beneficios reales** (el jugador 1 es
  Becado 100%). El sistema rechazó correctamente cobrar cuotas cubiertas.
- El usuario 2 devolvió 401 porque tiene `acceso_portal = 0`. Es el comportamiento esperado.
- La sesión SQL concurrente falló por falta de `QUOTED_IDENTIFIER` en `sqlcmd` (el índice filtrado
  lo exige). Se corrigió agregando `-I`.
- `GET /pagos/recientes?top=5` devolvió 500: es un bug preexistente (sección 7, bug 2).

Se recreó la copia desde el respaldo y se repitió todo con jugadores sin beneficios.

## 5. Pruebas de UI

| Tipo | Estado |
|---|---|
| Specs de componente (Vitest + TestBed, servicios reemplazados por dobles): listado, selección, bloqueo de pagadas y cubiertas, total, botón deshabilitado, modal, Cancelar, Confirmar con relectura desde la API, errores 400/0/500 sin éxito ni cambio visual | ✅ 12/12 |
| **Prueba manual en navegador** (Angular + API real) | ⏳ **Pendiente.** No se ejecutó en esta sesión. |

Pasos sugeridos para la prueba manual: levantar la API contra la copia (o una base con cuotas
reales), ejecutar `npm start` y hacer el recorrido Cuotas y Pagos → jugador deudor → tabla
"Cuotas". Seleccionar 2 cuotas, elegir método, Registrar Pago, verificar el modal, Confirmar, y
comprobar el toast, las filas en verde "Pagado" y el nuevo "Total adeudado".

## 6. Criterios de aceptación

| Criterio | Estado |
|---|---|
| Seleccionar una o varias cuotas Pendientes/Vencidas | ✅ Unit + API |
| Las cuotas Pagadas no se pueden seleccionar | ✅ Unit (UI) + API rechaza (#14) |
| Método de pago obligatorio (Efectivo/Transferencia) | ✅ Unit + API (#6) |
| Confirmación antes de cobrar, con cuotas, montos, método y total | ✅ Unit |
| El monto sale de la cuota histórica | ✅ API (#3, #12) |
| Persistencia real en SQL Server | ✅ API (#13) |
| Método, fecha de pago, fecha/hora de registro y administrador | ✅ API (#13, #17, #18) |
| Las cuotas pasan a Pagado y la UI se actualiza con datos reales | ✅ API (#19) + Unit (relectura). Falta la prueba manual en navegador. |
| Deuda recalculada | ✅ API (#20) |
| Protección backend contra doble cobro | ✅ API (#14, #24, #25) |
| Operación atómica y rollback completo | ✅ API (#15, #21, #22) |
| Concurrencia | ✅ API (#24, #25) |
| Errores mostrados correctamente | ✅ Unit (400 / conexión / 500) |
| Sin mocks en el flujo final | ✅ La ficha consume `GET /pagos/cuotas` y `POST /pagos/cobro` |
| No se agregó ORM | ✅ ADO.NET + SQL parametrizado (`ConstruirClausulaIn`) |
| No se rompieron lecturas existentes | ✅ en las rutas probadas. Ver sección 7 para `recientes` y `registrados`. |

## 7. Bugs encontrados (preexistentes, fuera de alcance, no corregidos)

1. **Spec de Cuotas y Pagos desactualizado.** `cuotas-pagos.spec.ts` ("shows the combined amount
   and debtor count...") espera el texto "Deuda Global Total:", pero el commit `b4ee421` cambió la
   etiqueta a "Deuda Global:". El componente no se tocó en esta HU.
2. **`GET /api/pagos/recientes` y `GET /api/pagos/registrados` devuelven 500** cuando el listado
   incluye pagos históricos con `fecha_vencimiento` NULL: `PagosDao.ObtenerPagosAgrupados` hace
   `Convert.ToDateTime(reader["fecha_vencimiento"])` sin chequear NULL. En `ClubCamionerosPRUEBA`
   los 1258 pagos existentes tienen `fecha_vencimiento` NULL, así que ahí fallan siempre. Se
   reprodujo en la copia (`recientes?top=10` y `registrados` → 500); con `top=5` funciona solo
   porque los 5 más recientes son cobros nuevos. Este método no se modificó.

## 8. Pendientes y dependencias bloqueantes

- **Emisión de cuotas:** HU-009 no está integrada. Además, en `ClubCamionerosPRUEBA` todos los
  jugadores tienen `PERSONA.genero = NULL`, por lo que ni siquiera programar un arancel genera
  cuotas. **En la base de desarrollo no hay ninguna cuota Pendiente/Vencida sobre la cual probar
  HU-025 de punta a punta.** Las pruebas de este documento usaron una copia con un fixture
  explícito.
- Prueba manual de UI en navegador (sección 5).
- Prueba del login real (los tokens de QA se firmaron con la clave temporal).
- Validar en una base compartida la migración `V20260930_02`, después de un respaldo.

## 9. Decisiones y observaciones

- **fecha_pago:** se asienta el día del registro (`DateTime.Now.Date`). No hay carga retroactiva
  en el código actual. `fecha_hora_registro` guarda el instante real del servidor SQL. Si el club
  necesita registrar pagos con fecha anterior, hay que definirlo como regla nueva.
- **Beneficios:** el cobro descuenta el beneficio activo del período, igual que la lectura de
  deuda. `monto_base` nunca se modifica; `monto_final` queda con lo efectivamente cobrado.
- **Autorización:** se mantuvo `Roles = "1,2"` en `PagosController`, igual que `/registrar`,
  por decisión del equipo.
- **Cobro parcial:** "Pagar saldo" (`/registrar`) convive con la selección múltiple.
- **Cuotas históricas sin período** (`fecha_vencimiento` NULL) no aparecen en la tabla de cuotas.
  Siguen visibles en el Historial de Pagos.
