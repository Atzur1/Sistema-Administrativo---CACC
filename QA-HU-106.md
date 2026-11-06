# QA HU-106: actualización de aranceles con resumen de impacto antes de confirmar

**Fecha:** 31/10/2026 · **Rama:** `feat/HU-106-impacto-aranceles` (desde `main` 342bd86, sin commitear) · **Base:** `ClubCamionerosUnificadaV2_HU106_Prueba` (LocalDB, descartable)

**Resultado: PASS.**

| Prueba | Resultado |
|---|---|
| QA contra la API real (3.004 jugadores, 29.405 cuotas) | **26/26 PASS** |
| QA en navegador | **11/11 PASS**, después de corregir 2 defectos propios que encontró esta misma prueba (§8) |
| Backend | **682** en verde · 0 fallos · 11 omitidos, con la integración contra SQL real |
| Frontend | **504/504** |
| Build | **No ejecutado**, por regla del equipo (ver §9) |

---

## 1. Arquitectura encontrada (antes de HU-106)

| Pieza | Estado real |
|---|---|
| Pantalla | `actualizacion-aranceles`: un formulario por destino (género **o** categoría), con tres modos de HU-069 (monto absoluto, porcentaje y monto fijo a sumar) y fecha de vigencia. **"Programar actualización" grababa directamente, sin ninguna confirmación.** |
| Cálculo de HU-069 | **Solo en el frontend** (`tariff-increase.ts`, `calculateNewTariff`). La API recibía el monto final ya calculado. |
| Modelo | Cambiar un arancel **no es un UPDATE**: se programa una fila nueva en `ARANCELES` (destino, monto, `vigente_desde`). Hay una sola por mes y destino, y las anteriores quedan como historial. |
| Jerarquía HU-048 | Una sola fuente en SQL: `SqlCuotaMensual.ArancelDelJugador` (primero el arancel de la categoría, si no el del género, con el último `vigente_desde` hasta el fin del mes). Elegibilidad: `JugadorEnElClub`. Los usan el generador mensual, la reemisión y las cuotas faltantes. |
| Cuotas ya emitidas | Las de meses anteriores no se tocan nunca, porque no se aceptan fechas pasadas. En el **mes en curso** (HU-049), las cuotas pendientes **sin pagos** se quitan y se vuelven a emitir con el monto nuevo, y **las que tienen pagos se conservan**. |
| Transacción | `TariffTransactionDao` toma el bloqueo de escritura de cobros (`BloqueoEscrituraPagos`) y graba el arancel y las cuotas de forma atómica. |
| Auditoría | Trigger `TR_AUDIT_ARANCELES`: guarda el JSON del arancel (género, categoría, monto, vigencia) y el actor (`SESSION_CONTEXT`). **No sabía** el tipo de cambio, el valor anterior ni el alcance. |
| Permisos | `[Authorize(Roles = "1")]` en `programar` y `historial`: solo SuperAdmin. |

## 2. Dependencias

| Dependencia | Estado | Uso en HU-106 |
|---|---|---|
| HU-069 (porcentaje / monto fijo) | Disponible (cálculo solo en el front) | **El cálculo pasó al backend** con las mismas reglas: base = arancel vigente hoy del destino; porcentaje > 0 y ≤ 999,99 con 2 decimales, redondeado al peso con las mitades hacia arriba; monto fijo > 0, sumado a la base. |
| HU-048 (jerarquía) | Disponible | Se reutiliza `ArancelDelJugador` **tal cual**. |
| HU-010 (historial de importes) | Disponible | Las cuotas guardan `monto_base` al emitirse; nada lo recalcula. |
| HU-040 / HU-041 (auditoría) | Disponible | Se amplía el trigger existente (§6). |
| HU-049 (generación y reemisión) | Disponible | Comportamiento del mes en curso **sin cambios**, informado en el resumen. |
| Permisos de SuperAdmin | Disponible | `simular` y `programar` con `Roles = "1"`. |

## 3. Diseño implementado

**Fuente única del cálculo.** `ArancelesService.CalcularMontoNuevo` (reglas de HU-069 en `decimal`) y `CalcularImpacto` las usan **la simulación y la confirmación**. El front ya no calcula dinero: se borró `calculateNewTariff` y sus pruebas de redondeo pasaron al backend.

**Lecturas compartidas.** `ILecturaImpactoArancel` la implementa una sola clase de `TariffTransactionDao`:

