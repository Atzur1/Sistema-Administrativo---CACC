# QA — HU-022: Acceso al panel de gestión de Cuotas

Pruebas de HU-022 en la rama `feature/HU-022-acceso-panel-cuotas`, creada desde `main`
(`6b8cb16`). Las pruebas unitarias se ejecutaron junto con Claude Code el 2026-10-05.
Las manuales (sección 5) quedan pendientes.

---

## 1. Análisis previo: qué existía y qué faltaba

| Tema | Encontrado | Acción en HU-022 |
|---|---|---|
| Sección "Cuotas" | Ya existe como **Cuotas y Pagos** en `/admin/portal/cuotas-pagos` (`CuotasPagos`), con encabezado, métricas, filtros y tabla de pendientes. | Se mantuvieron la ruta y el nombre. No se creó `/fees` ni `/cuotas` para no duplicar la pantalla. |
| Opción en el sidebar | Está en el grupo "Gestión". El sidebar solo se renderiza dentro de `AdminPortal`, que exige rol 1 o 2. | Sin cambios. Un usuario sin rol administrativo nunca ve el sidebar. |
| Guard de ruta | `adminGuard` en el padre `/admin/portal` (sesión + rol 1/2). | Ahora toma el mensaje de la ruta destino (`data.accesoDenegado`). |
| Mensaje de acceso denegado | Genérico: "No tienes permisos para acceder al Portal Administrativo." | En Cuotas: **"Acceso Denegado: No cuenta con permisos suficientes para acceder al panel financiero."** |
| Error de conexión con la API | Se ignoraba (`error: () => {}`). Si fallaba `GET /pagos/pendientes`, la vista decía **"No hay cobros pendientes en este momento"**, que es falso. | Aviso rojo con mensaje según el caso y botón "Reintentar". |
| Estado de carga | No había: mientras cargaba, la tabla aparecía vacía. | "Cargando cobros pendientes…". |
| Backend | `PagosController` con `[Authorize(Roles = "1,2")]`. El test solo verificaba que existiera el atributo. | Tests que fijan los roles `"1,2"` y la ausencia de `[AllowAnonymous]`. |

### Roles: "Admin" vs. roles numéricos

La HU menciona `[Authorize(Roles = "Admin")]`, pero el claim de rol del JWT es el **id numérico**
(`AuthController`: `new Claim(ClaimTypes.Role, idRol.ToString())`). Con `"Admin"` todos los
usuarios quedarían rechazados con 403. Se mantiene la convención del proyecto:

| Rol | Id | Acceso a Cuotas |
|---|---|---|
| SuperAdmin | 1 | Sí |
| Administrador | 2 | Sí |
| Otros (sin rol administrativo) | 3+ | No (403 en API, redirección en el front) |

## 2. Cambios

**Frontend**
- `services/auth.ts`: constante `FINANCIAL_ACCESS_DENIED`. `adminGuard` recorre el snapshot hasta la
  ruta hoja y, si la sección declara `data.accesoDenegado`, muestra ese mensaje. En las demás secciones
  se mantiene el texto anterior.
- `app.routes.ts`: `cuotas-pagos` declara `data: { accesoDenegado: FINANCIAL_ACCESS_DENIED }`.
- `shared/http-error-message.ts` (nuevo): `financialLoadErrorMessage(error)` arma el mensaje según el código de estado:
  - 0 → "No se pudo conectar con el servidor. Verificá tu conexión e intentá de nuevo."
  - 403 → mensaje de acceso denegado.
  - Otro código → "No se pudieron cargar los datos de cuotas. Intentá de nuevo en unos minutos."
- `cuotas-pagos.ts/.html/.css`: estados `errorCarga`, `pendingFailed` y `reintentarCarga()`. Aviso
  `.load-error` (`role="alert"`) debajo del encabezado y "Cargando cobros pendientes…" en la tabla.
- El 401 lo sigue manejando `authInterceptor`: cierra la sesión y vuelve al login con `?sesion=vencida`.

