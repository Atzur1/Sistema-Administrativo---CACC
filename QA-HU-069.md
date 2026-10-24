# QA — HU-069: Aumento de arancel por porcentaje o por monto fijo

Evidencia de las pruebas de HU-069. Amplía el formulario "Programar nueva cuota mensual" de la pantalla
**Actualización de Aranceles** (HU-034). Pruebas ejecutadas el 2026-10-24: unitarias con
Vitest (`ng test`) y compilación de producción (`ng build`).

---

## 1. Análisis previo: qué existía y qué faltaba

| Tema | Encontrado | Acción en HU-069 |
|---|---|---|
| Formulario de cuota | `actualizacion-aranceles` con destino (género o categoría), monto absoluto y fecha de vigencia. | Se agregó el selector **Tipo de aumento** arriba de los campos. |
| Arancel vigente por destino | El historial (`GET /api/aranceles/historial`) ya trae cada arancel con su estado (`Vigente`/`Programado`/`Anterior`), género o categoría. | Se reutiliza: no hay pedidos nuevos a la API. Cada fila del historial guarda su destino (`target`), con el mismo valor que el selector del formulario. |
| Firma de la API | `POST /api/aranceles/programar` recibe `{ genero | idCategoria, monto, vigenteDesde }`, con el monto absoluto. | **Sin cambios.** La conversión se resuelve en el frontend. |
| Validaciones de HU-034 | Monto > $0 en cuota ($0 permitido en inscripción), fecha no anterior a hoy y un arancel por mes y destino (lo valida el backend). | Se mantienen. Se agregaron mensajes por campo, que antes no había para el monto. |
| Inscripción | Pestaña aparte, con monto absoluto por género. | **Fuera de alcance:** sigue solo con monto absoluto. Al pasar a esa pestaña el modo vuelve a "Monto absoluto". |

## 2. Cambios

- `tariff-increase.ts` (nuevo): funciones puras.
  - `calculateNewTariff(mode, value, currentTariff)`:
    - absoluto → `value`;
    - porcentaje → `Math.round(vigente * (1 + value / 100))`;
    - monto fijo → `vigente + value`;
    - `null` si no hay valor, o si el modo es relativo y no hay arancel vigente.
  - `needsCurrentTariff(mode)`.
  - `sanitizePercentage(raw)`: acepta coma o punto, hasta 3 enteros y 2 decimales, y muestra coma.
- `actualizacion-aranceles.ts`:
  - `increaseMode`, `setIncreaseMode()`. Cambiar de modo limpia el monto y conserva el destino.
  - `arancelVigente(target)`, `currentTariff`, `resultingAmount` y `tariffPreview`.
  - `amountLabel`, `amountPlaceholder`, `amountError` y `onAmountInput()`.
  - El validador del monto suma el error `sinArancelVigente` y exige un valor > 0 en los modos relativos.
  - Al recargar el historial se revalida el monto, porque el vigente puede haber cambiado.
  - `onSubmit()` envía `resultingAmount`, es decir, el monto absoluto final.
- `actualizacion-aranceles.html`: control segmentado, campo con etiqueta, prefijo o sufijo y placeholder según el modo, mensaje debajo del campo y tarjeta de vista previa. El texto de ayuda menciona los tres modos.
- `actualizacion-aranceles.css`: estilos del control segmentado y de la vista previa, con las variables globales `--action-*` y `--color-info*`. En pantallas de hasta 520 px las opciones se apilan.
- **Sin cambios en backend, endpoints ni migraciones.**

| Modo | Etiqueta del campo | Placeholder | Signo | Mensaje si está vacío | Mensaje si es ≤ 0 |
|---|---|---|---|---|---|
| Monto absoluto | Nuevo monto | 92.000 | `$` | Ingresá el nuevo monto. | El monto tiene que ser mayor a $0. |
| Porcentaje (+%) | Porcentaje de aumento | 12,5 | `%` | Ingresá el porcentaje de aumento. | El porcentaje tiene que ser mayor a 0. |
| Monto fijo a sumar (+$) | Monto a sumar | 10.000 | `+$` | Ingresá el monto a sumar. | El monto a sumar tiene que ser mayor a $0. |

