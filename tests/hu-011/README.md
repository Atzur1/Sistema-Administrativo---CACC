# Pruebas de HU-011 — Bonificaciones

Scripts que respaldan la evidencia de `QA-HU-011.md`. Se corren a mano contra la
base de prueba; no forman parte de ninguna suite automática.

## Requisitos

- `ClubCamionerosPRUEBA` con las migraciones aplicadas, en este orden:
  1. `database/V20260912_01__recrear_descuentos_identity.sql`
  2. `database/V20260915_01__descuentos_catalogo_y_monto_fijo.sql`
- Para las pruebas de API, `ApiGestion` levantada en `http://localhost:5118`.

Ajustá la instancia de SQL Server en los comandos: en el equipo donde se corrió
esta QA era `(localdb)\MSSQLLocalDB`.

## `test_constraints.sql`

Verifica que las reglas del modelo se sostienen a nivel de motor: una sola
bonificación activa por jugador, exclusividad entre porcentaje y monto fijo, y
los rangos de ambos. Todo corre dentro de una transacción con `ROLLBACK` final,
así que **no deja datos**.

```bash
cd tests/hu-011
sqlcmd -S "(localdb)\MSSQLLocalDB" -d ClubCamionerosPRUEBA -E -C -i test_constraints.sql
```

Los nueve casos deben imprimir `OK` o `BLOQUEADO correctamente`.

## `test_api_hu011.sh`

Recorre los casos de la historia contra la API real: alta porcentual, alta de
monto fijo, validaciones, duplicidad, edición y cancelación. Cada alta se
verifica releyendo con un `GET`, no con lo que devolvió el `POST`.

Necesita un token en `/tmp/hu011_token.txt`. Para obtenerlo, logueate con un
usuario activo:

```bash
curl -s -X POST http://localhost:5118/api/Auth/login \
  -H "Content-Type: application/json" \
  -d '{"usuario":"<email>","contrasena":"<clave>"}' \
  | node -e "let d='';process.stdin.on('data',c=>d+=c).on('end',()=>console.log(JSON.parse(d).token))" \
  > /tmp/hu011_token.txt

cd tests/hu-011
bash test_api_hu011.sh
```

Usa los jugadores 10 a 15 y limpia sus bonificaciones al empezar y al terminar.
Sale con código 0 cuando los 24 casos pasan.

## `seed_regresion_hu014.sql`

Arma los tres escenarios que documenta `QA-HU-014.md` sobre el modelo nuevo
—bonificación vigente, vencida y dada de baja, en los jugadores 101, 102 y 103—
para comprobar que aquella historia sigue respondiendo lo mismo que antes.

```bash
cd tests/hu-011
sqlcmd -S "(localdb)\MSSQLLocalDB" -d ClubCamionerosPRUEBA -E -C -i seed_regresion_hu014.sql
```

Después, sin token:

| Petición | Esperado |
|---|---|
| `GET /api/Players/101/discount` | 200 |
| `GET /api/Players/102/discount` | 404 (vencida) |
| `GET /api/Players/103/discount` | 404 (dada de baja) |
| `GET /api/Players/102/discount?includeExpired=true` | 200 con `isActive: false` |

Este script **sí deja datos**. Para limpiarlos:

```sql
SET QUOTED_IDENTIFIER ON;
DELETE FROM JUGADORES_DESCUENTOS WHERE FK_id_jugador IN (101, 102, 103);
```

> El `SET QUOTED_IDENTIFIER ON` no es decorativo: el índice filtrado
> `UX_JUGDESC_UNA_ACTIVA` lo exige para cualquier escritura sobre la tabla, y
> `sqlcmd` lo trae apagado.
