# API de integración v1 con el Portal Deportivo

Contrato entre el Portal Administrativo (que la ofrece) y el Portal Deportivo (que la consume): el estado de habilitación de los jugadores (QR de ingreso y listados), las altas y cambios de categoría integrados (HU-056), los cargos de altas anteriores y la baja y reincorporación de jugadores (HU-057).
El Administrativo es la **única fuente de verdad** de lo que debe un jugador: aplica becas y descuentos, abonos parciales y el
vencimiento de la cuota el **día 10 del mes siguiente**. El Deportivo solo recibe **el estado**.

- **Documento OpenAPI:** [`openapi-v1.json`](openapi-v1.json). Con la API del Administrativo en Development se ve en
  Swagger: `/swagger` → "Integración con el Deportivo v1" (o directo en `/swagger/integracion-v1/swagger.json`).
- **Versionado:** las rutas llevan versión (`/api/integracion/v1/...`). Un cambio que rompa a quien la usa implica una versión nueva (`v2`).
  La ruta anterior, `/api/estado-deuda/...`, sigue funcionando como alias solo mientras el Deportivo migra y se retira después.
- **Base:** la URL del Portal Administrativo (en pruebas, `http://localhost:5118`).

## Qué se comparte y qué no
Las consultas de estado llevan **solamente el estado de habilitación**. Las respuestas de escritura informan el resultado y, para el alta, si se generó cada cargo; nunca incluyen montos, cantidad de cuotas, meses adeudados ni medios de pago.
Agregar cualquiera de esos datos es un cambio de contrato: hay una prueba que falla si aparece (`EstadoDeuda.Tests`).

## Autenticación
Cada pedido lleva **dos cosas**, y sin cualquiera de las dos responde `401`:

1. **La credencial del sistema Deportivo** (HU-055), en el header `X-Integration-Key`. Identifica al *sistema* que llama, no a una
   persona. La manda **solo el backend** del Portal Deportivo (`AdministrativoApi:ClaveIntegracion`); el navegador nunca la ve ni
   llama a estas rutas. Sola no sirve para nada: no es un usuario, no tiene roles y no abre ninguna otra ruta del Portal
   Administrativo. Se valida antes que todo lo demás.
2. **El mismo token (JWT)** de los dos portales: el Deportivo reenvía el `Authorization: Bearer ...` del usuario que está operando.
   Así cada consulta sigue ligada a una persona: se aplica su alcance por categoría, la auditoría sabe quién operó y cerrar sesión
   corta el acceso al instante.

Las escrituras quedan auditadas con las dos dimensiones: `origen_sistema = PortalDeportivo` y el usuario del token.
Configuración y rotación de la credencial: `database/InstructivoEquipo.md`, sección "Credencial de integración".
Cambio de esta versión (28/10/2026): hasta ahora bastaba el token; el backend del Deportivo tiene que empezar a mandar el header
(`cacc-unificacion/parches/ellos-credencial-integracion.patch`).

Pueden consultar: SuperAdmin del Administrativo, `Administrador (Tesorero)`, `Don QR`, `Coordinador`, `Médico`, `Director Técnico` y
`Preparador Físico`. **El Administrador del Administrativo (rol 2) no.**
- Ven a cualquier jugador: SuperAdmin, Administrador (Tesorero), Don QR, Coordinador y Médico.
- Director Técnico y Preparador Físico ven solo a los jugadores de las categorías de su token (claims `id_categoria`). Lo demás responde como "no encontrado".

## `GET /api/integracion/v1/estado-deuda/dni/{dni}`
El DNI puede venir con puntos (`41.000.001`).

| Respuesta | Cuándo |
|---|---|
| `200` | Hay un jugador con ese DNI. |
| `400` | El DNI no es válido (solo números, de 6 a 9 dígitos). |
| `404` | No es un jugador, o no es de tu alcance. |
| `409` | El DNI corresponde a más de un jugador. |
| `401` / `403` | Sin token (o sesión cerrada) / el rol no puede consultar. |