- `ExecuteRead` → simular: conexión simple, sin transacción ni bloqueo, sin escrituras;
- `ExecuteInTransaction` → confirmar: las **mismas** lecturas, dentro de la transacción que después graba y con el bloqueo tomado.

**Consulta de impacto** (`ArancelesDao.ConsultarImpacto`):

- **Una sola consulta para todo el club**, agrupada en SQL. Nada de una consulta por jugador.
- Para cada jugador del alcance que está en el club ese mes, evalúa la jerarquía dos veces con `ArancelDelJugador`:
  - sobre `ARANCELES` → cuota actual;
  - sobre `ARANCELES UNION ALL <arancel nuevo>` → la cuota que tendría. **El arancel nuevo no se inserta, ni siquiera dentro de una transacción revertida.**
- Si gana el arancel nuevo, el jugador está afectado. Si gana el de su categoría (HU-048), queda en "excluidos".

**Endpoints** (convención `api/aranceles/…`):

| Endpoint | Rol | Body | Respuesta |
|---|---|---|---|
| `POST api/aranceles/simular` | 1 | `{ genero \| idCategoria, tipo: absoluto\|porcentaje\|montoFijo, valor, vigenteDesde }` | `TariffImpactResponseDTO` |
| `POST api/aranceles/programar` | 1 | el mismo + `{ huella, jugadoresConfirmados }` | 200 `{ exito, mensaje, impacto }` · **409** `{ mensaje, impacto actual }` · 400 |

**DTO del impacto:**

- tipo, valor y destino;
- `montoBase` (vigente hoy) y `montoNuevo`;
- `mesDeAplicacion` y `reemplazadoDesde`;
- `lineas[]` (categoría, género, monto actual → nuevo, jugadores, diferencia por jugador y total) y `excluidos[]`;
- `jugadores`, `categorias` y `diferenciaMensual`;
- cuotas del mes en curso: pendientes sin pagos y con pagos;
- `seEmiteElDia`;
- `requiereConfirmacionReforzada` (más de una categoría);
- `huella`.

**Datos que cambian entre simular y confirmar:**

- La simulación devuelve una **huella**: SHA-256 de todo lo que se muestra y se aprueba (montos, líneas, cuotas del mes, fechas).
- Al confirmar, el backend **recalcula dentro de la transacción, con el bloqueo tomado**. Si la huella cambió, responde **409** con el impacto nuevo y no graba nada.
- Es la solución mínima segura: no hay tablas de simulaciones ni estado guardado entre pedidos.

**Confirmación reforzada:**

- Si el cambio alcanza a más de una categoría, el usuario tiene que escribir "Escribí N para confirmar", con N = jugadores de la simulación.
- El backend **también** la valida (400 si no coincide), así que no depende del botón.
- Con una sola categoría alcanza con el botón.

**Invalidación:**

- Cualquier cambio en el formulario descarta el resumen.
- Lo que se confirma es el cambio **simulado** (una instantánea), no lo que diga el formulario después.
- Volver, Esc y × descartan sin grabar.

**Feedback:**

- El aviso de éxito aparece **después** de la respuesta del backend.
- Si falla, no hay éxito: el resumen queda abierto con el error para reintentar, y como la transacción revierte no queda nada a medias.

## 4. Fixtures (base descartable, reproducible con `qa/HU-106/preparar_base_hu106.ps1`)

| Qué | Detalle |
|---|---|
| Esquema y migraciones | Esquema unificado (HU-053) + migrador de la API: 20 de base + 5 aplicadas, incluida `V20261031_01`. |
| Cuentas y fixtures | De HU-059: 4 cuentas ficticias (SuperAdmin 20111222, Administrador 30000005, …); 5 personas, 4 jugadores y 5 cuotas. |
| Volumen (`carga_hu106.sql`) | **3.004 jugadores** (2.854 activos, 150 dados de baja en junio), 70 % varones, en las 13 categorías; **29.405 cuotas** de enero a octubre, emitidas con la misma jerarquía que el generador (hasta septiembre 90 % pagadas; octubre 40 % pagadas); una categoría sin jugadores. |
| Aranceles iniciales | Masculino $1.000 (01/01) y **$85.000 (01/02)**; Femenino **$60.000** (01/01); categorías *AFA 20067* y *Pt AFA 2008* con arancel propio de **$95.000** (01/03). |

