# QA — HU-011: Formulario y selección del motivo de bonificación / descuento

Evidencia formal de las pruebas de HU-011, como parte de su Definición de
Listo. Pruebas corridas sobre `ClubCamionerosPRUEBA` en `(localdb)\MSSQLLocalDB`,
el 2026-09-15, con la API en `http://localhost:5118`.

HU-011 completa el flujo que HU-014 había dejado a medias: aquella mostraba las
bonificaciones pero el botón "Asignar beneficio" era una simulación en el
frontend, sin ningún POST detrás. Esta historia agrega la persistencia real, la
edición, la cancelación y el beneficio de monto fijo.

---

## Cambio de modelo de datos

Antes de HU-011, el porcentaje y las fechas de vigencia vivían en
`TIPO_DESCUENTO`, lo que convertía esa tabla en una fila por asignación en vez
de un catálogo. Con ese modelo no había forma de listar los motivos válidos ni
de que dos jugadores tuvieran el mismo motivo con distinto valor.

| | Antes (HU-014) | Después (HU-011) |
|---|---|---|
| `TIPO_DESCUENTO` | motivo + porcentaje + fechas | **solo el motivo** (catálogo cerrado, 3 filas) |
| `JUGADORES_DESCUENTOS` | jugador + motivo + estado | jugador + motivo + estado + **tipo_valor, porcentaje, monto_fijo, fechas** |

Migración: `database/V20260915_01__descuentos_catalogo_y_monto_fijo.sql`.

La regla "una sola bonificación activa por jugador" quedó declarada en el motor
con un índice único filtrado, no solamente en la API:

```sql
CREATE UNIQUE INDEX UX_JUGDESC_UNA_ACTIVA
    ON JUGADORES_DESCUENTOS (FK_id_jugador)
    WHERE estado_activo = 1;
```

---

## Pruebas de base de datos (constraints)

Script: pruebas transaccionales sobre `JUGADORES_DESCUENTOS`, con `ROLLBACK`
final — no dejan datos. Verifican que las reglas se sostienen aunque alguien
escriba por fuera de la API.

| # | Caso | Resultado esperado | Resultado obtenido | Estado |
|---|---|---|---|---|
| 1 | Alta porcentual 50 % | Insertada | Insertada | ✅ |
| 2 | Alta monto fijo $15.000 | Insertada | Insertada | ✅ |
| 3 | Segunda activa para el mismo jugador | Rechazada por `UX_JUGDESC_UNA_ACTIVA` | Bloqueada | ✅ |
| 4 | Porcentaje y monto fijo juntos | Rechazada por `CK_JUGDESC_VALOR_EXCLUYENTE` | Bloqueada | ✅ |
| 5 | Porcentaje 0 | Rechazada por `CK_JUGDESC_PORCENTAJE_RANGO` | Bloqueada | ✅ |
| 6 | Porcentaje 101 | Rechazada por `CK_JUGDESC_PORCENTAJE_RANGO` | Bloqueada | ✅ |
| 7 | Monto fijo negativo | Rechazada por `CK_JUGDESC_MONTO_POSITIVO` | Bloqueada | ✅ |
| 8 | Reasignar después de cancelar | Permitida | Permitida | ✅ |
| 9 | `tipo_valor` distinto de `%` o `$` | Rechazada por `CK_JUGDESC_TIPO_VALOR` | Bloqueada | ✅ |

**Resultado: 9/9 en verde.**

---

## Pruebas unitarias (xUnit)

Comando: `dotnet test ApiGestion.Tests/ApiGestion.Tests.csproj`

| Suite | Qué cubre | Resultado |
|---|---|---|
| `DiscountRequestDTOTests` | Validaciones del contrato de entrada: motivo obligatorio, tipo de valor cerrado, rango del porcentaje, monto positivo, exclusividad porcentaje/monto, formato y orden de fechas | 24 tests ✅ |
| `PlayersControllerDiscountsTests` | `playerId` no positivo en GET, POST, PUT y DELETE, resuelto antes de tocar la base | 9 tests ✅ |
| `PaymentRequestDTOTests` + `PaymentsControllerTests` (preexistentes) | Regresión de HU-015 / HU-016 | 33 tests ✅ |

