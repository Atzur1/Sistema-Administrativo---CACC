# QA HU-096: misma cabecera de jugador en Perfil y en Deuda

**Fecha:** 04/11/2026 · **Rama:** `FramD` (desde f9d4514, sin commitear)

**Resultado: PASS en lo automatizado · QA en navegador PENDIENTE (BLOCKED: entorno no levantado).**

| Prueba | Resultado |
|---|---|
| Tests frontend (`ng test`) | **617/617** en 42 archivos. 44 nuevos o actualizados para esta HU (componente 23, consistencia 3, Perfil 8, Deuda 10) |
| Tests backend (`dotnet test ApiGestion.Tests`) | **722 pasan · 1 falla · 167 omitidos** (los omitidos usan base de datos). La falla es **preexistente** y no tiene relación con HU-096 (ver §7). Los 8 tests nuevos pasan |
| Build frontend (`ng build`) | **Correcto.** Avisa que el bundle inicial pesa 1,23 MB contra un presupuesto de 1,00 MB. Ese aviso ya existía: QA-HU-107 lo registra con 1,21 MB |
| QA en navegador | **Pendiente.** No había API, base de datos ni front levantados. El checklist de §6 está listo para ejecutar |

---

## 1. Relevamiento: lo que había

| Aspecto | Perfil (`jugadores/:id`) | Deuda (`jugadores/:id/deuda`) |
|---|---|---|
| Datos del jugador | `GET api/jugadores/{id}` (`getJugador`) | El mismo endpoint |
| Nombre (`h1`) | `nombreCompleto` → `PRUEBA, MATIAS` | Igual |
| DNI / categoría | `DNI 99000003 · AFA 20067` | Igual |
| Iniciales | apellido + nombre, **getter copiado** | **El mismo getter, copiado otra vez** |
| Estado deportivo | **No se mostraba.** Solo "Dado de baja el …" (rojo) | **No se mostraba.** Solo "Dado de baja el …" (rojo, con `title`) |
| Fecha de baja | `new Date(iso).toLocaleDateString` | `date` pipe en UTC (otra implementación) |
| Volver | `.back-button` propio → `/admin/portal/actividad-movimientos` | `.boton .boton-secundario` → `/admin/portal/cuotas-pagos` |
| Color de la tarjeta | Verde institucional | Azul de "pendiente" |
| Acciones en la cabecera | Dar de baja / Reactivar + ayuda "?" | **Ninguna.** El cobro ("Registrar Pago") está en la sección Cuotas |
| Indicadores en la tarjeta | Pagos registrados | Total a abonar / Cuotas adeudadas |

**Código duplicado:** el marcado de la tarjeta completo (avatar, `h1`, DNI, categoría, baja), el getter `iniciales`, unas 80 líneas de CSS por pantalla (`.profile-card`, `.profile-avatar`, `.profile-name`, `.profile-meta`, `.profile-baja`, más el responsive) y el botón Volver con su SVG.

**Hallazgo que bloqueaba la HU:** ninguna de las dos pantallas recibía el **estado deportivo**. `GET api/jugadores/{id}` no lo informaba, y el único endpoint por jugador era el de integración con el Portal Deportivo (`api/integracion/v1/estado-deuda`). Ese endpoint tiene rate-limit y su propio contrato, y sus roles no incluyen el rol `2` del portal administrativo. Con el OK del responsable se agregó **un solo campo opcional** a `GET api/jugadores/{id}`.

## 2. Qué cambió

**Backend (mínimo, sin lógica nueva):**

- `PlayerSummaryResponseDTO.EstadoDeportivo` (`string?`). Solo lo completa `GET api/jugadores/{id}`; el listado devuelve `null`.
- `JugadoresController.ObtenerJugador` lo lee de `IEstadoDeudaDao.ObtenerPorDni` + `EstadoDeudaJugador.Estado` + `EstadoDeportivoTexto`. Es la misma fuente que usan Deudas y Morosidad y la API de integración. Toma la fila del mismo `IdJugador` (por si un DNI está repetido) y no filtra categorías.
- Si esa lectura falla, el jugador se devuelve igual con `estadoDeportivo: null` y se registra un warning. El perfil no deja de cargar por un dato secundario.
- No se tocaron la base de datos, el cálculo de deuda, el cálculo del estado, los permisos ni las rutas.

**Frontend:**