Los datos son sintéticos (DNI 97xxxxxx).

## 5. Simulación esperada vs. obtenida (`qa/HU-106/salida_api.txt`, `evidencia_api.json`)

| ID | Caso | Esperado | Obtenido | Estado |
|---|---|---|---|---|
| SIM-1 | Masculino +10 % desde 01/12. Jugadores por categoría frente a **una consulta SQL independiente** (escrita sin `ArancelDelJugador`) | `3:154, 4:150, 5:151, 6:151, 7:151, 8:149, 9:149, 10:149, 11:150, 12:150, 13:150` | idéntico | PASS |
| SIM-2 | Monto (HU-069 en el backend) | $85.000 → $93.500 | $85.000 → $93.500 | PASS |
| SIM-3 | Totales | 1.654 jugadores · +$14.059.000 por mes | 1.654 · 14.059.000 | PASS |
| SIM-4 | HU-048: varones de categorías con arancel propio | 300 excluidos | AFA 20067: 150, Pt AFA 2008: 150 | PASS |
| SIM-5 | 11 categorías | confirmación reforzada | sí | PASS |
| SIM-6 | Categoría *Pt AFA 2011*, $99.000 desde hoy (mes en curso) | los dos géneros, con su monto actual | Femenino 60.000 → 99.000 ×69 · Masculino 85.000 → 99.000 ×151; 128 pendientes sin pagos (esperado 128), 92 con pagos | PASS |
| SIM-7 | Categoría sin jugadores | 0 jugadores, $0, sin confirmación reforzada | igual | PASS |
| SIM-0 | **10 simulaciones no escriben** | ARANCELES, PAGOS (y su contenido) y AUDITORIA_CAMBIOS iguales | `5\|29405\|35581\|-75478198` antes y después | PASS |

**Simulación contra resultado real (REAL-1):** se confirmó SIM-6. **Se reemitieron exactamente las 128 cuotas pendientes que mostró la simulación, todas a $99.000.** El mensaje del backend coincide: "Se volvieron a emitir 128 cuotas… 92 ya tenían pagos y se mantuvieron sin cambios". Para meses futuros, la prueba de integración `ArancelDeGenero_RespetaLaJerarquiaYCoincideConLasCuotasQueSeEmiten` graba el arancel, corre el **generador real** (`GenerarCuotasPendientesDelMes`) y verifica que cada cuota emitida es la de la simulación.

## 6. Confirmación, persistencia y auditoría

| ID | Caso | Obtenido | Estado |
|---|---|---|---|
| CONF-1 | Varias categorías con la cantidad mal escrita o sin ella | 400 "…escribí la cantidad de jugadores afectados (1654)", nada grabado | PASS |
| CONF-2 | Con la cantidad exacta | 200 en 27,6 ms; fila `93500.00\|Porcentaje\|10.00\|85000.00\|1654\|14059000.00` | PASS |
| STALE-1 | Entra una jugadora entre simular y confirmar (Femenino +5 %) | **409**: 762 → 763 jugadores, mensaje "El impacto del cambio es distinto…", nada grabado | PASS |
| STALE-2 | Revisado el impacto nuevo | 200 | PASS |
| AUD-1 | Evento de auditoría de la confirmación | `INSERT\|1\|superadmin.prueba@ejemplo.com\|Porcentaje\|10.00\|85000.00\|93500.00\|1654\|14059000.00\|11` (11 líneas en el desglose) | PASS |
| VAL-1 | Segundo arancel para el mismo destino y mes | 400 ya en la simulación | PASS |
| AUT-1 | Administrador: simular / programar; sin sesión | **403 / 403 / 401** | PASS |

**Auditoría** (migración `V20261031_01__aranceles_impacto_auditado.sql`):

- `ARANCELES` suma columnas `tipo_cambio`, `valor_cambio`, `monto_anterior`, `jugadores_alcanzados`, `diferencia_mensual` e `impacto` (JSON del desglose).
- `TR_AUDIT_ARANCELES` las copia al **mismo** evento que ya registra quién y cuándo.
- El evento existe **solo si hubo commit**: el trigger corre dentro de la transacción.
- La simulación no genera eventos (SIM-0).
- Los aranceles anteriores a HU-106 quedan con esas columnas en null.

## 7. Cuotas históricas