Destino sin arancel vigente, en modo porcentaje o monto fijo: *"Categoría X no tiene un arancel vigente sobre el que
calcular el aumento. Usá "Monto absoluto" para definir su primer valor."* Este mensaje aparece apenas se elige el
destino, sin esperar a que se escriba. El botón queda deshabilitado.

## 3. Pruebas unitarias (frontend)

| Comando | Resultado |
|---|---|
| `npx ng test --watch=false --include src/app/admin-portal/actualizacion-aranceles/actualizacion-aranceles.spec.ts --include src/app/admin-portal/actualizacion-aranceles/tariff-increase.spec.ts` | **48/48**: los 15 previos de la pantalla siguen verdes, más 21 nuevos del componente y 12 de las funciones de cálculo |
| `npx ng test --watch=false` (suite completa) | **409/409** |
| `npx ng build` | Compila. Avisa que el bundle inicial supera el presupuesto de 1 MB (1,21 MB); ese aviso es anterior a esta HU. |

### 3.1 `tariff-increase.spec.ts` (cálculo)

| # | Caso | Estado |
|---|---|---|
| C1 | Absoluto: devuelve el monto escrito ($92.000) | ✅ |
| C2 | Absoluto: no necesita arancel vigente | ✅ |
| C3 | Porcentaje: $85.000 + 10% = $93.500 | ✅ |
| C4 | Porcentaje: $85.000 + 12,5% = **$95.625** | ✅ |
| C5 | Porcentaje: redondeo al peso más cercano (94.110,5 → 94.111; 93.866,3 → 93.866; siempre entero) | ✅ |
| C6 | Porcentaje sin arancel vigente → `null` | ✅ |
| C7 | Monto fijo: $85.000 + $10.000 = $95.000 | ✅ |
| C8 | Monto fijo sin arancel vigente → `null` | ✅ |
| C9 | Sin valor escrito (o NaN) → `null` | ✅ |
| C10–C13 | Entrada del porcentaje: coma o punto, un solo separador, 2 decimales, 3 enteros, descarta letras y signos, ",5" → "0,5" | ✅ |

### 3.2 `actualizacion-aranceles.spec.ts` (componente, bloque "HU-069 increase modes")

| # | Caso | Estado |
|---|---|---|
| U1 | Arranca en "Monto absoluto" y ofrece los tres modos con sus etiquetas | ✅ |
| U2 | Toma solo el arancel en estado `Vigente` del destino (ignora `Anterior`). Una categoría sin arancel da `null` | ✅ |
| U3 | **Absoluto:** envía `programar({ genero: 'Masculino' }, 92000, …)` | ✅ |
| U4 | Absoluto sobre una categoría sin arancel (su primer valor): válido, sin mensaje, se envía | ✅ |
| U5 | **Porcentaje:** 12,5% sobre $85.000 envía **95625** | ✅ |
| U6 | Porcentaje: 7,5% sobre $61.999 = 66.648,925 → envía **66649** | ✅ |
| U7 | Porcentaje: cambiar el destino recalcula al instante (Masculino $93.500 → Categoría AFA $99.000) | ✅ |
| U8 | Porcentaje escrito "12,5": el input muestra "12,5", el formulario guarda "12.5" y el resultado es $95.625 | ✅ |
| U9 | **Monto fijo:** +$10.000 sobre $85.000 envía **95000** | ✅ |
| U10 | Vista previa: `$85.000 ➔ $95.625 (+$10.625)` | ✅ |
| U11 | Vista previa oculta sin destino o sin resultado válido (destino sin arancel vigente) | ✅ |
| U12 | Vista previa de un primer arancel en modo absoluto: "Sin definir ➔ $50.000" | ✅ |
| U13 | **Mensaje:** vacío y tocado → "Ingresá el monto a sumar." (antes de tocarlo no se muestra) | ✅ |
| U14 | **Mensaje:** absoluto $0 → "El monto tiene que ser mayor a $0." | ✅ |
| U15 | **Mensaje:** porcentaje 0 → "El porcentaje tiene que ser mayor a 0." y no se envía | ✅ |
| U16 | **Mensaje:** monto fijo $0 → "El monto a sumar tiene que ser mayor a $0." | ✅ |
| U17 | **Mensaje:** destino sin arancel vigente, apenas se elige. El error `sinArancelVigente` invalida el formulario y no se envía | ✅ |
| U18 | El mismo destino vuelve a ser válido al pasar a "Monto absoluto" | ✅ |
| U19 | Cambiar de modo limpia el monto y conserva el destino | ✅ |
| U20 | Pasar a la pestaña Inscripción vuelve al modo absoluto | ✅ |