- Nuevo componente `shared/cabecera-jugador` (`app-cabecera-jugador`).
- Perfil y Deuda reemplazan su cabecera por el componente. Se borraron sus copias de marcado, CSS y getter.
- Deuda vuelve a leer el jugador **después de releer el estado de cuenta tras un cobro**. El estado depende de las cuotas vencidas: sin esta relectura, alguien que pasa de Inhabilitado a Habilitado seguiría viéndose Inhabilitado. Si la relectura falla, se conserva la cabecera anterior.

## 3. API del componente

| Entrada / salida | Tipo | Uso |
|---|---|---|
| `[jugador]` | `DatosCabeceraJugador \| null` | `nombre`, `apellido`, `dni`, `categoria?`, `nombreCompleto?`, `estadoDeportivo?`, `fechaBaja?`, `fotoUrl?`. `PlayerSummaryModel` ya lo cumple: la pantalla pasa el jugador que **ya cargó** |
| `[volverA]` | `string \| unknown[] \| null` | Ruta para `router.navigate`. Se usa solo si nadie escucha `(volver)` |
| `[textoVolver]` | `string` | `"Volver"` por defecto |
| `(volver)` | `void` | Si la pantalla lo escucha, decide ella el destino. Perfil y Deuda lo escuchan y conservan su `volver()` de siempre |
| `[cabeceraAcciones]` | content projection | Acciones propias (Perfil: Dar de baja / Reactivar + ayuda). Si no hay, el área no se muestra |
| contenido por defecto | content projection | Lo propio de cada pantalla debajo de los datos, dentro de la tarjeta (indicadores) |

La cabecera **no** llama a la API, **no** conoce el cobro y **no** calcula el estado.

## 4. Reglas de presentación

| Dato | Regla |
|---|---|
| `h1` | Lo tiene **la cabecera**. Las pantallas no tienen otro: **1 `h1` por pantalla**, con el nombre del jugador |
| Nombre | `nombreCompleto` del backend (`APELLIDO, Nombre`). Si no viene, se arma igual |
| Iniciales | Primera letra del apellido + primera del nombre, sin espacios, en mayúsculas `es-AR` (respeta acentos y ñ) |
| Foto | Hoy el sistema no tiene fotos de jugadores. Si llega `fotoUrl`, se muestra con `alt="Foto de …"`; si no carga, se vuelve a las iniciales. No se agregó subida de fotos |
| DNI | Tal como viene (`DNI 99000003`). El portal no formatea DNI con puntos en ningún otro lugar |
| Categoría | La del API; sin dato, "Sin categoría" (mismo texto que Actualización de Aranceles) |
| Estado | `habilitado` → **Habilitado** (`tono-exito`) · `solo_entrenamientos` → **Solo entrenamientos** (`tono-aviso`) · `inhabilitado` → **Inhabilitado** (`tono-peligro`) · `baja` → **Dado de baja el dd/mm/aaaa** (`tono-neutro`, como en Deudas y Morosidad) · sin dato → **Estado no disponible** (`tono-neutro`) |
| Estado sin dato y con fecha de baja | Se muestra igual "Dado de baja el …" aparte: es un dato del jugador, no un cálculo |
| Accesibilidad del estado | Texto visible más el prefijo "Estado deportivo:" para lectores de pantalla, y un `title` con el significado. No depende solo del color |
| Carga | La pantalla sigue mostrando su `app-cargando`. La cabecera solo muestra Volver hasta tener el jugador: nunca `undefined`, `null` ni iniciales vacías |
| Error | La pantalla sigue mostrando "No se encontró el jugador solicitado." debajo de Volver |

**Diferencias visuales que se unificaron a propósito:** Deuda pasa a usar la tarjeta verde institucional y el mismo Volver (`.boton .boton-secundario`) que Perfil. Antes la tarjeta era azul y el botón distinto. El resto de Deuda conserva su paleta de "pendiente". La baja deja de verse en rojo y pasa a la etiqueta neutra de Deudas y Morosidad.

## 5. Tests automatizados

