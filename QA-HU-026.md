# QA — HU-026: Confirmación visual y actualización del estado de cuenta

Evidencia de las pruebas de HU-026 en la rama `feature/HU-026-payment-success-feedback`,
creada desde el commit de HU-025 (`d032ffa`). Pruebas ejecutadas junto con Claude Code el
2026-09-30: unitarias de componente con Vitest, e integración contra la API real sobre la copia
`ClubCamionerosPRUEBA_HU025` (la misma de QA-HU-025.md; la base de desarrollo no se modificó).

---

## 1. Dependencia con HU-025

HU-025 está implementada y commiteada (`d032ffa`). El registro del pago ya es real: la ficha
`deuda-jugador` llama a `POST /api/pagos/cobro`, que persiste en SQL Server dentro de una
transacción (31/31 casos en QA-HU-025.md). **No quedan simulaciones en este flujo.** HU-026 no
modifica la lógica de cobro: trabaja solo a partir de la respuesta 200 del backend.

## 2. Análisis previo: qué existía y qué faltaba

| Tema | Encontrado | Acción en HU-026 |
|---|---|---|
| Toast | `NotificationService` + `<app-toast>` montado una vez en `AdminPortal`. Dura 3,5 s, no bloquea la pantalla y queda registrado en la campana. Tiene un único lugar: un segundo aviso pisa al anterior. | Reutilizado. Solo cambió el texto. |
| Éxito solo con respuesta 200 | Ya lo hacía HU-025 (`notify` dentro de `next`). | Se agregó un test que lo fija con respuesta diferida. |
| Cierre del modal y limpieza | Ya lo hacía HU-025 (cierra el modal, limpia selección y método). | Sin cambios. |
| Relectura de la grilla y la deuda | HU-025 hacía dos GET independientes. Si fallaba la de cuotas, la tabla se reemplazaba por un error genérico. Si fallaba la de deuda, **no se mostraba nada** y el total viejo quedaba como vigente. | `forkJoin` de ambas lecturas. Si falla después de un pago confirmado, aparece un aviso "el pago se registró, pero..." y un botón que **solo relee**. |
| Deuda $0 | "Total adeudado" se ocultaba cuando no quedaban cuotas pendientes. | Se muestra siempre que haya datos reales cargados, incluido $0. |
| Indicador de actualización | No había. | "Actualizando estado de cuenta..." mientras dura la relectura. |
| Badge verde "Pagado" | `.estado-pagado` (HU-025). | Reutilizado. |
| Comprobantes, impresión, descargas, recarga completa | No existían en el flujo. | Se verificó que siguen sin existir (sección 5). |

## 3. Cambios

- `deuda-jugador.ts`:
  - `actualizarEstadoDeCuenta(trasPagoConfirmado)`: lee `GET /api/pagos/deuda/{id}` y `GET /api/pagos/cuotas/{id}` juntos, sin recargar la página.
  - `reintentarActualizacion()`: repite solo esas lecturas, **nunca el POST**.
  - `aplicarCuotas()`, extraído de la carga inicial para no duplicar la depuración de la selección.
  - Estados nuevos: `deudaCargada`, `actualizandoEstadoCuenta`, `avisoActualizacion`.
  - Mensaje del toast: "Pago registrado correctamente. Se abonaron N cuotas por $X." (o "Se abonó 1 cuota por $X.").
- `deuda-jugador.html`: el total adeudado se muestra también en $0 (`.total-adeudado`, en rojo solo si es mayor a 0). Se agregaron el aviso con "Reintentar actualización" y el indicador de actualización.
- `deuda-jugador.css`: estilos del aviso, con las variables `--pending-*` de la misma pantalla.
- **Sin cambios en backend, endpoints ni migraciones.**

## 4. Pruebas unitarias (frontend)

