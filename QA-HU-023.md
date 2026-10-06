# QA — HU-023: Búsqueda de alumno para gestión de cuotas

Pruebas de HU-023 en la rama `feature/HU-023-busqueda-alumno-cuotas`, creada desde `main`
(`38a7f67`, HU-022). Se ejecutaron junto con Claude Code el 2026-10-05: pruebas unitarias (xUnit y
Vitest), integración del SQL contra una base real, medición de rendimiento con los 577 jugadores
reales y pruebas HTTP contra la API levantada. La prueba visual en navegador queda pendiente (sección 6).

---

## 1. Análisis previo: qué existía y qué faltaba

| Tema | Encontrado | Acción en HU-023 |
|---|---|---|
| Búsqueda de jugadores | El formulario "Registrar cobro" trae los 577 jugadores con `GET /api/jugadores` y filtra en el navegador. No había búsqueda del lado del servidor. | Endpoint nuevo `GET /api/players/search?term=` y buscador propio arriba del panel. El autocompletado del formulario no se tocó. |
| DAO | `PlayerDAO` (alta de jugadores, HU-033), ADO.NET puro. | `SearchPlayersForFeeManagement(term, maxResults)` con `SqlCommand` parametrizado. |
| Tildes | Collation `Modern_Spanish_CI_AS`: distingue tildes. **160 de los 577 apellidos reales tienen tilde** (BAZÁN, CÓRDOBA, DÍAZ…). Con un `LIKE` directo, "Bazan" no encuentra a BAZÁN. | El apellido se compara con `COLLATE Modern_Spanish_CI_AI`. La ñ sigue siendo una letra propia, como en castellano. |
| DNI | Se guardan sin puntos ni espacios (verificado: 0 casos). | El término se limpia: "47.970.803" busca "47970803". |
| Destino al elegir | La ficha `jugadores/:id/deuda` (HU-025/026) muestra las cuotas, su estado y la deuda, y la ven los roles 1 y 2. | "Ver cuotas" navega a esa ficha, igual que tocar un pendiente de cobro. |

### Decisiones

- **Roles `"1,2"`, no `"Admin"`.** Como en HU-022: el claim de rol del JWT es el id numérico. Con
  `"Admin"`, todos recibirían 403. Los dos roles administrativos gestionan cuotas.
- **Endpoint en `PlayersController`**, con la ruta pedida (`/api/players/search`).
- **Un término solo de dígitos se busca en el DNI, y cualquier otro, en el apellido.** Así no hace
  falta un `OR`, y "4797" no trae apellidos.
- **Validación:** el término debe tener entre 2 y 50 caracteres, sin contar los espacios de los
  extremos. Fuera de ese rango, la API responde 400 con un `mensaje` en español y no consulta la base.
- **Tope de 50 resultados**, ordenados así: DNI exacto, apellidos que empiezan con el término,
  el resto y, dentro de cada grupo, por apellido y nombre. Si se llega al tope, la pantalla pide
  refinar la búsqueda.
- **Comodines escapados:** `%`, `_`, `[` y `\` escritos por el usuario se buscan como texto
  (`ESCAPE '\'`).
- **Sin índice nuevo ni migración:** un `LIKE '%x%'` no puede usar un índice, y la tabla tiene
  cientos de filas. El recorrido completo tarda milisegundos (sección 4).
- **Parámetros `VarChar`**, del mismo tipo que las columnas, para evitar la conversión implícita
  de `nvarchar` en cada fila.

## 2. Cambios

**Backend**
- `EntityLibrary/PlayerSearchResult.cs` (nuevo).
- `ApiGestion/Models/PlayerSearchResultDto.cs` (nuevo): `Id`, `FirstName`, `LastName`, `Dni`, `CategoryName`.
- `DaoLibrary/PlayerDao.cs`:
  - `SearchPlayersForFeeManagement` (virtual).
  - `SearchPlayers`: interno, recibe la conexión y la transacción para la prueba de integración.
  - `OnlyDniDigits` y `EscapeLike`.
- `PlayersController.cs`: `GET api/players/search` con `[Authorize(Roles = "1,2")]`. Errores:
  - 400 si el término es inválido.
  - 500 con "No se pudo realizar la búsqueda de alumnos." si falla la base.

