# QA HU-090: mensajes de la API en español

**Fecha:** 04/11/2026 · **Rama:** `FramD` (sin commitear)

**Resultado: PASS en lo automatizado · QA manual contra la API PENDIENTE (BLOCKED: entorno no levantado).**

| Prueba | Resultado |
|---|---|
| Tests backend (`dotnet test ApiGestion.Tests`) | **761 pasan · 1 falla · 167 omitidos** (usan base de datos). La falla es preexistente y ajena a la HU (ver §8). Hay 39 casos nuevos o ajustados para HU-090 |
| Tests frontend (`ng test`) | **631/631** en 43 archivos; 14 nuevos para HU-090 |
| Build frontend (`ng build`) | **Correcto.** El aviso de presupuesto (1,23 MB contra 1,00 MB) ya existía antes de la HU |
| QA manual con Postman o navegador | **Pendiente.** No había API ni base levantadas. El checklist de §7 está listo |

---

## 1. Relevamiento

La HU describía mensajes en inglés y un parseo de texto en Becados y Descuentos. Revisé cada afirmación contra el código antes de cambiar nada.

| Afirmación de la HU | Estado real | Evidencia |
|---|---|---|
| Becados interpreta texto en inglés para sacar fechas | **Ya se había eliminado** en `c2fd207` ("HU-083 a HU-091"), que sacó `body.match(/\d{4}-\d{2}-\d{2}/g)` sobre el mensaje del 409. Hoy la pantalla muestra `error.mensaje` y no quedaba ningún regex sobre mensajes | `git show c2fd207` |
| Respuestas de negocio en inglés (beneficios, alta, aranceles, inscripción) | **No.** Los servicios (`BeneficiosService`, `JugadoresAltaService`, `ArancelesService`, `InscripcionesService`, `PagosService`) y `ResultadoHttp` ya respondían `{ exito, mensaje }` en español | Búsqueda de textos en inglés fuera de logs y comentarios: 0 resultados |
| Validaciones de DTO en inglés | **Sí, por tres caminos que el código no veía** (§2) | — |

### Inglés que sí le llegaba al usuario

1. **Formato de la validación.** Fuera de la API de integración, un DTO inválido respondía el `ValidationProblemDetails` del framework (`"title": "One or more validation errors occurred."`, `"errors": { "Reason": [...] }`), **sin `mensaje`**. Las pantallas leen `err.error?.mensaje`, así que el texto en español del DTO nunca se mostraba: el usuario veía el texto genérico de cada pantalla.
2. **`[Required]` implícito.** Con `<Nullable>enable</Nullable>`, .NET marca como obligatorio cada `string` no anulable con su texto por defecto: `"The Periodo field is required."`. Afectaba a `RegisterPaymentRequestDTO.Periodo` y `MetodoPago`, `CollectFeesRequestDTO.MetodoPago`, `TariffChangeRequestDTO.Tipo` y `ScheduleTariffRequestDTO.Huella`.
3. **Atributos sin `ErrorMessage`.** `CollectFeesRequestDTO.IdsPago` (`Required`, `MinLength`, `MaxLength`), los DTOs de Usuarios y Permisos y `LoginDTO` devolvían `"The field IdsPago must be a string or array type with a minimum length of '1'."` y otros similares.
4. **JSON ilegible.** `"The JSON value could not be converted to System.Nullable`1[System.Decimal]. Path: $.percentage…"` y `"A non-empty request body is required."`.
5. **Errores sin cuerpo** (`NotFound()` vacío, 415): `ProblemDetails` con `title` en inglés.

### Otros problemas encontrados

- **Fechas que dependen de la cultura del servidor.** `{fecha:dd/MM/yyyy}` aparecía en 13 mensajes. En .NET, la `/` de un formato personalizado es el separador de la cultura activa: con `de-DE` sale `01.10.2026` y con `nl-NL`, `01-10-2026`. No había ninguna cultura configurada.
- **Montos que dependen de la cultura.** Con `{monto:N0}`, un servidor `en-US` mostraba `$85,000` en los mensajes de inscripción y de pago.
- **Textos técnicos.** Aparecía "No existe la categoría con id 999." en aranceles y en el alta de jugador.
- **Cuerpo crudo en pantalla.** El pago de inscripción mostraba en el toast el cuerpo de la respuesta si era un string (por ejemplo, el HTML de un proxy).
- **Toasts vacíos o "[object Object]".** En aranceles, `err.error?.mensaje ?? fallback` dejaba pasar `{ mensaje: "" }` y un `mensaje` que fuera un objeto.

## 2. Contrato final

Todo error funcional del Portal Administrativo responde con este cuerpo:

```json
{ "exito": false, "mensaje": "Texto claro en español" }
```