| Comando | Resultado |
|---|---|
| `npx ng test --watch=false --include src/app/admin-portal/deuda-jugador/deuda-jugador.spec.ts --include src/app/services/pagos.cobro.spec.ts` | **24/24**: 12 de HU-025 (siguen verdes), 9 nuevos de HU-026, 3 del servicio |
| `npx ng test --watch=false` (suite completa) | **138/139**. El fallo es el preexistente de `cuotas-pagos.spec.ts` ("Deuda Global Total:" vs "Deuda Global:", commit `b4ee421`), ya documentado en QA-HU-025.md. |

Se trabajó con TDD. De los 9 tests nuevos, **6 fallaron antes de implementar** (texto del toast,
total $255.000 → $85.000, deuda $0, falla de la relectura y error sin cambio de deuda, que
dependía del selector nuevo). Los otros 3 ya pasaban con el comportamiento de HU-025: éxito recién
tras la respuesta, estado multi-cuota y ausencia de comprobantes. Quedan como regresión.

| # | Caso (spec HU-026) | Estado |
|---|---|---|
| U1 | Con la respuesta del backend demorada: sin toast, modal abierto con "Registrando..." y la cuota sigue Vencido. Al llegar el 200, un único toast de éxito. | ✅ |
| U2 | Texto exacto: "Pago registrado correctamente. Se abonaron 2 cuotas por $ 170.000." | ✅ |
| U3 | Una cuota: "Se abonó 1 cuota por $ 85.000." | ✅ |
| U4 | Marzo y abril pagados → 2 badges `.estado-pagado`; mayo sigue Pendiente y seleccionable. | ✅ |
| U5 | Deuda $255.000 → $85.000 según lo que devuelve `GET /pagos/deuda` (llamado 2 veces). | ✅ |
| U6 | Todo pagado → "Total adeudado" muestra $ 0. | ✅ |
| U7 | Misma ficha: no navega, misma instancia del componente, sin `window.print`, sin `URL.createObjectURL`, sin `a[download]`, sin textos de imprimir/comprobante/recibo/descargar/PDF. | ✅ |
| U8 | Pago OK y relectura fallida: 1 solo toast (éxito), modal cerrado, aviso "El pago se registró correctamente, pero no se pudo actualizar...". "Reintentar actualización" relee, el aviso desaparece y se ve Pagado y $85.000. `cobrarCuotas` se llamó **1 sola vez**. | ✅ |
| U9 | Error 500 del cobro: sin toast de éxito, modal abierto con el error, deuda $255.000 y marzo Vencido sin cambios, sin aviso de relectura. | ✅ |

## 5. Pruebas de integración con HU-025 (API real + SQL Server)

API levantada con `dotnet run` contra `ClubCamionerosPRUEBA_HU025` (mismo procedimiento y
tokens de QA descritos en QA-HU-025.md, sección 4). El script reproduce las llamadas de la ficha:
`POST /pagos/cobro` y, tras el 200, la relectura `GET /pagos/deuda/10` + `GET /pagos/cuotas/10`.
Jugador 10: sin beneficios, con enero ($70.000), marzo y abril ($85.000) vencidos.

| # | Caso | Resultado obtenido | Estado |
|---|---|---|---|
| I1 | Cobro de 1 cuota (enero) | 200, `cuotas.length` 1, `montoTotal` 70000 (los datos del toast) | ✅ |
| I2 | Relectura tras 1 cuota | Enero Pagado; marzo y abril siguen Vencido | ✅ |
| I3 | Deuda recalculada por el backend | $240.000 → $170.000 | ✅ |
| I4 | Cobro rechazado (enero de nuevo) | 400 "ya fueron abonadas"; deuda y estados sin cambios | ✅ |
| I5 | Cobro de 2 cuotas (marzo y abril) | 200, 2 cuotas, $170.000 | ✅ |
| I6 | Relectura tras 2 cuotas | Marzo y abril Pagado; septiembre sin cambios | ✅ |
| I7 | Deuda recalculada | $170.000 → **$0** | ✅ |
| I8 | Sin cuotas impagas | El backend devuelve deuda 0 | ✅ |

