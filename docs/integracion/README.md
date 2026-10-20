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
| `404` | No existe el jugador. Este 404 **trae un mensaje** (`{ "exito": false, "mensaje": ... }`); un 404 sin cuerpo es una ruta que no existe. |
| `422` | El jugador está dado de baja, o su alta no es reciente (solo se generan cargos de altas de los últimos 7 días). |
| `401` / `403` | Sin token (o sesión cerrada) / el rol no puede. |

```json
{ "idJugador": 42, "inscripcionGenerada": true, "cuotaGenerada": true }
```
Dice **solo si** se generó cada cargo en esa llamada; nunca montos. `false` significa que ya existía, que no corresponde (por ejemplo, la inscripción de
las mujeres en $0) o que todavía no se generan las cuotas del mes.

**Si la llamada falla, el alta del Deportivo no se deshace.** La cuota sale con la próxima generación mensual (al iniciar la API del Administrativo y cada
6 horas) y la inscripción se genera al registrar el primer pago, con el monto correcto. Cuando el Administrativo vuelve, se puede repetir la llamada.

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

## Para quien cambie la API
1. El cambio compatible (agregar un campo opcional que no sea financiero) se hace en la misma versión; el que rompe, en `v2`.
2. Regenerar `openapi-v1.json` desde Swagger y actualizar este documento.
3. Avisar al equipo del Deportivo antes de mover la versión.
