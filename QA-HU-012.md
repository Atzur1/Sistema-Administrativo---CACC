# QA — HU-012: Configuración y validación de fechas de vigencia para bonificaciones

Evidencia formal de las pruebas de HU-012, como parte de su Definición de Listo.
Pruebas corridas sobre `ClubCamionerosPRUEBA` en `(localdb)\MSSQLLocalDB`, el
2026-09-15, con la API en `http://localhost:5118`.

HU-012 se apoya en lo que dejaron HU-014 (lectura y etiquetado) y HU-011
(persistencia, formulario y bonificación única). Lo que agrega es la vigencia
obligatoria, el estado calculado y la caducidad automática.

---

## Cambio de regla de negocio

Este es el punto de fondo de la historia y fue **acordado con el Product Owner
antes de implementar**. Queda asentado acá porque cambia una regla que estaba
vigente desde HU-011.

**Antes:** un jugador no podía tener dos bonificaciones abiertas. Lo garantizaba
el índice filtrado `UX_JUGDESC_UNA_ACTIVA` sobre `estado_activo = 1`.

**El problema que apareció al implementar HU-012:** ese índice no mira las
fechas. Una bonificación vencida en 2025 seguía ocupando el lugar del jugador en
2026, de modo que para asignarle una nueva había que cancelar la anterior a
mano. Eso es exactamente la intervención manual que la historia pide eliminar.
Tampoco permitía dejar programada una bonificación para el año siguiente
mientras corría la de este año.

Verificado sobre la base antes de tocar nada:

```
Jugador 5 tiene una bonificacion EXPIRADA (2025-01-01 a 2025-12-31, estado_activo=1).
Hoy es 2026-09-15, o sea que ya caduco.
Intento asignarle una NUEVA bonificacion vigente para 2026...

>>> BLOQUEADO POR: Cannot insert duplicate key row in object
    'dbo.JUGADORES_DESCUENTOS' with unique index 'UX_JUGDESC_UNA_ACTIVA'.
```

**No se podía corregir el índice.** SQL Server exige que la condición de un
índice filtrado sea determinista, y `GETDATE()` no lo es: un "índice de
vigentes" no se puede declarar.

**Después:** un jugador puede acumular bonificaciones a lo largo del tiempo
—su historial, la que corre hoy y las que estén programadas— siempre que **los
rangos no se superpongan**. En cualquier fecha dada sigue habiendo una sola
bonificación aplicable, que es la intención original de la regla.

**Costo asumido:** la unicidad deja de estar garantizada por el motor y pasa a
validarse en `DiscountDao`, buscando superposición dentro de la misma
transacción que escribe, con `UPDLOCK` y `HOLDLOCK` sobre el rango consultado y
nivel de aislamiento `Serializable`. Eso bloquea el rango leído y no solo las
filas encontradas, así que dos altas simultáneas no pueden verlo libre las dos.

---

## Cambios de modelo

Migración: `database/V20260915_02__vigencia_obligatoria_bonificaciones.sql`

| | Antes (HU-011) | Después (HU-012) |
|---|---|---|
| `fecha_inicio` / `fecha_fin` | nullable | **NOT NULL** |
| `CK_JUGDESC_VIGENCIA` | `fecha_fin >= fecha_inicio` | **`fecha_fin > fecha_inicio`** |
| `UX_JUGDESC_UNA_ACTIVA` | índice único filtrado | **eliminado** |
| `IX_JUGDESC_JUGADOR_ESTADO` | `(jugador, estado)` | reemplazado por `IX_JUGDESC_JUGADOR_VIGENCIA` `(jugador, estado, inicio, fin)` |

**No se crearon columnas nuevas.** `fecha_inicio` y `fecha_fin` ya existían en
`JUGADORES_DESCUENTOS` desde HU-011 y se reutilizaron tal cual.

**Datos previos:** dos asignaciones (jugadores 3 y 4) tenían `fecha_fin` en NULL.
La migración las cerró al 31 de diciembre del año de inicio, criterio acordado
antes de ejecutarla. No se perdió ningún beneficio.