**Resultado: 8/8.** En la copia, septiembre del jugador 10 ya estaba pagado desde la prueba de
concurrencia de HU-025; por eso la deuda inicial era $240.000.

**Ausencia de comprobantes y de recarga completa en el código:** sobre el diff de HU-026 (sin
specs), `git diff | grep -iE "print|pdf|blob|download|createObjectURL|location\.|reload|setTimeout"`
→ ninguna coincidencia. Tampoco se agregaron delays artificiales.

## 6. Criterios de aceptación

| Criterio | Estado |
|---|---|
| Toast después de un pago realmente exitoso | ✅ U1, U2, I1, I5 |
| Sin toast ante error | ✅ U9, más los tests de error de HU-025 |
| Mensaje claro | ✅ U2, U3 |
| Modal/proceso cerrado tras el éxito | ✅ U8 y tests de HU-025 |
| Sin comprobantes, descargas ni impresión | ✅ U7 y grep del diff |
| Grilla actualizada automáticamente, sin recargar la página | ✅ U4, U7, I2, I6 |
| Cuotas pagadas en estado Pagado con badge verde | ✅ U4 (`.estado-pagado`) |
| Total de deuda actualizado; deuda parcial correcta; $0 | ✅ U5, U6, I3, I7 |
| Funciona con una y con varias cuotas | ✅ U3/I1, U4/I5 |
| Los datos actualizados provienen del backend | ✅ U5, U8, I2 a I8 |
| Un error al refrescar no reintenta el cobro | ✅ U8 (`cobrarCuotas` × 1) |
| Sin mocks como implementación final | ✅ Los dobles existen solo en los specs |
| No se rompió HU-025 | ✅ Sus 12 specs siguen verdes; suite 138/139 (fallo preexistente) |
| Prueba visual en navegador | ⏳ **Pendiente** (sección 8) |

## 7. Bugs encontrados y corregidos

1. **Deuda desactualizada silenciosa.** Si fallaba la relectura de la deuda después de un cobro,
   la ficha seguía mostrando el total anterior sin ningún aviso. Corregido con la relectura
   combinada y el aviso explícito.
2. **"Total adeudado" desaparecía al saldar la deuda** en lugar de mostrar $0. Corregido.
3. **Una falla de relectura reemplazaba la grilla** por un error genérico que no aclaraba que el
   pago sí se había registrado. Corregido: la grilla anterior queda visible bajo un aviso que
   indica que puede estar desactualizada.

Siguen abiertos, preexistentes y fuera de alcance: el spec desactualizado de `cuotas-pagos` y el
500 de `/pagos/recientes` y `/pagos/registrados` con pagos legacy (ver QA-HU-025.md, sección 7).

## 8. Pendientes para cerrar la HU

- **Prueba visual manual en navegador** (Angular + API real). No se ejecutó en esta sesión.
  Recorrido: ficha de un jugador con cuotas → pagar 1 y después 2 cuotas → verificar el toast
  arriba a la derecha (3,5 s), el modal cerrado, las filas en verde "Pagado" y el "Total
  adeudado" actualizado ($0 al saldar todo). Para simular una falla de relectura, detener la API
  justo después del 200 (o bloquear `GET /pagos/cuotas` en DevTools) y comprobar el aviso y el
  botón "Reintentar actualización".
- Como en HU-025: la base de desarrollo no tiene cuotas emitidas (HU-009 pendiente y
  `PERSONA.genero` NULL), así que la prueba visual necesita una base con cuotas reales o la copia
  de QA.

## 9. Observación

El flujo de pago parcial "Pagar saldo" (`POST /pagos/registrar`), que convive en la misma ficha,
también usa ahora la relectura combinada con aviso. Mantiene su mensaje de éxito propio
(banner en la página) y no se unificó con el toast, para no ampliar el alcance de HU-026.