**Resultado: 66 tests, 0 fallos.**

Los DAO reciben una cadena de conexión vacía a propósito: si un test llegara a
la base, fallaría. Que pasen prueba que cada regla responde por sí sola.

---

## Pruebas de integración (API + SQL Server)

Script: `test_api_hu011.sh`, contra la API levantada y la base real. Cada alta
se verifica releyendo con un `GET` posterior, no con lo que devolvió el `POST`.

| # | Caso de la HU | Método / URL | Esperado | Obtenido | Estado |
|---|---|---|---|---|---|
| 1 | Jugador sin beneficio → Beca Completa | `POST /api/Players/10/discount` | 201 | 201 Created | ✅ |
| 2 | Media Beca porcentual 50 % | `POST /api/Players/11/discount` | 201 + persistido | 201, releído `50.00 %` | ✅ |
| 3 | Descuento por Hermanos monto fijo | `POST /api/Players/12/discount` | 201 + persistido | 201, releído `$15000` | ✅ |
| 4 | Motivo vacío | `POST` con `reason: ""` | 400 | 400 Bad Request | ✅ |
| 4b | Motivo fuera del catálogo | `POST` con `reason: "Beca Inventada"` | 400 | 400, *"The benefit reason must be one of: Beca Completa, Media Beca, Descuento por Hermanos."* | ✅ |
| 5 | Porcentaje 0 | `POST` con `percentage: 0` | 400 | 400 Bad Request | ✅ |
| 6 | Porcentaje mayor a 100 | `POST` con `percentage: 101` | 400 | 400 Bad Request | ✅ |
| 7a | Monto fijo 0 | `POST` con `fixedAmount: 0` | 400 | 400 Bad Request | ✅ |
| 7b | Monto fijo negativo | `POST` con `fixedAmount: -500` | 400 | 400 Bad Request | ✅ |
| 7c | Porcentaje y monto juntos | `POST` con ambos | 400 | 400 Bad Request | ✅ |
| 9 | **Duplicado llamando directo a la API** | `POST` sobre jugador que ya tiene | 409 | 409 Conflict, *"already has an active benefit"* | ✅ |
| 10 | Editar y verificar persistencia | `PUT` + `GET` | 200, cambios en la base | 200, releído `Descuento por Hermanos / $ / fijo=22500` | ✅ |
| 11 | Cancelar y reasignar | `DELETE` + `GET` + `POST` | 204, luego 404, luego 201 | 204 / 404 / 201 | ✅ |
| 12 | Relectura desde la base | `GET /api/Players/10/discount` | 200 con datos persistidos | 200, incluye nombre y categoría del join | ✅ |
| — | Editar jugador sin bonificación | `PUT /api/Players/500/discount` | 404 | 404 Not Found | ✅ |
| — | Cancelar jugador sin bonificación | `DELETE /api/Players/500/discount` | 404 | 404 Not Found | ✅ |
| — | Jugador inexistente | `POST /api/Players/999999/discount` | 400 | 400 Bad Request | ✅ |
| — | Escritura sin token | `POST` sin `Authorization` | 401 | 401 Unauthorized | ✅ |

**Resultado: 24/24 en verde.**

El caso 9 es el central de la historia: la regla de una sola bonificación activa
se sostiene aunque se llame a la API por fuera de la interfaz. Y no depende solo
del chequeo del controller — si dos pedidos corrieran a la vez, el índice único
filtrado los corta igual y el controller traduce esa violación a un 409.

---

## Regresión de HU-014

`GET /api/Players/{id}/discount` cambió de comportamiento durante el desarrollo
y hubo que corregirlo: el popup necesita ver una bonificación vencida (porque
sigue ocupando el único lugar activo del jugador), pero el QA de HU-014 exige
que una vencida responda 404. Se resolvió con un parámetro opcional
`includeExpired`, que por defecto es `false` y deja el contrato original intacto.

Escenario recreado sobre el modelo nuevo:

| Jugador | Situación | `GET .../discount` | `GET .../discount?includeExpired=true` | Estado |
|---|---|---|---|---|
| 101 | Bonificación vigente | 200 ✅ | 200 ✅ | ✅ |
| 102 | Vigencia vencida (2025) | **404** ✅ | 200 con `isActive: false` ✅ | ✅ |
| 103 | Dada de baja | **404** ✅ | 404 ✅ | ✅ |