---

## Estado calculado, no almacenado

El estado no se guarda en ninguna columna. Se resuelve en cada lectura, en SQL,
contra `GETDATE()`:

```sql
CASE
    WHEN jd.estado_activo = 0 THEN 3   -- Cancelled
    WHEN CAST(GETDATE() AS DATE) < jd.fecha_inicio THEN 0   -- Scheduled
    WHEN CAST(GETDATE() AS DATE) > jd.fecha_fin THEN 2      -- Expired
    ELSE 1                                                   -- Active
END
```

De ahí salen las tres consecuencias que pedía la historia:

- **No hace falta una tarea diaria.** Una bonificación pasa a Expirada el día que
  le corresponde, sola, porque nadie guardó lo contrario.
- **No hay botón de "marcar como expirada".** No existe nada que marcar.
- **No hay flags que se desincronicen.** `isActive` del contrato viejo se deriva
  de `Status` en el mapper y no tiene setter propio en la entidad, así que es
  imposible que digan cosas distintas.

**Cuarto estado, `Cancelled`.** La historia describe tres, pero el modelo ya
tenía la baja manual (`estado_activo = 0`) y esas filas no se borran porque
`PAGOS` las referencia. Exponer `estado_activo` al lado de `status` habría
devuelto justamente los dos campos que el cliente tiene que combinar. Se resolvió
con un único campo de cuatro valores, donde la cancelación gana sobre las fechas.

---

## Hallazgo: zona horaria institucional

**El proyecto no define ninguna zona horaria.** Lo único que existe es
`"language": "Spanish (Argentina)"` en `openspec/sdd-init.json` y en `AGENTS.md`.
No hay `TimeZoneInfo`, ni configuración en `appsettings`, ni nada equivalente.

Siguiendo lo que indica la propia historia para este caso, **no se hardcodeó
ninguna zona**. Se usa de forma consistente el reloj del servidor de base de
datos (`GETDATE()`), que es lo que ya venía usando `DiscountDao` desde HU-014 y
también `PaymentDao`. El backend es la única fuente de verdad: el navegador nunca
decide el estado de una bonificación.

El único `new Date()` que queda en el frontend precarga las fechas del formulario
—de hoy a fin de año— y es editable. No participa de ninguna decisión.

**Queda pendiente que el equipo defina la zona institucional.** Mientras el
servidor de base de datos y el de aplicación corran en la misma máquina, o en la
misma zona, esto no cambia nada. Si algún día se separan, hay que revisarlo.

---

## Pruebas unitarias (xUnit)

Comando: `dotnet test ApiGestion.Tests/ApiGestion.Tests.csproj`

| Suite | Qué cubre | Resultado |
|---|---|---|
| `DiscountRequestDTOTests` | Motivo, tipo de valor, rangos, exclusividad, **fechas obligatorias, formato y rango estricto** | 34 tests ✅ |
| `PlayersControllerDiscountsTests` | `playerId` no positivo en los cuatro verbos | 9 tests ✅ |
| Suites preexistentes (HU-015 / HU-016) | Regresión de cobros | 33 tests ✅ |

**Resultado: 76 tests, 0 fallos.**

Tres pruebas de HU-011 cambiaron de signo porque cambió la regla, y eso es
correcto: `EndDate_Missing_IsValid` pasó a `EndDate_Missing_IsInvalid`,
`EndDate_SameAsStartDate_IsValid` pasó a `IsInvalid`, y
`Dates_Blank_CountAsNotSent` se eliminó.

---

## Pruebas de integración (API + SQL Server)

Script: `tests/hu-012/test_api_hu012.sh`

### Estado resuelto por el servidor

| Caso | Rango | Esperado | Obtenido | Estado |
|---|---|---|---|---|
| Rango enteramente pasado | 2020-01-01 a 2020-12-31 | `Expired`, `isActive: false` | `Expired`, `false` | ✅ |
| Rango que contiene hoy | ayer a mañana | `Active`, `isActive: true` | `Active`, `true` | ✅ |
| Rango enteramente futuro | 2030-01-01 a 2030-12-31 | `Scheduled`, `isActive: false` | `Scheduled`, `false` | ✅ |

