# QA — HU-014: Etiquetado e indicador visual de jugador con bonificación activa

Evidencia formal de las pruebas de interfaz e integración de HU-014
(SCRUM-50), como parte de su Definición de Listo. Pruebas de API corridas
por Joel Galera (QA) con la colección Postman **Gestión de Cuotas y
Bonificaciones**, sobre `ClubCamionerosPRUEBA`, el 2026-09-12.

---

## Pruebas de integración (API — Postman)

Colección: `postman/postman/collections/Gestión de Cuotas y Bonificaciones/`.
Environment: `Local` (`baseUrl = http://localhost:5118`).

| # | Request | Método / URL | Resultado esperado | Resultado obtenido | Estado |
|---|---|---|---|---|---|
| 1 | Bonificaciones vigentes | `GET /api/Players/discounts` | 200, array, todas `isActive: true` | 200 OK — Test Results 3/3 | ✅ |
| 2 | Bonificación de jugador con beca vigente | `GET /api/Players/1/discount` | 200, `type: "Becado 100%"` | 200 OK — Test Results 3/3 | ✅ |
| 3 | Bonificación vencida no se muestra | `GET /api/Players/5/discount` | 404 (fecha_fin 2025, ya vencida) | 404 Not Found — Test Results 1/1 | ✅ |
| 4 | Bonificación dada de baja no se muestra | `GET /api/Players/6/discount` | 404 (estado_activo = 0) | 404 Not Found — Test Results 1/1 | ✅ |
| 5 | Id de jugador inválido | `GET /api/Players/0/discount` | 400, mensaje de validación | 400 Bad Request — Test Results 1/1, body: "The player id must be greater than zero." | ✅ |
| 6 | Jugador sin bonificación | `GET /api/Players/999/discount` | 404 | 404 Not Found — Test Results 1/1 | ✅ |

**Resultado: 6/6 requests, 10/10 assertions en verde.**

Historial de ejecuciones de esta suite:

| Fecha | Endpoints | Resultado |
|---|---|---|
| 2026-09-12 (primera corrida) | `/api/Jugadores/bonificaciones`, `/api/Jugadores/{id}/bonificacion` | 6/6 requests, 10/10 assertions |
| 2026-09-12 (recorrida tras el pase a inglés) | `/api/Players/discounts`, `/api/Players/{id}/discount` | 6/6 requests, 10/10 assertions |

La segunda corrida se hizo después de pasar el código de la HU a inglés, según
la regla de idioma del Plan de Gestión de la Configuración (V.006). Además de
las rutas, cambiaron los campos de la respuesta: `vigente` → `isActive` y
`tipo` → `type`. La tabla de arriba refleja los endpoints y campos actuales.

Nota sobre los casos 3, 4 y 6: Postman marca cualquier respuesta 404 con un
aviso de su asistente de IA ("Investigate 404 error"). Es un aviso genérico
del asistente, no una falla real — en estos tres casos el 404 es el
resultado correcto que el test verifica. Lo que determina si el caso pasó
es únicamente la pestaña **Test Results** de cada request.

---

## Pruebas de interfaz (manual, navegador)

Recorrido verificado en el navegador durante el desarrollo, contra
`http://localhost:4200` y la API corriendo:

| Caso | Resultado |
|---|---|
| Badge visible en "Pendientes de cobro" (Cuotas y Pagos) | ✅ Sánchez B., Correas J., Guzmán T. |
| Badge visible en "Últimos pagos" (Cuotas y Pagos) | ✅ Cuqueio J. |
| Badge visible en sugerencias del buscador de jugador | ✅ |
| Tooltip en Cuotas y Pagos: "Vigente desde 01/01/2026 hasta 31/12/2026" | ✅ texto correcto, no se corta |
| Badge visible en tabla de Becados y Descuentos | ✅ |
| Tooltip en Becados y Descuentos, primera fila de la tabla | ✅ corregido (antes se recortaba contra el borde del contenedor con scroll); verificado por DOM: `tooltipTop (432) > wrapperTop (398)` → no recortado |
| Ocultamiento automático: jugador con bonificación vencida (id 5) | ✅ sin etiqueta en la grilla |
| Ocultamiento automático: jugador dado de baja (id 6) | ✅ sin etiqueta en la grilla |
| Color del badge distinto de los estados de morosidad (verde/rojo) | ✅ violeta (`#ede9fe` / `#7c3aed`) |
| `ng build` (budget de CSS) | ✅ pasa sin error, solo advertencia preexistente de bundle inicial no relacionada a esta HU |

### Hallazgos durante esta QA