**Backend**
- Sin cambios de código. `PagosController.Tests.cs` suma 2 tests de autorización.
- El 401 y el 403 los devuelve el pipeline estándar (`UseAuthentication` + `UseAuthorization`):
  - Sin token, o con un token inválido o vencido → 401.
  - Con un token válido pero de un rol fuera de `"1,2"` → 403.
  - `OnTokenValidated` además rechaza a usuarios desactivados o con permisos revocados (`tokenVersion`).

## 3. Pruebas unitarias — Frontend (Vitest)

| Comando | Resultado |
|---|---|
| `npx ng test --watch=false --include src/app/services/auth.guards.spec.ts --include src/app/admin-portal/cuotas-pagos/cuotas-pagos.spec.ts` | **12/12** (2 preexistentes de HU-019 + 10 nuevos) |
| `npx ng test --watch=false` (suite completa) | **189/189** |

| # | Caso | Archivo | Estado |
|---|---|---|---|
| F1 | Rol 3 fuerza `/admin/portal/cuotas-pagos` → alerta "Acceso Denegado: …panel financiero." y redirección a `/portales` | `auth.guards.spec.ts` | ✅ |
| F2 | Rol 3 en otra sección (`deudas-morosidad`) → mantiene el mensaje genérico | `auth.guards.spec.ts` | ✅ |
| F3 | Sin sesión → "Debes iniciar sesión…" y redirección al login | `auth.guards.spec.ts` | ✅ |
| F4 | Rol 1 entra a Cuotas sin alerta | `auth.guards.spec.ts` | ✅ |
| F5 | Rol 2 entra a Cuotas sin alerta | `auth.guards.spec.ts` | ✅ |
| F6 | Mientras la API no responde → "Cargando cobros pendientes…", nunca "No hay cobros pendientes" | `cuotas-pagos.spec.ts` | ✅ |
| F7 | Status 0 (sin conexión) → aviso "No se pudo conectar con el servidor" y "No se pudieron cargar los cobros pendientes." | `cuotas-pagos.spec.ts` | ✅ |
| F8 | 403 → aviso con el mensaje de acceso denegado | `cuotas-pagos.spec.ts` | ✅ |
| F9 | 500 → "No se pudieron cargar los datos de cuotas…" | `cuotas-pagos.spec.ts` | ✅ |
| F10 | "Reintentar" vuelve a pedir los datos, el aviso desaparece y se ve el estado real | `cuotas-pagos.spec.ts` | ✅ |

## 4. Pruebas unitarias — Backend (xUnit)

| Comando | Resultado |
|---|---|
| `dotnet test ApiGestion.Tests --filter "FullyQualifiedName~PagosControllerTests"` | **23/23** (21 preexistentes + 2 nuevos) |

| # | Caso | Estado |
|---|---|---|
| B1 | `PagosController` tiene un único `[Authorize]` con `Roles = "1,2"` | ✅ |
| B2 | Ni la clase ni ninguna acción pública tiene `[AllowAnonymous]` | ✅ |

## 5. Pruebas manuales (pendientes)

Requieren la API (`dotnet run` en `ApiGestion`) y el frontend (`npm start`). Para los casos de API,
obtener los tokens con `POST /api/auth/login`.

### 5.1 Navegación y vista