### Vigencia obligatoria

| Caso | Esperado | Obtenido | Estado |
|---|---|---|---|
| Sin fecha desde | 400 | 400 | ✅ |
| Sin fecha hasta | 400 | 400 | ✅ |
| Ambas vacías | 400 | 400 | ✅ |

### Rango estricto — los dos ejemplos de la historia

| Caso | Esperado | Obtenido | Estado |
|---|---|---|---|
| `10/10/2026` a `10/10/2026` (iguales) | 400 | 400, *"The end date must be later than the start date."* | ✅ |
| `10/10/2026` a `09/10/2026` (invertidas) | 400 | 400 | ✅ |
| `10/10/2026` a `11/10/2026` (un día) | 201 | 201 | ✅ |
| Formato inválido (`2026-13-01`) | 400 | 400 | ✅ |

### Superposición de períodos

| Caso | Esperado | Obtenido | Estado |
|---|---|---|---|
| Primer beneficio ene–jun | 201 | 201 | ✅ |
| Segundo jul–dic, no se pisa | 201 | 201 | ✅ |
| Tercero jun–ago, se pisa con ambos | 409 | 409, *"already has a benefit for that period: Media Beca from 2026-01-01 to 2026-06-30"* | ✅ |
| Solapa por un solo día (borde) | 409 | 409 | ✅ |
| Cuarto en 2027, contiguo | 201 | 201 | ✅ |

### Caducidad automática

Este es el caso que la regla anterior impedía:

| Caso | Esperado | Obtenido | Estado |
|---|---|---|---|
| Beneficio ya expirado (2024) | 201 | 201 | ✅ |
| Nuevo beneficio vigente **sin cancelar el anterior** | 201 | 201 | ✅ |
| El nuevo queda Activo | `Active` | `Active` | ✅ |

### Edición

| Caso | Esperado | Obtenido | Estado |
|---|---|---|---|
| Estirar un beneficio hasta pisar al siguiente | 409 | 409 | ✅ |
| Editar sin tocar el rango (no choca consigo mismo) | 200 | 200, porcentaje persistido en 75 | ✅ |

### Regresión de HU-014 y HU-011

| Caso | Esperado | Obtenido | Estado |
|---|---|---|---|
| `GET /discounts` | 200, solo vigentes | 200 | ✅ |
| `GET /discounts/all` | 200 | 200 | ✅ |
| Jugador con bonificación expirada | 404 | 404 | ✅ |
| Jugador con bonificación cancelada | 404 | 404 | ✅ |
| Jugador con bonificación vigente | 200, `status: Active` | 200, `Active` | ✅ |
| Id inválido | 400 | 400 | ✅ |
| Escritura sin token | 401 | 401 | ✅ |

**Resultado: 35/35 en verde.**

---

## Bug encontrado y corregido durante la QA

Las pruebas de integración detectaron que el `POST` devolvía la bonificación
equivocada. Al asignar una programada a un jugador que ya tenía una vigente, la
respuesta traía la vigente en lugar de la recién creada, porque releía con
`GetAssignedDiscountByPlayer`, que prioriza la que aplica hoy.

Corregido: después de escribir, la relectura se hace **por id**. El mismo arreglo
se aplicó a la edición. Sin la prueba, el formulario habría mostrado un beneficio
distinto del que se acababa de guardar.

---

## Compilación

| Proyecto | Comando | Resultado |
|---|---|---|
| Backend | `dotnet test ApiGestion.Tests/ApiGestion.Tests.csproj` | ✅ 76/76 |
| Frontend (tipos) | `npx tsc --noEmit -p tsconfig.app.json` | ✅ exit 0 |
| Frontend (plantillas AOT) | `npx ngc -p tsconfig.app.json --rootDir ./src` | ✅ exit 0 |

---

## Criterios de aceptación — estado final