| ID | Caso | Estado |
|---|---|---|
| HIST-1 | Confirmar un mes futuro: suma de verificación de todas las cuotas anteriores, idéntica | PASS |
| HIST-2 | Confirmar el mes en curso: las 1.930 filas con pagos de la categoría y sus meses anteriores, idénticas | PASS |
| Integración `LasCuotasYaEmitidasConservanSuMonto` | Una cuota del mes anterior ($777) y una ya emitida del mes ($888) no cambian al programar y generar | PASS |

## 8. Tiempos, errores y navegador

**Rendimiento** (requisito: < 2 s con datos de club):

| Medición | Resultado |
|---|---|
| HTTP de ida y vuelta, 10 simulaciones | mín. **24,7 ms**, mediana **26,6 ms**, máx. **103,2 ms** |
| Medido en el backend (log) | 13 simulaciones del QA de API: 6 a 149 ms; todo el QA, incluido el navegador: máx. **201 ms** |
| Confirmación | 27,6 a 39,9 ms |

No se agregaron índices: no hubo un problema medido.

**Validaciones (VAL-2, todas 400 con mensaje):**

- porcentaje 1000, 12,345 y 0;
- monto fijo sin arancel vigente ("…Usá «Monto absoluto»…");
- fecha pasada;
- tipo inexistente.

**Navegador** (`qa/HU-106/qa_navegador_hu106.mjs`): Chrome headless sobre la API y la base reales; capturas en `qa/HU-106/capturas/`. **11/11 PASS**:

- el resumen muestra los números del backend;
- el foco queda dentro al abrir;
- Confirmar arranca deshabilitado;
- **con teclado:** escribir 1.502 lo deja deshabilitado y 1.503 lo habilita;
- Tab se queda dentro de la ventana;
- 1024 / 768 / 375 px: entra en la pantalla, sin scroll horizontal; en celular el desglose pasa a tarjetas con etiquetas;
- Esc cierra sin grabar;
- 0 jugadores;
- cambiar el formulario descarta el resumen.

**Defectos encontrados por el QA en navegador (míos, corregidos antes del cierre):**

| ID | Defecto | Causa | Corrección |
|---|---|---|---|
| BUG-106-QA-1 | Con la cantidad bien escrita, "Confirmar actualización" **seguía deshabilitado** | En esta pantalla, Chart.js deja eventos fuera de la zona de Angular. El handler nuevo no repintaba a mano como el resto de la pantalla. Los specs con mocks no lo podían ver. | `cdr.detectChanges()` en el input de confirmación, al cerrar y al descartar |
| BUG-106-QA-2 | En el celular, la **fecha de vigencia** se perdía | La ventana compartida trunca el subtítulo en una línea | Dato "Rige desde" en el cuerpo del resumen |

**Accesibilidad:**

- La suba o la baja se indica **con signo y con palabras** ("+$8.500 (aumento)", "−$2.000 (baja)"); el color solo acompaña (`--action-accent` y `--color-danger`, ≥ 4,5:1 sobre blanco).
- El campo de confirmación tiene `label`, `aria-describedby` y `aria-invalid`.
- La ventana compartida (HU-094) atrapa el foco y lo devuelve al cerrar.

## 9. Pruebas y regresión

| Suite | Resultado | Evidencia |
|---|---|---|
| Backend completo (`dotnet test`, `CACC_TEST_DB` = base de HU-106) | **682 ok · 0 fallos · 11 omitidos** (los omitidos piden otras variables de entorno) | `qa/HU-106/salida_tests_backend.txt` |
| — `ArancelesService.Tests` | HU-069 en el backend (93.500, 95.625, redondeos de mitades, 0,01 %, 999,99 %, inválidos, base faltante); la simulación no escribe; huella estable y sensible; 409 por jugadores y por arancel actual; confirmación reforzada (null / 0 / 41 / 43 rechazados, 42 aceptado); una categoría sin escribir; cero jugadores; mes en curso; casos que ya existían, adaptados | |
| — `ArancelesImpacto.Integration.Tests` (SQL real, transacción revertida) | 6/6: no escribe; jerarquía + bajas + igual al generador; categoría con los dos géneros; históricas intactas; contar = lo que quita la confirmación; auditoría | |
| Frontend completo (`ng test`) | **504/504** (37 archivos) | `qa/HU-106/salida_tests_frontend.txt` |
| — `actualizacion-aranceles.spec.ts` | HU-069: se envía el tipo y el valor **sin calcular**; HU-106: resumen, confirmación reforzada (vacío, 41, "42 ", " 42", texto, 042), una categoría, éxito después del commit, 409, error 500, invalidación, Volver, simulación rechazada | |
| QA API | **26/26** | `qa/HU-106/salida_api.txt` |
| QA navegador | **11/11** | `qa/HU-106/salida_navegador.txt` |