| Origen | Antes | Ahora | Status |
|---|---|---|---|
| Resultado de un servicio (`ResultadoHttp`) | `{ exito, mensaje }` | Sin cambios | 400 / 404 / 409 / 422 (sin cambios) |
| Validación de DTO | `ValidationProblemDetails` en inglés y sin `mensaje` | `{ exito, mensaje }` que junta todos los mensajes del DTO | **400** (sin cambios) |
| JSON ilegible o sin cuerpo | `errors` con el texto de System.Text.Json | `El dato «porcentaje» no tiene un formato válido.` / `No se recibieron los datos del formulario o no tienen un formato válido.` | **400** |
| Ruta o query inválida (`/players/abc/...`) | `"The value 'abc' is not valid."` | `Uno de los valores indicados no es válido.` | **400** |
| Error sin cuerpo (`NotFound()`, 415) | `ProblemDetails` con `title` en inglés | `{ exito, mensaje }` en español | **mismo status** |
| Excepción no controlada | `{ exito, mensaje }` genérico | Sin cambios | 500 |
| API de integración (Deportivo) | `{ errorCode, message, field }` (HU-058) | **Sin cambios**: tiene su propio contrato | — |

Nunca se muestran nombres de propiedades, de columnas ni de tipos .NET. Un dato del JSON que no está en el diccionario de etiquetas da el mensaje genérico, sin nombrarlo.

## 3. Superposición de beneficios (409)

```json
409
{ "exito": false, "mensaje": "El jugador ya tiene la bonificación \"Beca Completa\" vigente del 01/10/2026 al 31/12/2026. Ajustá las fechas o cancelá la existente." }
```

- Nombra el **motivo** real de la bonificación (el del catálogo: Becado, Descuento…), su **estado** (`vigente` o `programada`) y su **período**.
- Antes decía "El jugador ya tiene una bonificación para ese período (Becado, del 01/10/2026 al 31/12/2026)…": nombraba el motivo pero no el estado, y la fecha dependía de la cultura.
- **Carrera entre dos pedidos.** Si la transacción rechaza por superposición porque otro pedido grabó primero, ahora se vuelve a consultar cuál quedó grabada para nombrarla. Antes devolvía un texto genérico.
- La regla no cambia: la misma consulta y la misma transacción. Solo cambia el texto.

## 4. Fechas y montos

`EntityLibrary/FormatoVisible`:

- `Fecha(...)` devuelve `dd/MM/yyyy` con `InvariantCulture`.
- `Pesos(...)` devuelve `$85.000` con `es-AR`.

Se usa en todos los mensajes que llevan fecha o monto (beneficios, aranceles, aranceles de inscripción, inscripción, pagos y bajas). Los tests corren con `de-DE`, `en-US`, `nl-NL`, `de-CH` y `es-AR`.

## 5. Frontend

- **`mensajeDeError(error, fallback)`** en `shared/http-error-message.ts` es el único punto que convierte un error en texto. Muestra el `mensaje` de la API tal cual. Si no hay un mensaje utilizable, muestra el texto propio de la pantalla o "Ocurrió un error al procesar la solicitud.". Sin conexión, dice "No se pudo conectar con el servidor…". Nunca muestra un cuerpo crudo, `[object Object]`, `message` ni `errors`.
- **Lo usan Becados y Descuentos, Actualización de Aranceles (aranceles e inscripción) y Cuotas y Pagos** (pago de cuota y de inscripción).
- **Becados:** no hay parseo (ya se había sacado). El 409 se muestra tal cual. Se eliminó además el texto técnico "Verificá que la API esté levantada.".
- **Aranceles:** el 409 de "impacto desactualizado" sigue leyendo `impacto` como **dato estructurado** del contrato, no del texto.
- **Fuera de alcance:** Usuarios y Permisos, Deuda y Perfil siguen con `err.error?.mensaje ?? …`. Funciona igual, porque la API ahora siempre manda `mensaje`. No se migraron para no hacer un refactor masivo.

## 6. Tests

| # HU | Qué valida | Dónde |
|---|---|---|
| 1, 2, 9 | Respuesta `{ exito, mensaje }`, en español, 400 para validación, el mismo status en errores sin cuerpo | `MensajesEnEspanol.Tests.cs` |
| 3 | DTO de beneficio: sin datos y con fin antes que inicio | `MensajesEnEspanol.Tests.cs` (pipeline real de MVC) |
| 4 | DTO de alta de jugador: sin datos, DNI con puntos, varios errores juntos. Categoría inexistente sin id | `MensajesEnEspanol.Tests.cs`, `PlayersController.CreatePlayer.Tests.cs` |
| 5 | DTO de arancel: sin tipo y sin huella. Categoría inexistente sin id | `MensajesEnEspanol.Tests.cs`, `ArancelesService.Tests.cs` |
| 6 | DTO de arancel de inscripción y de pago de inscripción. Saldo en pesos sin depender de la cultura | `MensajesEnEspanol.Tests.cs`, `PlayerEnrollmentController.Tests.cs` |
| 7 | Superposición al asignar y al editar (vigente y programada) y al perder la carrera | `BeneficiosService.Tests.cs` |
| 8 | `dd/MM/yyyy` con `de-DE`, `en-US` y `nl-NL` | `BeneficiosService.Tests.cs`, `MensajesEnEspanol.Tests.cs` |
| Guardián | Por reflexión: todo DTO de entrada del portal tiene `ErrorMessage` en cada atributo y `[Required]` explícito en cada `string` no anulable. **Se verificó que falla** si se quita uno | `MensajesEnEspanol.Tests.cs` |
| Front | Mensaje de la API mostrado tal cual, fallback, cuerpo crudo, `[object Object]` y sin conexión | `http-error-message.spec.ts` |
| Front | 409 de superposición mostrado sin interpretar; 400; 409 sin mensaje; cuerpos inválidos | `becados-descuentos.spec.ts` |
| Front | Inscripción: mensaje de la API y cuerpo crudo nunca mostrado (**se verificó que falla con el código anterior**) | `cuotas-pagos.spec.ts` |
| Front | Aranceles: mensaje vacío, objeto o cuerpo crudo → texto propio | `actualizacion-aranceles.spec.ts` |