Resto de endpoints de HU-014, con el seed cargado:

| Endpoint | Esperado | Obtenido | Estado |
|---|---|---|---|
| `GET /api/Players/discounts` | 200, solo vigentes | 200, 4 filas, todas `isActive: true` | ✅ |
| `GET /api/Players/discounts/all` | 200, vigentes e históricas | 200 | ✅ |
| `GET /api/Players/5/discount` (vencida) | 404 | 404 | ✅ |
| `GET /api/Players/6/discount` (de baja) | 404 | 404 | ✅ |
| `GET /api/Players/0/discount` | 400 | 400 | ✅ |
| `GET /api/Players/999999/discount` | 404 | 404 | ✅ |

**HU-014 no se rompió.**

---

## Compilación

| Proyecto | Comando | Resultado |
|---|---|---|
| Backend | `dotnet test ApiGestion.Tests/ApiGestion.Tests.csproj` | ✅ compila, 66/66 tests |
| Frontend (tipos) | `npx tsc --noEmit -p tsconfig.app.json` | ✅ exit 0, sin errores |
| Frontend (plantillas, AOT) | `npx ngc -p tsconfig.app.json --rootDir ./src` | ✅ exit 0, sin errores |

---

## Hallazgos durante esta QA

Ninguno de estos es culpa de HU-011: son problemas del entorno y del repositorio
que aparecieron al intentar levantar el sistema para probar.

1. **`frontend-cacc/angular.json` estaba commiteado con marcadores de conflicto
   sin resolver**, y además con la cadena literal `git checkout` pegada dentro
   del objeto `cli`. El archivo no era JSON válido, así que `ng serve` no
   levantaba. Corregido en esta rama.

2. **`package-lock.json` y `frontend-cacc/.gitignore` arrastraban el mismo
   problema** desde merges anteriores. Corregidos.

3. **La migración de HU-014 nunca se había aplicado a la base local.** Las claves
   primarias de `TIPO_DESCUENTO` y `JUGADORES_DESCUENTOS` no eran `IDENTITY`, que
   es justamente el bug que esa migración corrige. Por eso ambas tablas estaban
   vacías: ningún `INSERT` podía funcionar. Se ejecutó `V20260912_01` antes que
   la de esta historia.

4. **La cadena de conexión del repositorio apunta a `localhost\SQLEXPRESS`**, que
   no existe en el equipo de desarrollo usado. La instancia real es
   `(localdb)\MSSQLLocalDB`. Es configuración local, no un defecto del código,
   pero conviene documentarla en el README.

5. **`Jwt:Key` no estaba cargada en `dotnet user-secrets`**, así que el login no
   podía funcionar. El `appsettings.Development.json` remite al README, pero el
   README no explica el paso. Se cargó una clave local para poder probar.

6. **Node.js v24.14.0 instalado; Angular CLI exige v22.22.3, v24.15.0 o v26.0.0.**
   Falta un parche. `ng serve` y `ng build` se niegan a arrancar.

7. **`ng2-charts` y `chart.js` estaban declaradas en `package.json` pero no
   instaladas.** Se resolvió con `npm install`.

8. **Gotcha del índice filtrado:** desde esta migración, cualquier `INSERT`,
   `UPDATE` o `DELETE` sobre `JUGADORES_DESCUENTOS` requiere
   `SET QUOTED_IDENTIFIER ON` y `SET ANSI_NULLS ON`. No afecta a la API
   (`Microsoft.Data.SqlClient` los activa por defecto, y las 24 pruebas de
   integración lo confirman), pero sí a los scripts corridos desde `sqlcmd`. Los
   scripts del repositorio ya los fijan internamente.

9. **`database/seed_descuentos_prueba.sql` quedó incompatible** con el modelo
   nuevo, porque insertaba en columnas de `TIPO_DESCUENTO` que dejaron de
   existir. Se reescribió y se verificó su ejecución.

---

## Criterios de aceptación — estado final