| Criterio | Estado | Evidencia |
|---|---|---|
| Fecha Desde y Fecha Hasta obligatorias | ✅ | `NOT NULL` en la tabla, `[Required]` en el DTO, `Validators.required` en el formulario. Integración: tres casos en 400 |
| No se puede registrar sin ambas | ✅ | Validado en los tres niveles |
| Las fechas persisten junto a la asignación | ✅ | Viven en `JUGADORES_DESCUENTOS`, columnas reutilizadas |
| Estado Programada / Activa / Expirada | ✅ | `StatusExpression` en el DAO; verificados los tres |
| El estado no depende de un campo manual | ✅ | No se almacena. No hay botón ni tarea programada |
| Formulario con motivo, tipo, valor y fechas | ✅ | Popup de `becados-descuentos` |
| Fecha Hasta estrictamente posterior | ✅ | `CK_JUGDESC_VIGENCIA`, DTO y validador del formulario |
| Mensaje inmediato al romper el rango | ✅ | *"La fecha de finalización debe ser posterior a la fecha de inicio."* Se muestra sin esperar el submit |
| Guardar deshabilitado con rango inválido | ✅ | `[disabled]="benefitForm.invalid \|\| saving()"` |
| Validación reactiva al cambiar cualquiera de los dos | ✅ | El validador vive en el grupo, no en un campo |
| Mismas reglas en backend | ✅ | 400 ante cualquier violación, aunque se llame por curl |
| Estado devuelto por la API | ✅ | Campo único `status` |
| Un solo estado, sin flags sueltos | ✅ | `isActive` derivado de `status`, sin setter |
| Vigencia visible en la ficha | ✅ | "01/10/2026 - 31/12/2026" |
| Badges consistentes con el diseño | ✅ | `status-pill` reutilizada; se agregaron `status-scheduled` y `status-cancelled` |
| Toast de confirmación | ✅ | Componente compartido, extraído del de HU-015 / HU-016 |
| No mostrar éxito antes de la respuesta | ✅ | El toast se dispara en el `next` del observable |
| Expiración automática | ✅ | Caso verificado: se asignó un beneficio nuevo sin cancelar el vencido |
| Interacción con la regla de unicidad | ✅ | Documentada arriba y acordada antes de implementar |

---

## Pendiente para cerrar la Definición de Listo

- **Pruebas manuales de interfaz en el navegador: NO EJECUTADAS.** Node.js
  v24.14.0 está por debajo del mínimo que pide Angular CLI (v24.15.0), así que
  `ng serve` no arranca. El frontend se verificó por compilación de tipos y de
  plantillas en AOT, que **no reemplaza probar el flujo a mano**. Quedan sin
  verificar en pantalla: el mensaje inmediato al romper el rango, el botón
  deshabilitado, las etiquetas de estado, el historial dentro del popup y el
  toast.
- **`cuotas-pagos` sigue con su propia copia del toast.** El componente
  compartido `shared/toast/` se creó a partir de ese código, pero migrar esa
  pantalla implica tocar una historia que tiene pruebas propias
  (`cuotas-pagos.spec.ts`) que no se pueden ejecutar en este entorno. Se dejó
  como está a propósito. Cuando se pueda correr `ng test`, debería migrarse.
- Actualizar la colección de Postman con `status`, el parámetro `includeExpired`
  y el endpoint `GET /{playerId}/discounts`.
- Definir la zona horaria institucional (ver Hallazgo).

---

## Seguridad de la historia

- Las fechas viajan siempre como `SqlParameter`, nunca concatenadas en el SQL.
- Se mantuvo ADO.NET puro con `Microsoft.Data.SqlClient`. No se incorporó ORM.
- Las tres operaciones de escritura siguen exigiendo `[Authorize]`.
- La validación de superposición corre dentro de la transacción que escribe, no
  antes: no hay ventana entre la comprobación y el `INSERT`.
- No se tocaron las vulnerabilidades abiertas fuera del alcance. Sigue
  pendiente, de HU-011, que `AuthDao` compara contraseñas en texto plano.