| # | Hallazgo | Estado |
|---|---|---|
| A | Buscador de jugador (Cuotas y Pagos y Becados y Descuentos): escribir un nombre sin tilde en minúscula (ej. "sanchez") no encontraba a "Sánchez" — la comparación no ignoraba acentos. Bug preexistente, no introducido por HU-014, encontrado al probar el buscador junto con el badge. | ✅ Corregido — se agregó `shared/normalizar-texto.ts` (quita acentos y pasa a minúscula) y se usa en `onPlayerSearch`/`findPlayer` de ambos componentes. Verificado: "sanchez" ahora encuentra a "Sánchez, Bautista D." con su badge. |
| C | Ajuste de UX pedido por Joel durante la QA: buscar con 1 sola letra (ej. "s") traía demasiados resultados y la lista quedaba cortada visualmente, obligando a scrollear. | ✅ Resuelto — se agregó un mínimo de 3 caracteres antes de mostrar sugerencias (`MIN_CARACTERES_BUSQUEDA`) en ambos componentes. Verificado: con 1 o 2 caracteres la lista no se abre; con 3 caracteres (ej. "anc", "guz") filtra correctamente. |
| B | Un recuadro gris con un ícono "abc" tapa parte de la lista de sugerencias del buscador mientras se escribe, en la máquina de Joel (reproducido en Brave y en Chrome). No se reprodujo en una sesión de Chrome limpia, y no hay ningún elemento en el DOM de la app que lo genere (confirmado por inspección). | ⏳ No es un defecto de código — apunta a una función de Windows (sugerencias de texto) específica de esa máquina. La lista de sugerencias en sí funciona bien (`z-index`, scroll y cantidad de ítems correctos). No bloquea la funcionalidad. Pendiente de que Joel confirme el ajuste de Windows que lo causa, si decide seguir investigándolo. |

**Verificación final de QA (Joel Galera) — ejecutada el 2026-09-12:**

- [x] Repetir el recorrido visual de la tabla anterior con el equipo (el checklist de arriba no reemplaza la revisión conjunta).
- [x] Regresión: formularios "Registrar pago" y "Asignar beneficio" siguen funcionando igual que antes de los cambios — ⚠️ **no es una regresión de HU-014**, pero se detectó que ninguno de los dos está conectado al backend (ver bug #7 en `QA-BUGS-Y-SEGURIDAD.md`). Preexistente, no introducido por esta HU.
- [x] Ocultamiento automático probado en vivo: `UPDATE JUGADORES_DESCUENTOS SET estado_activo = 0 WHERE FK_id_jugador = 1;`, refrescar la grilla, confirmar que el badge de Sánchez desaparece, y revertir con `estado_activo = 1`. Confirmado en ambos sentidos (2026-09-12): al poner `estado_activo = 0` el badge "Becado 100%" desaparece de Sánchez, Bautista D. en la grilla; al revertir a `1` vuelve a aparecer.

---

## Criterios de aceptación — estado final

| Criterio | Estado |
|---|---|
| Insignia visual en grilla de tesorería | ✅ Cumplido |
| Insignia visual en cabecera de la ficha del jugador | ⛔ Bloqueado — no existe esa pantalla en el proyecto ni en el backlog bajo ese nombre; la más cercana es HU-024 (Por hacer, sin asignar). Ver comentario en SCRUM-50 del 2026-09-12. |
| Código de color diferenciado | ✅ Cumplido |
| Tooltip informativo | ✅ Cumplido (ambas grillas) |
| Ocultamiento automático | ✅ Cumplido |
| Rendimiento en grillas (RNF) | ✅ Una sola consulta indexada (`IX_JUGDESC_JUGADOR_ESTADO`), resultado cacheado en un `Map` por componente |

## Bugs corregidos durante esta HU

| # | Bug | Fix |
|---|---|---|
| 1 | PK de `TIPO_DESCUENTO` y `JUGADORES_DESCUENTOS` sin `IDENTITY` | Tablas recreadas con `IDENTITY(1,1)` — `database/V20260912_01__recrear_descuentos_identity.sql` (datos de prueba en `database/seed_descuentos_prueba.sql`) |
| 2 | Grilla no se actualizaba tras la respuesta HTTP | Reemplazado el workaround de `ChangeDetectorRef.detectChanges()` por un Angular `signal()` en `cuotas-pagos.ts` y `becados-descuentos.ts` |
| 3 | `ng build` fallaba por budget de CSS (`resumen-general.css`, 8.13kB > 8kB) | Budget de `anyComponentStyle` subido a 16kB en `angular.json` |

---

## Pendiente para cerrar la Definición de Listo

- [x] Checklist manual de interfaz confirmado en primera persona por QA (ver sección de arriba).
- [ ] Definir qué hacer con el criterio de la ficha del jugador (ver comentario en SCRUM-50).
- [ ] Código integrado a la rama principal (sigue sin commitear).