| # | Criterio | Estado | Evidencia |
|---|---|---|---|
| 1 | Opción visible "Asignar Bonificación" en el perfil del jugador | ⚠️ Parcial | El botón existe y abre el popup, pero vive en el panel de *Becados y Descuentos*, no en una ficha financiera por jugador: esa pantalla no existe en el proyecto. Ver *Desvíos*. |
| 2 | Motivo obligatorio entre Beca Completa, Media Beca y Descuento por Hermanos | ✅ | `GET /discounts/types` devuelve exactamente esos tres; unitarias + integración casos 4 y 4b |
| 3 | Permite indicar el valor del beneficio | ✅ | Campo dinámico en el popup; integración casos 2 y 3 |
| 4 | El beneficio puede ser porcentual o de monto fijo | ✅ | `tipo_valor` + columnas excluyentes; integración casos 2, 3 y 7c |
| 5 | Un jugador solo puede tener una bonificación activa | ✅ | Índice único filtrado + chequeo en el controller; BD caso 3, integración caso 9 |
| 6 | Si ya posee una: no crear otra, mostrarla, editarla, cancelarla | ✅ | El popup muestra la activa en lugar del formulario; integración casos 9, 10 y 11 |
| 7 | No se habilita el guardado con datos inválidos | ✅ | `[disabled]="benefitForm.invalid \|\| saving()"` en el botón; validación replicada en DTO y en la tabla |
| 8 | La bonificación persiste en SQL Server, vinculada al jugador | ✅ | Integración casos 1, 2, 3 y 12; releído con `GET` |
| 9 | La bonificación activa se identifica con un badge | ✅ | `app-discount-badge`, con un tono por motivo y el valor cuando se muestra suelto |

---

## Pendiente para cerrar la Definición de Listo

- **Pruebas manuales de interfaz en el navegador: NO EJECUTADAS.** No se pudo
  levantar `ng serve` por el hallazgo 6 (Node.js una versión por debajo del
  mínimo que pide Angular CLI). El frontend se verificó por compilación de tipos
  y de plantillas en AOT, que es lo máximo alcanzable sin actualizar Node, pero
  **eso no reemplaza probar el flujo a mano**. Quedan sin verificar en pantalla:
  apertura del popup, alternancia entre porcentaje y monto fijo, botón
  deshabilitado, confirmación antes de cancelar, badges y mensajes de error.
- Actualizar la colección de Postman **Gestión de Cuotas y Bonificaciones** con
  los cuatro endpoints nuevos (`types`, `POST`, `PUT`, `DELETE`).
- Documentar en el README el paso de `dotnet user-secrets set "Jwt:Key"` y la
  cadena de conexión según la instancia local de cada quien.

---

## Desvíos respecto del enunciado

**Ubicación del formulario.** El criterio 1 pide la acción en "la ficha/perfil
financiero de cada jugador". Esa pantalla no existe: el portal administrativo
tiene tableros por tema (*Becados y Descuentos*, *Cuotas y Pagos*, *Deudas y
Morosidad*…), no una ficha por jugador. Construirla habría significado una ruta,
una pantalla y un servicio nuevos, muy por encima del alcance de esta historia.
Se resolvió con un popup que se abre desde dos lugares del tablero de *Becados y
Descuentos*: buscando al jugador en el panel superior, o desde el botón
**Gestionar** de cada fila de la tabla. La decisión fue acordada antes de
implementar.

**Fuera de alcance por pedido explícito.** No se tocaron las vulnerabilidades de
seguridad abiertas. Queda registrada, para la historia que corresponda, que
`AuthDao` compara la contraseña en texto plano contra la columna `contrasenia`,
sin ningún hash.

---

## Seguridad de la historia

- Todas las consultas nuevas usan SQL parametrizado (`SqlParameter`), sin
  concatenación de valores. Se mantuvo ADO.NET puro, sin ORM.
- Las tres operaciones de escritura (`POST`, `PUT`, `DELETE`) exigen `[Authorize]`
  y responden 401 sin token, verificado en integración.
- El `playerId` viaja en la ruta y no en el cuerpo, así que un pedido no puede
  declarar un jugador y apuntar a otro.
- El motivo se valida contra la tabla catálogo, no contra una lista en código:
  no hay forma de insertar un motivo arbitrario.