- **Build:** no se ejecutó (`ng build` ni `dotnet build` aparte) por la regla del equipo de no compilar después de los cambios. `dotnet test` y `ng test` compilan todo el código tocado sin errores.
- **Antes de integrar:** `npx ng build` y `dotnet build`.

## 10. Observaciones (no son defectos; decisiones de negocio a confirmar)

- **OBS-106-1 · Base del porcentaje.** Por HU-069, el porcentaje y el monto fijo se aplican sobre el arancel **vigente hoy**, aunque ya haya otro programado antes del mes elegido. En el QA: con $93.500 programado para diciembre, pedir "+10 %" para enero dio otra vez $93.500, porque el 10 % se calculó sobre los $85.000 de hoy. **Ahora el resumen lo deja a la vista** (desglose "$93.500 → $93.500 · Sin cambio"). Antes se habría programado a ciegas. Si se prefiere aplicar sobre el arancel que va a regir ese mes, el cambio está en un solo lugar (`ArancelesService.CalcularImpacto`).
- **OBS-106-2 · Mes en curso.** La "diferencia mensual" es el impacto recurrente desde el mes de vigencia. Este mes, las cuotas que ya tienen pagos conservan el monto anterior; el resumen las informa aparte ("N con pagos se conservan sin cambios").
- **OBS-106-3 · Regla histórica vs. HU-049.** HU-106 dice que no se modifiquen cuotas ya emitidas. El sistema, por HU-049, vuelve a emitir con el monto nuevo las cuotas **pendientes y sin pagos del mes en curso**. No lo cambié, porque es una regla aprobada y esta HU no la menciona. Ahora **se informa antes de confirmar**. Las cuotas con pagos y las de meses anteriores no se tocan nunca (HIST-1/2).
- **OBS-106-4 · Becas y descuentos.** El impacto es sobre el arancel crudo, igual que lo que emite el generador (`monto_base`). Los beneficios se aplican al leer o cobrar la cuota, así que lo que efectivamente se recauda puede ser menor.
- **OBS-106-5 · Origen en la auditoría.** La columna `origen_sistema` llega con `V20261028_01` (rama de integración, todavía no en `main`). Hoy el evento identifica al actor por id y email. Cuando se integre, el mismo evento va a sumar el origen sin cambios en HU-106 (el QA ya contempla los dos casos).
- **OBS-106-6 · Inscripción.** Queda fuera de alcance: no tiene modos de HU-069 y sigue programándose como antes.

## 11. Criterios (Definición de listo)

| Criterio | Estado |
|---|---|
| Simulación backend sin persistencia | ✅ SIM-0, `LaSimulacionNoEscribeNada` |
| Usa datos reales | ✅ consulta sobre `JUGADORES`, `ARANCELES` y `PAGOS` |
| Cantidad de jugadores | ✅ |
| Categorías y género | ✅ nombres, no solo un número |
| Distingue porcentaje / monto fijo / absoluto | ✅ |
| Valores actuales y nuevos (con desglose si difieren) | ✅ SIM-6 |
| Diferencia mensual total | ✅ |
| Jerarquía HU-048 | ✅ SIM-4 + integración |
| Cuotas históricas no participan | ✅ HIST-1/2 (excepción documentada de HU-049, OBS-106-3) |
| Varias categorías → confirmación deliberada con el número real | ✅ front + back |
| Cambios entre simular y confirmar no se aceptan en silencio | ✅ 409 (STALE-1) |
| Solo SuperAdmin | ✅ 403 |
| Actualización transaccional | ✅ `ExecuteInTransaction` + bloqueo |
| Feedback después del commit | ✅ |
| Auditoría | ✅ AUD-1 |
| Cuotas emitidas conservan importe; futuras usan la tarifa nueva | ✅ |
| Simulación < 2 s | ✅ máx. 201 ms |
| Tests en verde | ✅ 682 + 504 |
| QA comparó simulación y resultado real en base de prueba | ✅ REAL-1 + integración |
| Build | ⚠️ no ejecutado por regla del equipo (§9) |