**Frontend**
- `models/PlayerSearchResultModel.ts` y `services/player-search.ts` (nuevos).
- `admin-portal/cuotas-pagos/player-search/` (nuevo componente `PlayerSearch`):
  - Barra con ícono, botón "Buscar" y botón "×" para limpiar.
  - Busca mientras se escribe (debounce de 350 ms) o de inmediato con Enter o "Buscar".
  - `switchMap`: una respuesta vieja nunca pisa a una más nueva.
  - Spinner en el campo y el texto "Buscando alumnos…".
  - Grilla con las columnas Nombre, Apellido, DNI, Categoría y "Ver cuotas".
  - Sin coincidencias: "No se encontraron alumnos con ese criterio de búsqueda".
  - Error: toast en español y "Reintentar".
- `cuotas-pagos.html/.ts`: el buscador va debajo del encabezado. Elegir un alumno abre
  `/admin/portal/jugadores/:id/deuda`.
- `shared/http-error-message.ts`: acepta un texto alternativo y muestra el `mensaje` del backend
  en los errores 4xx. En los 5xx nunca lo muestra, porque son textos internos.

**Postman:** colección nueva `HU-023 - Busqueda de Alumno para Gestion de Cuotas`, con 15 requests.

## 3. Pruebas unitarias

| Comando | Resultado |
|---|---|
| `dotnet test ApiGestion.Tests` | **220 pasan, 11 omitidas** (las de integración sin `CACC_TEST_DB`), 0 fallas |
| `npx ng test --watch=false` | **201/201** (189 previas + 12 nuevas) |

**Backend: `PlayersController.Search.Tests.cs` (23 casos)**

