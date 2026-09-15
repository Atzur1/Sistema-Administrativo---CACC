# Pruebas de HU-012 — Vigencia de bonificaciones

Script que respalda la evidencia de `QA-HU-012.md`. Se corre a mano contra la
base de prueba; no forma parte de ninguna suite automática.

## Requisitos

- `ClubCamionerosPRUEBA` con las migraciones aplicadas, en este orden:
  1. `database/V20260912_01__recrear_descuentos_identity.sql`
  2. `database/V20260915_01__descuentos_catalogo_y_monto_fijo.sql`
  3. `database/V20260915_02__vigencia_obligatoria_bonificaciones.sql`
- `ApiGestion` levantada en `http://localhost:5118`.
- Un token en `/tmp/hu011_token.txt` (ver `tests/hu-011/README.md`).

## `test_api_hu012.sh`

Recorre los casos de la historia contra la API real:

- **Estado calculado por el servidor**: un rango pasado da `Expired`, uno que
  contiene hoy da `Active` y uno futuro da `Scheduled`. Verifica además que
  `isActive` se derive del estado y no viaje por su cuenta.
- **Vigencia obligatoria**: sin fecha desde, sin fecha hasta, o ambas vacías.
- **Rango estricto**: los dos ejemplos que la historia nombra, `10/10` a `10/10`
  y `10/10` a `09/10`, más el caso válido de un día de diferencia.
- **Superposición**: dos períodos que no se pisan se aceptan; uno que se pisa,
  incluso por un solo día, responde 409 nombrando el que está en el medio.
- **Caducidad automática**: se asigna un beneficio nuevo a un jugador cuyo
  beneficio anterior venció, **sin cancelarlo a mano**. Esto es lo que la regla
  anterior a HU-012 impedía.
- **Edición**: estirar un período hasta pisar al siguiente se rechaza; editar sin
  mover las fechas no choca contra sí mismo.
- **Regresión** de HU-014 y HU-011.

```bash
cd tests/hu-012
bash test_api_hu012.sh
```

Usa los jugadores 20 a 24 y sale con código 0 cuando los 35 casos pasan.

> **Importante sobre la limpieza.** El script cancela las bonificaciones que creó
> en lugar de borrarlas, porque eso es lo que hace la API: una bonificación no se
> elimina nunca, queda como historial. Después de varias corridas esos jugadores
> acumulan filas canceladas. No afecta a las pruebas, pero para dejar la base
> ordenada:
>
> ```sql
> SET QUOTED_IDENTIFIER ON;
> DELETE FROM JUGADORES_DESCUENTOS WHERE FK_id_jugador BETWEEN 20 AND 24;
> ```

## Datos de referencia

`database/seed_descuentos_prueba.sql` deja un escenario que cubre los cuatro
estados posibles:

| Jugador | Período | Estado |
|---|---|---|
| 1 a 4 | 2026 completo | Activa (la 4 es de monto fijo) |
| 5 | 2025 | Expirada |
| 6 | 2026, dada de baja | Cancelada |
| 7 | 2027 | Programada |
| 8 | ene–jun y jul–dic de 2026 | Expirada + Activa, dos períodos que no se pisan |

El jugador 8 es el caso que HU-012 habilitó: dos bonificaciones del mismo jugador
conviviendo porque sus rangos no se superponen.