## 7. QA manual contra la API (pendiente de ejecutar)

Con un token de SuperAdmin. Anotar en cada fila el código, el mensaje obtenido, el idioma y el formato de fecha.

| # | Endpoint | Escenario | HTTP esperado | Mensaje esperado | Obtenido | Resultado |
|---|---|---|---|---|---|---|
| 1 | `POST /api/players/{id}/discount` | Cuerpo `{}` | 400 | Empieza con "Indicá el motivo de la bonificación." | | ☐ |
| 2 | `POST /api/players/{id}/discount` | `percentage: "abc"` | 400 | `El dato «porcentaje» no tiene un formato válido.` | | ☐ |
| 3 | `POST /api/players/{id}/discount` | Fin anterior al inicio | 400 | `La fecha de fin debe ser posterior a la de inicio.` | | ☐ |
| 4 | `POST /api/players/{id}/discount` | Se superpone con una vigente | **409** | `…la bonificación "<motivo>" vigente del dd/MM/yyyy al dd/MM/yyyy…` | | ☐ |
| 5 | `POST /api/players` | Cuerpo `{}` | 400 | "Indicá el nombre. Indicá el apellido. …" | | ☐ |
| 6 | `POST /api/players` | `dni: "12.345.678"` | 400 | `El DNI debe tener 7 u 8 dígitos, sin puntos.` | | ☐ |
| 7 | `POST /api/players` | `categoryId: 999` | 400 | `La categoría elegida no existe.` | | ☐ |
| 8 | `POST /api/aranceles/simular` | `tipo: null` | 400 | `Indicá el tipo de cambio del arancel.` | | ☐ |
| 9 | `POST /api/aranceles/programar` | Sin huella | 400 | `Revisá el impacto del cambio antes de confirmarlo.` | | ☐ |
| 10 | `POST /api/enrollmentfees` | Cuerpo `{}` | 400 | "Indicá a qué género corresponde la inscripción. …" | | ☐ |
| 11 | `POST /api/players/{id}/enrollment/payments` | Sin `paymentMethod` | 400 | `Indicá el método de pago.` | | ☐ |
| 12 | `POST /api/players/{id}/enrollment/payments` | Monto mayor al saldo | 400 | `El monto supera el saldo pendiente de la inscripción ($N.NNN).` | | ☐ |
| 13 | `POST /api/pagos/cobro` | `idsPago: []` | 400 | `Elegí al menos una cuota para cobrar.` | | ☐ |
| 14 | `GET /api/players/abc/discounts` | Id no numérico | 400 | `Uno de los valores indicados no es válido.` | | ☐ |
| 15 | `POST /api/integracion/v1/...` | JSON ilegible | 400 | Contrato `{ errorCode, message, field }` **sin cambios** | | ☐ |
| 16 | Pantalla Becados | Asignar superpuesta | — | Se ve el texto de la fila 4, sin cambios | | ☐ |
| 17 | Pantalla Cuotas y Pagos → Inscripción | Monto mayor al saldo | — | Toast con el texto de la fila 12 | | ☐ |
| 18 | Pantalla Aranceles | Arancel ya programado para esa fecha | — | Toast con la fecha en dd/MM/yyyy | | ☐ |

## 8. Pendientes y observaciones

- **QA manual (§7): BLOCKED.** Necesita la API, SQL Server y credenciales.
- **Falla preexistente:** `PlayersControllerVoidDiscountTests.AssignDiscount_StartingWithAnOldFee_IsRefused(-1)` por `ConnectionString` no inicializada. Ya fallaba antes de HU-096 y de HU-090 (verificado). Depende de la fecha.
- **Test ajustado:** `ArancelesService.Tests.ProgramarArancel_WithAnUnknownCategory_IsRefused` exigía que el id `999` apareciera en el mensaje. Ahora exige lo contrario, que es lo que pide la HU.
- **Reportes (PDF/Excel):** `OtrosReportBuilders` y `DeudoresReportBuilder` siguen con `ToString("dd/MM/yyyy")` sin cultura. No son mensajes de error y quedan fuera de alcance; conviene revisarlos en una HU de reportes.
- **Terminología:** se mantuvo "bonificación" (el término de la pantalla Becados y Descuentos) y el motivo real del catálogo ("Becado", "Descuento").
- Logs: siguen en inglés y no se tocaron.
