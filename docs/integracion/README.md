# API de integración v1 con el Portal Deportivo

Contrato entre el Portal Administrativo (que la ofrece) y el Portal Deportivo (que la consume): el estado de habilitación de los jugadores (QR de ingreso y listados) y los cargos de alta de los jugadores nuevos.
El Administrativo es la **única fuente de verdad** de lo que debe un jugador: aplica becas y descuentos, abonos parciales y el
vencimiento de la cuota el **día 10 del mes siguiente**. El Deportivo solo recibe **el estado**.

- **Documento OpenAPI:** [`openapi-v1.json`](openapi-v1.json). Con la API del Administrativo en Development se ve en
  Swagger: `/swagger` → "Integración con el Deportivo v1" (o directo en `/swagger/integracion-v1/swagger.json`).
- **Versionado:** las rutas llevan versión (`/api/integracion/v1/...`). Un cambio que rompa a quien la usa implica una versión nueva (`v2`).
  La ruta anterior, `/api/estado-deuda/...`, sigue funcionando como alias solo mientras el Deportivo migra y se retira después.
- **Base:** la URL del Portal Administrativo (en pruebas, `http://localhost:5118`).

## Qué se comparte y qué no
La respuesta lleva **solamente el estado de habilitación**. Nunca incluye montos, cantidad de cuotas, meses adeudados ni medios de pago.
Agregar cualquiera de esos datos es un cambio de contrato: hay una prueba que falla si aparece (`EstadoDeuda.Tests`).

## Autenticación
El **mismo token (JWT)** de los dos portales: el Deportivo reenvía el `Authorization: Bearer ...` del usuario que está operando.
Se eligió así, en lugar de una credencial de sistema, porque cada consulta queda ligada a una persona: se aplica su alcance por categoría,
la auditoría sabe quién preguntó y cerrar sesión corta el acceso al instante. Si algún día hace falta un consumidor sin persona detrás
(un proceso nocturno), se agrega una credencial de servicio aparte sin tocar esto.

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

## `POST /api/integracion/v1/jugadores/{idJugador}/cargos-alta`
Cuando el Portal Deportivo da de alta a un jugador lo graba directo en la base compartida. Después llama acá, con el token del usuario, para que
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
- `errorCode`: estable, para decidir por código. `message`: en español, para mostrar. `field`: solo en los `400`, el dato que no es válido (`dni`, `dnis`, `dnis[3]`, `idJugador`).
- `exito` y `mensaje` (igual a `message`) se mantienen por compatibilidad con la v1. Lo nuevo es un agregado: quien ya leía `mensaje` no tiene que cambiar nada.

| Código HTTP | `errorCode` | Cuándo |
|---|---|---|
| `400` | `INVALID_INPUT` | Un dato inválido, o un cuerpo que falta o no es JSON válido. |
| `401` | `UNAUTHORIZED` | Sin token, vencido o con la sesión cerrada. |
| `403` | `FORBIDDEN` | El rol no puede usar ese endpoint. |
| `404` | `NOT_FOUND` | El jugador o el DNI no existen. Sin cuerpo: la ruta no existe. |
| `405` / `415` | `METHOD_NOT_ALLOWED` / `UNSUPPORTED_MEDIA_TYPE` | Método HTTP equivocado / el cuerpo no es `application/json`. |
| `409` | `CONFLICT` | El DNI corresponde a más de un jugador. |
| `422` | `UNPROCESSABLE` | La regla de negocio no lo permite (jugador dado de baja, alta que no es reciente). |
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