| # | Caso | Estado |
|---|---|---|
| B1 | Mapea cada resultado al DTO con los 5 campos | ✅ |
| B2 | Sin coincidencias → 200 con lista vacía | ✅ |
| B3 | Recorta espacios del término y pide `MaxSearchResults` (50) al DAO | ✅ |
| B4 | `null`, `""`, `"   "`, `"a"` → 400, sin consultar el DAO (4 casos) | ✅ |
| B5 | 51 caracteres → 400, sin consultar el DAO | ✅ |
| B6 | Falla de la base → 500 con mensaje en español | ✅ |
| B7 | `[HttpGet("search")]`, `Roles = "1,2"`, sin `[AllowAnonymous]` | ✅ |
| B8 | `OnlyDniDigits`: "47.970.803" y " 47 970 803 " → DNI; "acosta", "o'neill", "47a", ".." → apellido (8 casos) | ✅ |
| B9 | `EscapeLike`: `%`, `_`, `[` y `\` escapados (5 casos) | ✅ |

**Frontend: `player-search.spec.ts` (12 casos)**

| # | Caso | Estado |
|---|---|---|
| F1 | No consulta antes de que termine el debounce (350 ms) | ✅ |
| F2 | Escribiendo "b", "ba", "baz" seguido, consulta una sola vez con "baz" | ✅ |
| F3 | Con 1 carácter no consulta y muestra "Ingresá al menos 2 caracteres" | ✅ |
| F4 | Columnas Nombre, Apellido, DNI y Categoría, con los datos del alumno | ✅ |
| F5 | Sin coincidencias: "No se encontraron alumnos con ese criterio de búsqueda" y sin tabla | ✅ |
| F6 | Spinner y "Buscando alumnos…" mientras la respuesta no llega; desaparecen al llegar | ✅ |
| F7 | Enter busca al instante, sin esperar el debounce | ✅ |
| F8 | Error de conexión → 1 toast "No se pudo conectar con el servidor…" (tipo error), aviso en pantalla (no "sin resultados"); "Reintentar" vuelve a buscar | ✅ |
| F9 | Error 500 → toast "No se pudo realizar la búsqueda de alumnos. Intentá de nuevo." | ✅ |
| F10 | Una respuesta lenta anterior no pisa a la más reciente | ✅ |
| F11 | "Ver cuotas" emite el alumno elegido | ✅ |
| F12 | "×" limpia el campo y oculta los resultados al instante | ✅ |

**Bug encontrado por F12 y corregido:** "×" vaciaba el campo, pero la grilla anterior seguía
visible hasta que vencía el debounce.

## 4. Integración y rendimiento (SQL Server)

### 4.1 SQL contra una base real — `PlayerSearchIntegrationTests`

```powershell
$env:CACC_TEST_DB = "Server=DESKTOP-IT1K08M\SQLEXPRESS;Database=ClubCamionerosPruebaCuatro;Trusted_Connection=True;TrustServerCertificate=True;"
dotnet test ApiGestion.Tests --filter "FullyQualifiedName~PlayerSearchIntegrationTests"
```

Cada prueba crea sus jugadores dentro de una transacción que se revierte, así que no deja datos.
**Resultado: 2/2.**

| # | Caso | Estado |
|---|---|---|
| S1 | Apellido "QWZÁRTEZ": lo encuentran "qwzartez" (sin tilde y en minúsculas) y "WZÁR" (parcial) | ✅ |
| S2 | DNI "99887766": lo encuentran "99.887.766" (con puntos) y "9988776" (parcial), con nombre, apellido y categoría | ✅ |
| S3 | "qwz%rtez" no encuentra nada: el `%` es literal | ✅ |
| S4 | Término inexistente → vacío | ✅ |
| S5 | Con tope 2 y 3 coincidencias devuelve 2 | ✅ |

### 4.2 Rendimiento con los 577 jugadores reales

Se ejecutó la misma consulta en `ClubCamionerosPRUEBA` (solo lectura). El límite pedido era
**menor a 2 segundos**.

| Término | Campo | Filas | Tiempo |
|---|---|---|---|
| `ez` | apellido | 50 (tope) | 15,8 ms |
| `bazan` | apellido | 2 (BAZÁN, sin tilde en el término) | 10,9 ms |
| `a` (caso extremo, la API lo rechaza) | apellido | 50 (tope) | 6,2 ms |
| `47` | DNI | 50 (tope) | 4,8 ms |
| `47970803` | DNI | 1 | 3,0 ms |

## 5. Pruebas HTTP contra la API real

Entorno: la API se levantó con `dotnet run` contra `ClubCamionerosPruebaCuatro`, con las
migraciones desactivadas (`DatabaseMigrations__Enabled=false`) y una clave JWT temporal.
No se conocía la contraseña del administrador. Por eso, igual que en QA-HU-025, el token se firmó
con los claims de `AuthController` para el usuario real 1 (rol 1). Así se ejercita toda la
validación del token, incluida `UsuarioActivoConRol`, salvo el endpoint de login.

| # | Request | Esperado | Obtenido | Estado |
|---|---|---|---|---|
| A1 | Sin token | 401 | 401 | ✅ |
| A2 | Token inválido | 401 | 401 | ✅ |
| A3 | `term=rueb` (apellido parcial) | 200 con los PRUEBA | 200, 5 resultados | ✅ |
| A4 | `term=prueba` (minúsculas) | 200 | 200, 5 resultados (10 ms) | ✅ |
| A5 | `term=99000003` (DNI exacto) | 200, 1 resultado | 200, id 3 | ✅ |
| A6 | `term=99.000.003` (con puntos) | 200, 1 resultado | 200, id 3 | ✅ |
| A7 | `term=0000` (DNI parcial) | 200 | 200, 5 resultados | ✅ |
| A8 | `term=zzzz` | 200 `[]` | 200 `[]` | ✅ |
| A9 | `term=pr%eba` (comodín) | 200 `[]` | 200 `[]` | ✅ |
| A10 | `term=p` | 400 "Ingresá al menos 2 caracteres…" | 400, mismo texto | ✅ |
| A11 | Sin `term` | 400 | 400 | ✅ |
| A12 | 51 caracteres | 400 "La búsqueda admite hasta 50 caracteres." | 400, mismo texto | ✅ |
| A13 | `term=x' OR 1=1--` | 200 `[]` | 200 `[]` | ✅ |

**Resultado: 13/13.** Los tiempos fueron de 8 a 270 ms; los más altos corresponden a las
primeras llamadas, mientras la API arrancaba.