```json
{
  "dni": "41000001",
  "idJugador": 1,
  "estado": "solo_entrenamientos",
  "accesoPermitido": true,
  "fechaBaja": null
}
```

## `POST /api/integracion/v1/estado-deuda/lote`
Para los listados. Hasta 500 DNI por pedido.
```json
{ "dnis": ["41.000.001", "42000002"] }
```
Responde `{ "items": [ ...igual que arriba... ], "noEncontrados": ["42000002"] }`. En `noEncontrados` van los DNI inválidos,
los que no son de un jugador y los que están fuera del alcance de quien consulta.

## `POST /api/integracion/v1/jugadores`
El alta integrada crea la persona (si no existe), el jugador, su pertenencia inicial a la categoría y los cargos que correspondan, todo en una transacción. El alta se registra con las reglas financieras del Administrativo.

```json
{
  "dni": "41000001",
  "nombre": "Ana",
  "apellido": "Pérez",
  "genero": "Femenino",
  "fechaNacimiento": "2012-05-20",
  "idCategoria": 3
}
```

El DNI debe tener 7 u 8 dígitos, la misma regla que el alta del Portal Administrativo y que el Deportivo valida antes de llamar (el rango 6 a 9 de las consultas es el de las identidades que la base admite, no el de un jugador nuevo); `genero` debe ser `Masculino` o `Femenino`; `idCategoria` debe existir. Si la persona no existe, también hacen falta nombre, apellido y fecha de nacimiento (`yyyy-MM-dd`, no futura). Si ya existe, se reutiliza sin sobrescribir sus datos personales; nombre, apellido y fecha de nacimiento pueden omitirse. Una persona existente sin género lo completa con el del pedido; un género distinto se rechaza. Si ya hay un jugador activo, no se duplica: el pedido es idempotente y solo completa cargos de alta recientes que falten. Un jugador dado de baja no se reactiva por esta ruta.

| Respuesta | Cuándo |
|---|---|
| `201` | Se creó el jugador. |
| `200` | El jugador ya existía activo; no se duplicó y, si correspondía, se completaron cargos pendientes del alta reciente. |
| `400` | DNI, género, categoría o datos personales inválidos. |
| `409` | El jugador existente está dado de baja, el género no coincide o hay más de un jugador para la persona (requiere revisión manual). |
| `422` | La categoría no existe o falta el arancel de inscripción masculina vigente. |
| `401` / `403` | Falta credencial/token o el rol no puede operar. |

```json
{
  "idJugador": 42,
  "creado": true,
  "personaExistente": false,
  "inscripcionGenerada": true,
  "cuotaGenerada": true
}
```

Los cargos siguen las reglas vigentes: la inscripción se cobra solo si hay arancel positivo; la cuota del mes se emite desde el día 10, con prioridad del arancel de categoría sobre el de género. Sin arancel aplicable no se inserta un cargo en $0. Un alta nueva sin arancel de inscripción masculina se revierte completa.

## `PUT /api/integracion/v1/jugadores/{idJugador}/categoria`
Cambia la categoría de cobro (`JUGADORES.FK_id_categoria`) del jugador activo. El cuerpo es obligatorio:

```json
{ "idCategoria": 4 }
```

Responde `200` con `{ "idJugador": 42, "idCategoria": 4, "cambiado": true }`; repetir la categoría actual también responde `200` con `cambiado: false`. No modifica pagos ni sus importes históricos; asegura la nueva pertenencia en `JUGADORES_CATEGORIAS` sin quitar las anteriores.

| Respuesta | Cuándo |
|---|---|
| `400` | El id del jugador o de la categoría no es válido (`field`: `idJugador` o `idCategoria`). |
| `404` | No existe el jugador. |
| `422` | La categoría no existe o el jugador está dado de baja. |
| `401` / `403` | Falta credencial/token o el rol no puede operar. |