| # HU | Qué valida | Dónde |
|---|---|---|
| 1-4 | Nombre en el `h1`, DNI, categoría, los 4 estados y "no disponible" con su tono | `cabecera-jugador.spec.ts` |
| 5-6 | Iniciales sin foto (con acentos y ñ); foto con `alt`; vuelta a iniciales si la foto falla | `cabecera-jugador.spec.ts` |
| 7 | Volver con `(volver)` y con `volverA`; botón accesible; visible durante la carga | `cabecera-jugador.spec.ts` |
| 8 | Acciones proyectadas; contenido propio dentro de la tarjeta; orden de tabulación | `cabecera-jugador.spec.ts` |
| 9 | Perfil usa la cabecera, con un solo `getJugador`; Dar de baja/Reactivar en el área de acciones | `jugador-perfil.spec.ts` |
| 10 | Deuda usa la cabecera, con un solo `getJugador`; "Registrar Pago" sigue en Cuotas, fuera de la cabecera; indicadores dentro de la tarjeta; relectura del estado tras un cobro, y qué pasa si esa relectura falla | `deuda-jugador.spec.ts` |
| 11 | Un único `h1` en Perfil y en Deuda | Ambos specs de pantalla |
| 12 | Mismo jugador → cabecera idéntica en Perfil y Deuda; cada una conserva su destino de Volver; solo Perfil agrega acciones | `cabecera-jugador.consistencia.spec.ts` |
| Backend | Estado por cada regla (0 / 1 / 2+ vencidas, baja); DNI compartido; falla de la fuente; sin fila; jugador inexistente no consulta el estado | `JugadoresController.Tests.cs` |

## 6. QA manual en navegador (pendiente de ejecutar)

Usar **el mismo jugador**, idealmente con una cuota vencida para que aparezca "Solo entrenamientos". Repetir con SuperAdmin; con Administrador (rol 2) solo se puede probar Deuda.

| # | Paso | Esperado | Resultado |
|---|---|---|---|
| 1 | Actividad y Movimientos → abrir el **Perfil** del jugador | Volver, avatar con iniciales, `h1` `APELLIDO, Nombre`, `DNI …`, categoría, etiqueta de estado | ☐ |
| 2 | Registrar la cabecera (captura) | — | ☐ |
| 3 | Cuotas y Pagos o Deudas y Morosidad → abrir la **Deuda** del mismo jugador | — | ☐ |
| 4 | Comparar iniciales, nombre, DNI, categoría, estado y estilos con la captura del paso 2 | Idénticos. Solo cambian los indicadores y las acciones | ☐ |
| 4b | Comparar el estado con la columna Estado de Deudas y Morosidad | Mismo texto | ☐ |
| 5 | Volver en Perfil / Volver en Deuda | Actividad y Movimientos / Cuotas y Pagos (sin cambios) | ☐ |
| 6 | En Deuda, cobrar la cuota vencida con "Registrar Pago" | Se cobra como antes y la cabecera pasa a "Habilitado" sin recargar | ☐ |
| 6b | En Perfil: Dar de baja y después Reactivar | Etiqueta "Dado de baja el …" y después el estado vuelve | ☐ |
| 7 | Repetir 1-4 a 1280 px, 768 px y 375 px | Sin superposición; acciones debajo de los datos en ≤ 640 px; un nombre largo baja de renglón; DNI legible | ☐ |
| 8 | Teclado: Tab desde el inicio | Volver → acciones de Perfil → resto; foco visible | ☐ |
| 9 | DevTools → Network al abrir cada pantalla | **Un solo** `GET api/jugadores/{id}` por pantalla | ☐ |
| 10 | Lector de pantalla o inspector de accesibilidad | Un `h1`; el estado se lee "Estado deportivo: …" | ☐ |

## 7. Pendientes y observaciones

- **QA en navegador (§6): BLOCKED.** Necesita API + SQL Server + front levantados y credenciales.
- **Falla backend preexistente:** `PlayersControllerVoidDiscountTests.AssignDiscount_StartingWithAnOldFee_IsRefused(mesesAtras: -1)` → `InvalidOperationException: No se ha inicializado la propiedad ConnectionString`, en `PlayersController.AssignDiscount`. **Falla igual sin los cambios de HU-096**: se verificó dejando de lado los 3 archivos backend de la HU. Depende de la fecha (−1 mes desde hoy llega a una rama que abre conexión). Conviene abrir un ticket aparte.
- **Deuda no tenía acción en la cabecera.** La HU menciona "Cobrar", pero el cobro siempre estuvo en la sección Cuotas ("Registrar Pago"). Se conservó ahí y no se inventó un botón nuevo.
- **Fotos:** no existen en el modelo. El componente las soporta (`fotoUrl`), pero ninguna pantalla las pasa todavía.
- `GET api/jugadores` (listado) ahora serializa `estadoDeportivo: null`. Es un campo opcional nuevo y no rompe a quien lo consume.