**403 no probado por HTTP:** la base de desarrollo tiene un solo usuario, con rol 1. Un token de
otro rol para ese usuario falla en `UsuarioActivoConRol` y da 401. El 403 queda cubierto por B7
(atributo `Roles = "1,2"`) y por el caso manual M9.

## 6. Pruebas manuales (pendientes)

API y frontend levantados (`dotnet run` + `npm start`), con login real.

| # | Caso | Pasos | Resultado esperado | Estado |
|---|---|---|---|---|
| M1 | Ubicación | Entrar a Cuotas y Pagos | "Buscar alumno" arriba del panel, debajo del encabezado | ⏳ |
| M2 | Por apellido | Escribir "bazan" sin Enter | Al dejar de escribir aparecen los BAZÁN con nombre, DNI y categoría | ⏳ |
| M3 | Por DNI | Escribir un DNI con puntos y apretar Enter | Aparece el alumno al instante | ⏳ |
| M4 | Sin resultados | Escribir "zzqx" | Tarjeta "No se encontraron alumnos con ese criterio de búsqueda" | ⏳ |
| M5 | Carga | DevTools → Network → "Slow 3G" → buscar | Spinner en el campo y "Buscando alumnos…" | ⏳ |
| M6 | Sin conexión | Detener la API → buscar | Toast rojo "No se pudo conectar con el servidor…", aviso con "Reintentar"; al levantar la API y reintentar aparecen los resultados | ⏳ |
| M7 | Seleccionar | "Ver cuotas" (o clic en la fila) | Se abre la ficha de deuda del alumno con sus cuotas y su total adeudado | ⏳ |
| M8 | Tope | Buscar "ez" en una base con muchos jugadores | 50 filas y el aviso "Se muestran los primeros 50 resultados…" | ⏳ |
| M9 | 403 | Usuario real con un rol fuera de 1/2 → `GET /api/players/search?term=ab` con su token | 403 Forbidden | ⏳ |
| M10 | Responsive | Ancho de 375 px | Barra a todo el ancho; se oculta la columna Categoría; sin scroll horizontal de la página | ⏳ |
| M11 | Postman | Correr la colección HU-023 en orden con el entorno Local | 15/15 requests en verde | ⏳ |

## 7. Criterios de aceptación

| Criterio | Estado |
|---|---|
| Búsqueda por DNI, exacta o parcial | ✅ S2, A5–A7, B8 |
| Búsqueda por apellido parcial (`LIKE %término%`) | ✅ S1, A3, A4 |
| Incluye la categoría del alumno | ✅ B1, S2 |
| Consulta parametrizada en ADO.NET, sin ORM | ✅ A13, S3, B9 |
| Respuesta en menos de 2 segundos | ✅ 4.2 (máximo 15,8 ms en la base) y sección 5 (menos de 300 ms por HTTP) |
| `GET /api/players/search?term=` protegido por rol | ✅ A1, A2, B7; ⏳ M9 (403) |
| DTO `PlayerSearchResultDto` (Id, FirstName, LastName, Dni, CategoryName) | ✅ B1 |
| Barra de búsqueda arriba del panel, con debounce | ✅ F1, F2, F7; ⏳ M1 |
| Grilla Nombre, Apellido, DNI, Categoría y acción para seleccionar | ✅ F4, F11; ⏳ M7 |
| Mensaje "No se encontraron alumnos con ese criterio de búsqueda" | ✅ F5; ⏳ M4 |
| Indicador de carga | ✅ F6; ⏳ M5 |
| Toast en español ante errores de conexión | ✅ F8, F9; ⏳ M6 |

## 8. Observaciones

- **Ñ:** "nunez" no encuentra a "NÚÑEZ", porque en `Modern_Spanish` la ñ es una letra distinta de
  la n. Las tildes sí se ignoran ("nuñez" lo encuentra).
- **Solo DNI o apellido**, como pide la HU. Buscar por nombre de pila no está incluido; agregarlo
  es una línea más en el `WHERE`.
- **El autocompletado de "Registrar cobro"** sigue descargando el padrón completo. Podría usar
  este endpoint, pero queda fuera de esta HU.
- **El texto "alumno"** sale de la HU. El resto del panel dice "jugador".