| # | Caso | Pasos | Resultado esperado | Estado |
|---|---|---|---|---|
| M1 | Sidebar SuperAdmin | Login con rol 1 → Portal administrativo | "Cuotas y pagos" visible en "Gestión"; al hacer clic, se abre la vista con el título "Cuotas y Pagos" | ⏳ |
| M2 | Sidebar Administrador | Login con rol 2 | "Cuotas y pagos" visible; `/admin/portal` lleva directo a Cuotas y Pagos | ⏳ |
| M3 | URL directa sin rol | Login con un usuario sin rol 1/2 → escribir `/admin/portal/cuotas-pagos` | Alerta "Acceso Denegado: No cuenta con permisos suficientes para acceder al panel financiero." y vuelta a `/portales`; nunca se ve el panel | ⏳ |
| M4 | URL directa sin sesión | Cerrar sesión → escribir `/admin/portal/cuotas-pagos` | Alerta "Debes iniciar sesión…" y vuelta al login | ⏳ |
| M5 | Estado de carga | DevTools → Network → throttling "Slow 3G" → entrar a Cuotas | "Cargando cobros pendientes…" hasta que llegan los datos | ⏳ |
| M6 | API caída | Con la vista abierta, detener la API → recargar la ruta | Aviso rojo "No se pudo conectar con el servidor…"; la tabla dice "No se pudieron cargar…" (no "No hay cobros") | ⏳ |
| M7 | Reintentar | Con el aviso de M6 visible, levantar la API → "Reintentar" | El aviso desaparece y se cargan métricas y pendientes sin recargar la página | ⏳ |
| M8 | Sesión vencida | Con la vista abierta, esperar a que venza el token (o editarlo en `sessionStorage`) → "Reintentar" | Vuelta al login con el aviso de sesión vencida | ⏳ |
| M9 | Responsive | Ancho de 375 px | El aviso hace wrap y el botón queda visible; sin scroll horizontal | ⏳ |

### 5.2 API (401 / 403)

```bash
API=http://localhost:5000/api   # ajustar al puerto de launchSettings.json
curl -i $API/pagos/resumen                                     # M10
curl -i -H "Authorization: Bearer token-invalido" $API/pagos/resumen   # M11
curl -i -H "Authorization: Bearer $TOKEN_ROL_3" $API/pagos/resumen     # M12
curl -i -H "Authorization: Bearer $TOKEN_ROL_2" $API/pagos/resumen     # M13
curl -i -H "Authorization: Bearer $TOKEN_ROL_1" $API/pagos/resumen     # M14
```

| # | Caso | Esperado | Estado |
|---|---|---|---|
| M10 | Sin token | `401 Unauthorized` | ⏳ |
| M11 | Token inválido | `401 Unauthorized` | ⏳ |
| M12 | Token de un rol sin acceso | `403 Forbidden` | ⏳ |
| M13 | Token rol 2 | `200 OK` con el resumen | ⏳ |
| M14 | Token rol 1 | `200 OK` con el resumen | ⏳ |
| M15 | Usuario deshabilitado después del login | Deshabilitarlo desde Usuarios y Permisos y repetir con su token → `401` | ⏳ |
| M16 | Repetir M10 y M12 con `GET /pagos/pendientes`, `GET /pagos/cuotas/{id}` y `POST /pagos/cobro` | Mismos códigos (el atributo está a nivel de clase) | ⏳ |

## 6. Criterios de aceptación

| Criterio | Estado |
|---|---|
| Opción "Cuotas" en la navegación, solo para administradores | ✅ (existente); ⏳ M1, M2 |
| Ruta protegida por guard de sesión y rol | ✅ F1–F5 |
| Vista con encabezado, métricas/filtros y estado de carga | ✅ F6 (la vista ya existía); ⏳ M5 |
| URL forzada sin rol → "Acceso Denegado: …panel financiero" | ✅ F1; ⏳ M3 |
| Error de conexión con un mensaje claro | ✅ F7–F10; ⏳ M6, M7 |
| Endpoints de cuotas con autorización por rol | ✅ B1, B2; ⏳ M10–M16 |
| 401 y 403 en el backend | ⏳ M10–M12 |

## 7. Observaciones

- El aviso de acceso denegado usa `alert()`, como el resto de los guards del proyecto. El toast
  (`<app-toast>`) vive dentro de `AdminPortal`, que no llega a montarse cuando el guard rechaza.
  Unificarlo requeriría montar un toast global (fuera de alcance).
- Las otras pantallas financieras (`deudas-morosidad`, `jugadores/:id/deuda`, `becados-descuentos`)
  pueden usar el mismo mensaje agregando `data: { accesoDenegado: FINANCIAL_ACCESS_DENIED }` a su ruta.
- Las cargas secundarias de la vista (aranceles vigentes, categorías, lista de jugadores del formulario)
  siguen sin aviso si fallan. No bloquean la consulta de cuotas y están fuera del alcance de esta HU.