Ambas rutas de escritura requieren el header `X-Integration-Key`, el JWT del usuario y los roles SuperAdmin del Administrativo o `Administrador (Tesorero)`. Sus modificaciones quedan auditadas con origen `PortalDeportivo` y el operador del token. Los rechazos de negocio se registran en `INTEGRACION_RECHAZOS` sin DNI ni nombres.

## `POST /api/integracion/v1/jugadores/{idJugador}/cargos-alta`
Para completar los cargos de un jugador que una versión anterior del Portal Deportivo ya registró directamente en la base compartida, llama acá con el token del usuario para que
el Administrativo le genere su **inscripción** y su **cuota del mes** con las reglas de siempre (las de nuestro alta): la cuota solo desde el día 10
(antes la recibe con la generación mensual), con el arancel de su categoría o, si no, el de su género; la inscripción es la de su género y en $0 no
se cobra. No se duplica nada: se puede llamar más de una vez.

Pueden llamarla el SuperAdmin del Administrativo y el `Administrador (Tesorero)`; el resto de los roles recibe 403.

| Respuesta | Cuándo |
|---|---|
| `200` | Se procesó (aunque no hubiera nada nuevo para generar). |
| `400` | El id no es válido. |
| `404` | No existe el jugador. Este 404 **trae cuerpo** (`errorCode: "NOT_FOUND"`, ver [Errores](#errores)); un 404 sin cuerpo es una ruta que no existe. |
| `422` | El jugador está dado de baja, o su alta no es reciente (solo se generan cargos de altas de los últimos 7 días). |
| `401` / `403` | Sin token (o sesión cerrada) / el rol no puede. |

```json
{ "idJugador": 42, "inscripcionGenerada": true, "cuotaGenerada": true }
```
Dice **solo si** se generó cada cargo en esa llamada; nunca montos. `false` significa que ya existía, que no corresponde (por ejemplo, la inscripción de
las mujeres en $0) o que todavía no se generan las cuotas del mes.

**Si la llamada falla, el alta del Deportivo no se deshace.** La cuota sale con la próxima generación mensual (al iniciar la API del Administrativo y cada
6 horas) y la inscripción se genera al registrar el primer pago, con el monto correcto. Cuando el Administrativo vuelve, se puede repetir la llamada.

## `POST /api/integracion/v1/jugadores/{idJugador}/baja`
HU-057. Cuando un jugador deja el club, el Portal Deportivo lo informa acá (desde su backend, con la credencial de sistema y el token del usuario que
opera). El Administrativo registra la baja con sus reglas de siempre (las mismas que la baja desde su pantalla):
- **Es una baja lógica.** No se borra el jugador, ni la persona, ni sus pagos, ni su deuda, ni su auditoría. `JUGADORES.fecha_baja` guarda la fecha
  efectiva y `JUGADORES_PERIODOS` cierra su permanencia en el club.
- **Deja de recibir cuotas desde el mes siguiente al de la baja.** La cuota del mes de la baja le corresponde (estuvo en el club parte del mes, mismo
  criterio que el alta). Ningún camino de emisión (generación mensual, cuotas faltantes, cobro por adelantado, aranceles nuevos) le genera cuotas posteriores.
- **La deuda anterior no se perdona:** sigue en Deudas y Morosidad y en `estado-deuda` hasta que se cobre.
- Si ya tenía emitida, **sin ningún pago**, la cuota de un mes posterior al de la baja (por ejemplo, una baja que se informa tarde o un mes adelantado),
  esa cuota se quita: no es deuda real. Las que tienen algún pago quedan.
- Las becas y descuentos vigentes o programados se cancelan con la fecha de baja (siguen aplicando a los meses anteriores). Si vuelve, no se restauran solos.

Pueden llamarla el SuperAdmin del Administrativo y el `Administrador (Tesorero)`; el resto de los roles recibe 403.
```json
{ "fechaBaja": "2026-10-15" }
```
`fechaBaja` es **obligatoria** y es la fecha **efectiva** en que el jugador dejó el club (`yyyy-MM-dd`), no la del pedido: así un reintento al día siguiente
sigue siendo el mismo pedido.

| Respuesta | Cuándo |
|---|---|
| `200` | Se registró la baja, **o** el pedido repite una baja ya registrada con la misma fecha (`yaEstabaDadoDeBaja: true`, no se cambió nada). |
| `400` | El id no es válido, falta la fecha, no tiene formato `yyyy-MM-dd` o es futura (`field`: `idJugador` o `fechaBaja`). |
| `404` | No existe el jugador. |
| `409` | El jugador ya está dado de baja **con otra fecha**: no se pisa la baja registrada (el mensaje dice desde cuándo). |
| `422` | La fecha es anterior a su ingreso al club (su fecha de alta o, si no la tiene, el inicio de su permanencia en `JUGADORES_PERIODOS`). |
| `401` / `403` | Sin credencial de sistema, sin token (o sesión cerrada) / el rol no puede. |

```json
{ "idJugador": 42, "estado": "baja", "fechaBaja": "2026-10-15", "yaEstabaDadoDeBaja": false }
```
Nunca lleva montos ni cuántas cuotas se quitaron. **Reintentos:** si se pierde la respuesta, repetir el mismo pedido es seguro: responde `200` con
`yaEstabaDadoDeBaja: true`. Dos pedidos simultáneos dejan una sola baja (se ordenan con el mismo bloqueo que la generación de cuotas).

## `POST /api/integracion/v1/jugadores/{idJugador}/reincorporacion`
HU-057. Cuando un jugador dado de baja vuelve al club. Es un **reingreso** del mismo jugador: se reutilizan su `idJugador` y su persona, y conserva todo
su historial (pagos, deuda anterior, auditoría). **No hay que llamar a `cargos-alta` después:** la reincorporación ya genera la cuota y la
inscripción que correspondan. `cargos-alta` vería un alta de hoy y, a quien nunca tuvo una inscripción, le generaría una que el reingreso decidió no cobrar.
- Su **fecha de alta pasa a ser hoy** y `JUGADORES_PERIODOS` abre una permanencia nueva (la anterior, con su baja, queda).
- **Los meses en que estuvo de baja no se le cobran** nunca, ni ahora ni con la generación de cuotas faltantes.
- **Cuota del mes**, con el calendario de siempre: si vuelve **desde el día 10**, se le emite en el momento (si no la tenía ya); si vuelve **antes del 10**,
  se la emite la generación mensual el día 10, como a todos.
- **Inscripción:** si vuelve dentro del año de su última inscripción no la paga de nuevo; si ese año terminó, empieza uno nuevo (ver "La inscripción es anual").
- Las becas canceladas por la baja no se restauran: si corresponde, el Administrativo asigna una nueva.

```json
{ "idCategoria": 4 }
```
El cuerpo es **opcional**. Con `idCategoria`, esa pasa a ser su categoría de cobro (`JUGADORES.FK_id_categoria`): rige para la cuota que se emita en
el momento y las siguientes; lo ya emitido conserva su monto. Se asegura además su pertenencia a esa categoría en `JUGADORES_CATEGORIAS`; las
pertenencias anteriores son del Deportivo y no se tocan (igual que en el cambio de categoría). Sin cuerpo, conserva la categoría que tenía.

| Respuesta | Cuándo |
|---|---|
| `200` | Se reincorporó. |
| `400` | El id o la categoría no son válidos (`field`: `idJugador` o `idCategoria`). |
| `404` | No existe el jugador. |
| `409` | El jugador ya está activo (también si se repite una reincorporación que ya se hizo: el jugador quedó activo). |
| `422` | La categoría no existe. |
| `401` / `403` | Sin credencial de sistema, sin token (o sesión cerrada) / el rol no puede. |

```json
{ "idJugador": 42, "fechaAlta": "2026-11-03", "categoriaCambiada": true }
```

## La inscripción es anual
Cada jugador paga **una inscripción por año**. Su año empieza el día en que se le asignó la inscripción (su alta) y se renueva sola en cada aniversario, con el arancel de
inscripción de su género que rige ese día (con $0 o sin arancel no se cobra). La emite el mismo proceso automático que genera las cuotas (al iniciar la API y cada 6 horas),
solo para jugadores activos. No hay una fecha única para todo el club: cada jugador tiene la suya.
- **Baja:** sigue cubierto hasta que termina su año y no se le asigna la siguiente.
- **Reingreso:** si vuelve dentro de su año no la paga de nuevo; si ese año ya terminó, empieza uno nuevo ese día con el arancel vigente.
- **Sin pagar:** si el año anterior quedó impago y llega el aniversario, conviven las dos; se cobra primero la más vieja, como las cuotas.
- **API apagada:** la inscripción se emite hasta 31 días después del aniversario; pasado ese plazo ese año no se cobra y la siguiente sale al año siguiente.
- Cada inscripción queda en `PAGOS` con `fecha_vencimiento` = el día en que empieza su año, que la identifica.
- Para el Portal Deportivo no cambia el contrato: `cargos-alta` genera la del primer año y nada más.

## Inscripción que empieza a regir después de que el jugador entró
Si un jugador entra cuando su género todavía no tiene inscripción en vigencia (nunca se cargó, o está en $0) pero **ya hay una programada con fecha futura**,
el Administrativo se la genera **el día en que empieza a regir**, como deuda pendiente con vencimiento ese día. Lo hace el proceso automático que ya emite las
cuotas (al iniciar la API y cada 6 horas), sin que el Deportivo haga nada.
- **Quién la recibe:** quien entró desde el día en que se cargó ese arancel hasta el día en que rige, y todavía no tiene ninguna inscripción (pendiente, pagada ni anulada).
  Quien ya estaba antes de que se cargara no recibe ningún cobro (por ejemplo, las jugadoras que ya estaban cuando la inscripción femenina pasó de $0 a un monto).
- **Quién no:** los jugadores dados de baja y las inscripciones de $0.
- **Si la API estuvo caída:** también se miran las inscripciones anteriores que empezaron a regir en los últimos 31 días. Quien entró mientras una estaba
  programada la paga (con su monto y el vencimiento de su primer día) aunque otra más nueva ya rija hoy.
- **Importante:** el momento en que se cargó cada inscripción sale de la **auditoría**. Una inscripción cargada fuera de la pantalla de Actualización de
  aranceles (por ejemplo, un `INSERT` directo sin identificar al usuario) no deja ese registro y no genera nada con esta regla.
- Es idempotente: repetir el proceso no duplica nada. Para el Deportivo no cambia el contrato: `cargos-alta` sigue respondiendo lo mismo.

## Campos
| Campo | Qué es |
|---|---|
| `dni` | DNI del jugador, solo dígitos. |
| `idJugador` | Identificador del jugador en la base compartida. |
| `estado` | `habilitado`, `solo_entrenamientos` (puede entrenar, no jugar partidos oficiales), `inhabilitado` o `baja` (el jugador se fue del club). |
| `accesoPermitido` | `true` en `habilitado` y `solo_entrenamientos`. Es lo que decide el semáforo del QR. |
| `fechaBaja` | Fecha de la baja (`yyyy-MM-dd`) si está dado de baja; si no, `null`. |

Cómo se llega a cada estado (lo decide el Administrativo, el Deportivo no lo calcula): 0 cuotas vencidas = `habilitado`, 1 = `solo_entrenamientos`,
2 o más = `inhabilitado`; con baja, `baja`.

## Cómo lo usa el QR del Deportivo
Llama a `GET /api/integracion/v1/estado-deuda/dni/{dni}` con el token del usuario y usa `accesoPermitido` y `estado`: verde si `habilitado`,
amarillo si `solo_entrenamientos`, rojo si `inhabilitado` o `baja`. Sigue siendo el Deportivo quien registra la asistencia.
**Si esta API no responde, el QR no deja pasar a nadie** (decisión del club; `AdministrativoApi:SiNoResponde = Denegar`).

## Errores
Todo error de la API de integración tiene el mismo cuerpo:
```json
{ "errorCode": "INVALID_INPUT", "message": "Indicá un DNI válido (solo números, de 6 a 9 dígitos).", "field": "dni",
  "exito": false, "mensaje": "Indicá un DNI válido (solo números, de 6 a 9 dígitos)." }
```
- `errorCode`: estable, para decidir por código. `message`: en español, para mostrar. `field`: solo en los `400`, el dato que no es válido (`dni`, `dnis`, `dnis[3]`, `idJugador`, `fechaBaja`, `idCategoria`).
- `exito` y `mensaje` (igual a `message`) se mantienen por compatibilidad con la v1. Lo nuevo es un agregado: quien ya leía `mensaje` no tiene que cambiar nada.

| Código HTTP | `errorCode` | Cuándo |
|---|---|---|
| `400` | `INVALID_INPUT` | Un dato inválido, o un cuerpo que falta o no es JSON válido. |
| `401` | `UNAUTHORIZED` | Falta la credencial del sistema (`X-Integration-Key`) o no es válida; o falta el token, está vencido o la sesión se cerró. Nunca trae datos. |
| `403` | `FORBIDDEN` | El rol no puede usar ese endpoint. |
| `404` | `NOT_FOUND` | El jugador o el DNI no existen. Sin cuerpo: la ruta no existe. |
| `405` / `415` | `METHOD_NOT_ALLOWED` / `UNSUPPORTED_MEDIA_TYPE` | Método HTTP equivocado / el cuerpo no es `application/json`. |
| `409` | `CONFLICT` | El DNI corresponde a más de un jugador; la baja ya está registrada con otra fecha; el jugador a reincorporar ya está activo. |
| `422` | `UNPROCESSABLE` | La regla de negocio no lo permite (jugador dado de baja, alta que no es reciente, baja anterior al ingreso al club, categoría que no existe). |
| `429` | `TOO_MANY_REQUESTS` | Se superó el cupo de consultas simultáneas de integración. Reintentar en unos segundos. |
| `500` | `INTERNAL_ERROR` | Falla inesperada del Administrativo. |
| `503` | `SERVICE_UNAVAILABLE` | La base de datos no responde (caída o timeout). Reintentar más tarde. |

**Aislamiento.** Las consultas de integración comparten un cupo de pedidos simultáneos, aparte del resto del Portal Administrativo
(`Integracion:ConsultasSimultaneas`, por defecto 8, y `Integracion:ConsultasEnCola`, por defecto 32). Un pico de consultas del Deportivo
recibe `429`, pero no deja sin conexiones a la base a las pantallas de cobranzas. Una falla en un endpoint de integración siempre termina
en una de las respuestas de arriba: nunca afecta a otra pantalla.

**Registro.** Cada error queda en el log de la API con fecha y hora (`Timestamp`, UTC), endpoint (la plantilla de la ruta, por ejemplo
`GET api/integracion/v1/estado-deuda/dni/{dni}`, nunca el valor) y motivo. Antes de escribirse, el motivo pasa por `LogSanitizer`:
nunca se registran tokens, credenciales, correos ni DNI.

## Para quien cambie la API
1. El cambio compatible (agregar un campo opcional que no sea financiero) se hace en la misma versión; el que rompe, en `v2`.
2. Regenerar `openapi-v1.json` desde Swagger y actualizar este documento.
3. Avisar al equipo del Deportivo antes de mover la versión.