## 4. Verificación manual de interfaz (checklist para QA)

Ruta: Portal Administrativo → **Actualización de Aranceles** → pestaña **Cuota mensual**. Precondición: Masculino
con arancel vigente (por ejemplo, $85.000) y al menos una categoría **sin** arancel propio.

| # | Paso | Resultado esperado | Estado |
|---|---|---|---|
| M1 | Abrir la pantalla | Se ve "Tipo de aumento" con "Monto absoluto" seleccionado. El campo dice "Nuevo monto", con `$` y placeholder 92.000. | ⬜ |
| M2 | Elegir Masculino y escribir 92000 | El campo muestra "92.000". La vista previa dice "Arancel vigente: $85.000 ➔ Nuevo arancel a aplicar: $92.000 (+$7.000)". | ⬜ |
| M3 | Cambiar a "Porcentaje (+%)" | El campo se vacía, pasa a "Porcentaje de aumento", con `%` a la derecha y placeholder 12,5. El destino se conserva. | ⬜ |
| M4 | Escribir 12,5 | La vista previa se actualiza al escribir: $85.000 ➔ **$95.625** (+$10.625). | ⬜ |
| M5 | Cambiar el destino a Femenino | La vista previa se recalcula con el vigente femenino, sin volver a escribir. | ⬜ |
| M6 | Escribir letras o "-" en el porcentaje | No se aceptan. Solo dígitos y una coma, con hasta 2 decimales. | ⬜ |
| M7 | Escribir 0 y salir del campo | Mensaje en rojo "El porcentaje tiene que ser mayor a 0.", sin vista previa y botón deshabilitado. | ⬜ |
| M8 | Borrar el valor y salir del campo | "Ingresá el porcentaje de aumento." | ⬜ |
| M9 | Elegir la categoría sin arancel | Aparece de inmediato "Categoría X no tiene un arancel vigente…" y el botón queda deshabilitado. | ⬜ |
| M10 | Con esa categoría, cambiar a "Monto absoluto" | El mensaje desaparece. Al escribir un monto, la vista previa dice "Sin definir ➔ $X". | ⬜ |
| M11 | "Monto fijo a sumar (+$)", Masculino, 10000 | El campo muestra "+$ 10.000" y la vista previa $85.000 ➔ $95.000. | ⬜ |
| M12 | Programar (M11) con una fecha futura | Toast de éxito. En el historial aparece un arancel **Programado** de **$95.000**. En la pestaña Network, el body del POST lleva `"monto": 95000`. | ⬜ |
| M13 | Programar otro arancel para el mismo destino y el mismo mes | El backend lo rechaza como antes (un arancel por mes y destino) y el mensaje aparece en el toast. | ⬜ |
| M14 | Ir a la pestaña Inscripción | No aparece el selector de modo. El formulario es el de siempre, con monto absoluto. | ⬜ |
| M15 | Achicar la ventana a menos de 520 px | Las tres opciones se apilan, la vista previa se acomoda y no hay scroll horizontal. | ⬜ |
| M16 | Navegar con teclado (Tab y flechas) | Se puede elegir el modo con el teclado y se ve el foco sobre la opción. | ⬜ |

## 5. Decisiones y límites

- **Base del cálculo:** el arancel en estado **Vigente** hoy para ese destino. Si ya hay uno **Programado** a futuro, el aumento se sigue calculando sobre el vigente, no sobre el programado.
- **Categoría sin arancel propio:** sus jugadores pagan el arancel de su género, pero la categoría no tiene "arancel vigente" propio. Por eso los modos relativos se bloquean y se pide definir su primer valor en monto absoluto.
- **Redondeo:** solo en porcentaje, con `Math.round` sobre el monto final (los .5 suben). El monto fijo y el absoluto ya son enteros, porque el campo solo acepta dígitos.
- **Inscripción:** sin modos relativos (fuera del alcance de la HU).
