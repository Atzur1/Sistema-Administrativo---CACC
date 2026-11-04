# QA — HU-061: Habilitación y permisos de Administrador y SuperAdmin con usuarios del otro portal

## Actualización 29/10/2026, tarde: cierre técnico

**Estado: NO LISTA (sin cambios de fondo).** Re-ejecutada desde cero sobre `CCACUnificada_QA_HU061`, con el código actual (HU-055, HU-056 y HU-057 incluidas):

| Corrida | Runner | Resultado |
|---|---|---|
| 3 | `run_hu061.py` del 27/10 | 40 PASS · 4 FAIL · 3 BLOCKED. **No es una regresión:** ADM-02, CRU-03 y HAB-09 llaman a `/api/integracion` sin `X-Integration-Key` y HU-055 las corta con 401 antes de mirar el rol (`salida_ejecucion3.txt`). |
| 4 | Runner actualizado: manda la credencial en `/api/integracion` (`Integracion__ClienteDeportivo__Clave`), como el backend del Deportivo | **43 PASS · 1 FAIL · 3 BLOCKED**, igual que el 27/10. ADM-02 → 403, CRU-03 → 404, HAB-09 → 403 (`salida_ejecucion4.txt`, `evidencia_ejecucion4.json`) |

- **FAIL:** HAB-08 = **SCRUM-584**. Habilitar el acceso Administrativo bloquea el login deportivo hasta la activación. Requiere una **decisión de producto**.
- **BLOCKED:** SES-02 (RS256, HU-UNI-02), SES-03 (cookie, HU-UNI-03) y HAB-06 (buzón de prueba). Son dependencias que no existen.
- **SCRUM-583:** corregido; cubierto por `UsuariosPortalDao.Integration.Tests` dentro de 720 / 720, y LAST-05 PASS en la corrida 4.
- **Entorno de la corrida 4:** `Email:LogResetLink=true` en Development, así que **no se envió ningún correo**; el SMTP además apuntaba a un puerto cerrado. La clave JWT y la credencial se generaron al azar.

---

Jira: [SCRUM-337](https://alexandermonelli.atlassian.net/browse/SCRUM-337) (épica SCRUM-329 «Integración con el Sistema Deportivo»).
Rama `FramD` (`cc5e0d8`). Ejecutado junto con Claude Code el 2026-10-27:

- 48 casos HTTP contra la API real (`qa/HU-061/run_hu061.py`), sobre una base descartable con el esquema unificado,
  ejecutados dos veces (antes y después de corregir SCRUM-583);
- matriz de autorización de 64 endpoints × 4 tipos de cuenta;
- regresión: xUnit (unitarias + integración contra SQL Server) y Vitest (frontend).

**Resultado: HU-061 NO está lista.** Se encontraron 2 defectos (uno de severidad alta) y hay criterios bloqueados por
dependencias que no están terminadas (HU-059, autenticación RS256 y sesión por cookie). Detalle en las secciones 8 y 10.

---

## 1. Objetivo

Verificar que una persona que ya existe en la base unificada (cargada por el Portal Deportivo) puede incorporarse al
Portal Administrativo, y que después de habilitada cada petición respeta estrictamente su rol: SuperAdmin todo,
Administrador solo lo de HU-043, y una cuenta solo deportiva nada del Administrativo. Incluye cambio de rol,
deshabilitación con revocación de sesiones, rehabilitación, protección del último SuperAdmin y auditoría.

## 2. Entorno

| | |
|---|---|
| API | `ApiGestion` en Development, `http://localhost:5161`, `dotnet run --no-launch-profile` |
| Base | `CCACUnificada_QA_HU061` en `(localdb)\MSSQLLocalDB`, creada con `database/esquema/ClubCamioneros_unificada_crear_base.sql` (nombre reemplazado). La API aplicó al iniciar las 3 migraciones posteriores al esquema (`V20261018_01`, `V20261018_02`, `V20261021_01`). |
| Variables | `ConnectionStrings__ConexionSQL` → la base QA; `Email__LogResetLink=true` (ningún correo sale a las direcciones ficticias); `GeneracionCuotas__Habilitada=false` (el generador automático no mete eventos en la auditoría durante la prueba). |
| Bases que NO se tocaron | `ClubCamionerosPRUEBA` (la de `appsettings.Development.json`), `CCACUnificada`, `CCACUnificada_Antes` y las copias QA anteriores. |
| Cliente | `python qa/HU-061/run_hu061.py` (solo biblioteca estándar) + `sqlcmd` para leer la base. Usa el token real del login y lo envía como `Authorization: Bearer`, igual que el frontend. No usa `localStorage`/`sessionStorage`. |

**Entorno HU-059: no disponible.** SCRUM-335 está «En curso»; no existe la base compartida con el otro equipo ni la
sección del Instructivo para levantarla. Se usó una base **aislada** construida con el mismo esquema unificado que HU-059
exige (HU-053). Es reproducible (sección 4), pero **no** es el entorno compartido ni tiene datos del Portal Deportivo
real: las «personas del otro portal» se simularon con filas de `PERSONAS`/`USUARIOS` tal como las graba el Deportivo
(`FK_id_rol` cargado, `acceso_portal = 0`, contraseña BCrypt). El criterio «ejecutado sobre HU-059» queda **BLOCKED**.

## 3. Dependencias

Estado real verificado en Jira y en el código de la rama, no por la existencia de archivos.

| Dependencia | Jira | En el código de `FramD` | Para HU-061 |
|---|---|---|---|
| HU-059 entorno de prueba compartido | SCRUM-335 **En curso** | No hay base compartida ni instructivo. | **No disponible** → se usó base aislada. |
| HU-043 Administrador / SuperAdmin | SCRUM-226 **Finalizado** | `[Authorize(Roles = "1")]` / `"1,2"` por acción; guards `adminGuard`/`superAdminGuard` en el frontend. | Disponible. Verificado por API (sección 5). |
| HU-040 / HU-041 auditoría | SCRUM-218 / SCRUM-219 **Finalizado** | Triggers `TR_AUDIT_*` con el operador de `SESSION_CONTEXT('idUsuario')`, que fija `AuditActorMiddleware` → `SqlConnectionFactory`. `GET /api/auditoria/cambios`. | Disponible. |
| HU-UNI-02 autenticación común **RS256** | **No existe** en Jira | `AuthController.GenerarToken` firma con `HmacSha256` y `Program.cs` valida con `SymmetricSecurityKey(Jwt:Key)`. | **No implementado**: el JWT es **HS256**. Sí es un único emisor (esta API) y un único token para los dos portales. |
| HU-UNI-03 sesión compartida por **cookie HttpOnly** | **No existe** | El login devuelve el token en el cuerpo JSON (sin `Set-Cookie`); el frontend lo guarda en `sessionStorage` y lo pasa al Deportivo en el fragmento `#sso=<token>`. | **No implementado**. |
| HU-UNI-04 permisos y navegación | **No existe** | Lo que hay: `rol_portal` (Administrativo) independiente de `FK_id_rol` (Deportivo) y `ClaimsDelToken`, que no le da claims deportivos al Administrador. | Parcial (modelo de datos y claims, sin HU propia). |
| HU-UNI-05 logout / revocación | **No existe** como tal (lo cubrió HU-077, SCRUM-414, Finalizado) | `POST /api/auth/logout` y cada cambio de permisos suben `token_version`; `OnTokenValidated` lo compara en cada pedido. | Disponible. |
| HU-UNI-06 gestión única de cuentas | **No existe** | Una fila de `USUARIOS` por persona y una sola `contrasenia` para los dos portales; la habilita esta API (`UsuariosController`). | Disponible en lo que necesita HU-061. |

Las HU-UNI-0x se buscaron por clave, por texto «RS256», «HttpOnly» y «unificada»: no hay ninguna en el proyecto SCRUM.
Lo que el pedido de esta HU describe como «arquitectura actual» (RS256 + cookie HttpOnly) **no es la arquitectura de
esta rama**. No se implementó acá: está fuera del alcance de una HU de pruebas y requiere su propia historia
(con el otro equipo, porque el Deportivo valida el mismo token).

## 4. Modelo real y datos de prueba

### Cómo se representa hoy

```
PERSONAS (una fila por persona, dni único)
  └─ USUARIOS (una cuenta por persona, UNA contraseña para los dos portales)
       ├─ FK_id_rol → ROLES        permiso DEPORTIVO (1 Administrador (Tesorero), 2 Administrativo, 3 Médico,
       │                           4 Director Técnico, 5 Don QR, 6 Preparador Físico, 7 Coordinador)
       ├─ acceso_portal (bit)      ¿tiene acceso al Portal Administrativo? Nace en 0.
       ├─ rol_portal (1|2|NULL)    permiso ADMINISTRATIVO: 1 SuperAdmin, 2 Administrador
       ├─ activacion_pendiente     1 = habilitada pero todavía no creó su contraseña con el enlace
       ├─ activo                   la cuenta existe y está vigente (lo maneja el Deportivo)
       └─ token_version            se sube al cambiar rol, deshabilitar, activar o cerrar sesión
```

| Estado | Condición en `USUARIOS` | Dónde aparece |
|---|---|---|
| Persona del Deportivo **sin acceso** (candidata) | `activo=1, acceso_portal=0, rol_portal IS NULL, FK_id_rol IN (1,2)` | Usuarios y Permisos → Candidatos |
| Habilitada, **activación pendiente** | `acceso_portal=1, rol_portal=1|2, activacion_pendiente=1` | Habilitados (con marca de pendiente). Login bloqueado. |
| Habilitada y activa | `acceso_portal=1, activacion_pendiente=0` | Habilitados |
| Deshabilitada | `acceso_portal=0, rol_portal` conserva el último rol, `contrasenia IS NOT NULL` | Deshabilitados (se reactiva sin correo) |

- **Sesión:** el login (`AuthDao.ValidarLogin`) deja entrar a quien tiene `acceso_portal=1` **o** un `FK_id_rol` (rol
  deportivo), con `activo=1` y `activacion_pendiente=0`. El token lleva `rol_portal` (`0` sin acceso), `tokenVersion` y,
  como `ClaimTypes.Role`, el rol administrativo (`"1"`/`"2"`) y —salvo para el Administrador— el nombre del rol deportivo.
- **Validación por pedido** (`AuthDao.UsuarioActivoConRol`): `rol_portal = 0` → solo `activo` y `token_version`;
  `rol_portal 1|2` → además `acceso_portal = 1` y que `rol_portal` siga siendo el del token.
- **Último SuperAdmin:** `UsuariosPortalDao.QuedaOtroSuperAdminHabilitado` (dentro de la transacción serializable de
  `CambiarRol` / `Deshabilitar`). En el frontend no hay chequeo propio: solo se deshabilita el botón «Deshabilitar»
  sobre la cuenta propia y se advierte al bajarse el rol a uno mismo.
- **Auditoría:** `TR_AUDIT_USUARIOS` / `TR_AUDIT_TOKEN_ACCESO_CUENTA` / `TR_AUDIT_JUGADORES_DESCUENTOS`, etc. escriben
  `AUDITORIA_CAMBIOS` con `id_usuario`, `email_usuario`, nombre, apellido y DNI del operador, `fecha_utc`, `accion`,
  `entidad`, `id_entidad` y el antes/después en JSON.

### Datos de prueba (todos ficticios)

`qa/HU-061/01_datos_prueba.sql`. Contraseña inicial de todas las cuentas: `Deporte#QA2026`. Contraseña que se crea al
activar: `Ingreso#Seguro2026`. Ningún dato personal real.

| Clave | Persona / DNI | Correo | Rol deportivo | Acceso Administrativo al empezar |
|---|---|---|---|---|
| SA1 | Qa Superadmin Uno / 90610001 | sa1@cacc.test | 1 Administrador (Tesorero) | SuperAdmin |
| CAND | Qa Candidato Deportivo / 90610003 | cand@cacc.test | 2 Administrativo | sin acceso |
| CAND2 | Qa Tesorero Deportivo / 90610004 | cand2@cacc.test | 1 Administrador (Tesorero) | sin acceso |
| DEP | Qa Solo Deportivo / 90610005 | dep@cacc.test | 4 Director Técnico | sin acceso (nunca candidato) |
| CAND3 | Qa Candidato Pendiente / 90610006 | cand3@cacc.test | 1 Administrador (Tesorero) | sin acceso |
| JUG | Qa Jugador Prueba / 90610099 | — | jugador (Masculino, categoría 1) | — |

### Cómo repetirlo

```powershell
powershell -ExecutionPolicy Bypass -File qa\HU-061\00_preparar_entorno.ps1      # borra y recrea SOLO CCACUnificada_QA_HU061
$env:ASPNETCORE_ENVIRONMENT='Development'; $env:ASPNETCORE_URLS='http://localhost:5161'
$env:ConnectionStrings__ConexionSQL='Server=(localdb)\MSSQLLocalDB;Database=CCACUnificada_QA_HU061;Trusted_Connection=True;TrustServerCertificate=True;'
$env:Email__LogResetLink='true'; $env:GeneracionCuotas__Habilitada='false'
dotnet run --project ApiGestion --no-launch-profile > qa\HU-061\api.log 2>&1     # en otra terminal
python qa\HU-061\run_hu061.py --log qa\HU-061\api.log --salida qa\HU-061\evidencia.json
```

La suite asume la base recién preparada (los casos encadenan estados). Tarda unos 3 minutos: espera sola la ventana
del límite de logins (10 por minuto por IP). Si LAST-05 falla, la restauración (LAST-06) hace un quinto pedido de
activación/recuperación y el script espera también la ventana de 15 minutos de ese límite (4 pedidos cada 15 minutos).

## 5. Casos ejecutados y resultados

Ejecución 1 = código original sobre `CCACUnificada_QA_HU061`. Ejecución 2 = con la corrección de SCRUM-583, sobre la
base nueva `CCACUnificada_QA_HU061_R2` preparada desde cero con los mismos scripts.
**Ejecución 1: 43 PASS · 2 FAIL · 3 BLOCKED. Ejecución 2: 43 PASS · 1 FAIL · 3 BLOCKED** (LAST-06 solo corre si
LAST-05 falla: es la restauración del escenario).

Criterio de la matriz: un endpoint **permitido** tiene que responder sin 401/403 y sin 5xx (200, o 400/404/409 en las
escrituras de prueba con id inexistente o cuerpo inválido, que pasan la autorización sin cambiar datos). Un endpoint
**prohibido** tiene que responder exactamente **403**.

### Habilitación de una persona del otro portal

| ID | Precondición | Actor | Acción | Esperado | Real | Estado |
|---|---|---|---|---|---|---|
| ENV-01 | base recién preparada | — | leer el estado inicial | SA1 SuperAdmin; CAND, CAND2, CAND3, DEP sin acceso | coincide | PASS |
| HAB-01 | CAND, CAND2, CAND3 cargados por el Deportivo (`FK_id_rol` 1/2) | SA1 | `GET /api/usuarios/candidatos` | incluye a los 3; no incluye a DEP (Director Técnico) ni a SA1 | `200 [2, 3, 5]` | PASS |
| HAB-02 | CAND solo deportivo | CAND | login con su contraseña deportiva | 200, role 0 | 200, role 0 | PASS |
| HAB-03 | HAB-01 | SA1 | `POST /api/usuarios/{CAND}/habilitar` rol 2 | 200 | 200 «Cuenta habilitada. Se envió un correo…» | PASS |
| HAB-04 | HAB-03 | — | estado en base | misma PERSONA y USUARIO; `acceso_portal 1`, `rol_portal 2`, `activacion_pendiente 1`; sin filas nuevas | persona 2→2; PERSONAS 6→6; USUARIOS 5→5 | PASS |
| HAB-05 | HAB-03 | — | activación **generada** | 1 token ACTIVACION sin consumir, 30 min; enlace `/crear-contrasena` | coincide | PASS |
| HAB-06 | sin SMTP de prueba | — | activación **enviada / recibida** | correo entregado al buzón | no verificable: en Development la API escribe el enlace en el log y no intenta SMTP | **BLOCKED** |
| HAB-07 | HAB-03 | SA1 | listas de Usuarios y Permisos | sale de candidatos; una sola vez en habilitados, con la activación pendiente | coincide | PASS |
| HAB-08 | CAND habilitado, activación pendiente | CAND | login con su contraseña deportiva (para usar el Deportivo) | 200 role 0: los permisos son independientes | **401** «Usuario o contraseña incorrectos.» | **FAIL → SCRUM-584** |
| HAB-09 | token deportivo emitido antes de habilitar | CAND | seguir usándolo | sigue autenticando; su rol deportivo «Administrativo» no consulta estado-deuda → 403 | 403 | PASS |
| HAB-10 | enlace de HAB-05 | CAND | `POST /api/auth/account-access/complete` | 200; `activacion_pendiente 0` | 200; pendiente 0 | PASS |
| HAB-11 | enlace ya usado | CAND | reusar el enlace | 400 | 400 | PASS |
| HAB-12 | CAND activado | CAND | login con la contraseña nueva | 200 role 2; token `rol_portal 2`, roles `["2"]`, sin claims deportivos | coincide | PASS |
| HAB-13 | CAND activado | CAND | login con la contraseña deportiva anterior | 401: una sola contraseña para los dos portales | 401 | PASS |

### SuperAdmin, Administrador y cruce de accesos

| ID | Actor | Acción | Esperado | Real | Estado |
|---|---|---|---|---|---|
| SA-01 | SA1 (SuperAdmin) | 64 endpoints del Portal Administrativo | todos permitidos | 64/64 | PASS |
| ADM-01 | CAND (Administrador) | los mismos 64 | 35 de cobranzas permitidos; los 29 restantes 403 | 64/64 | PASS |
| ADM-02 | CAND (Administrador) | `POST /api/integracion/v1/jugadores/{id}/cargos-alta` | 403 | 403 | PASS |
| CRU-01 | DEP (solo deportivo) | login común | 200 role 0, sin roles `1`/`2` en el token | 200 role 0, roles `["Director Técnico"]` | PASS |
| CRU-02 | DEP | los 64 endpoints | todos 403 | 64/64 | PASS |
| CRU-03 | DEP | `GET /api/integracion/v1/estado-deuda/dni/{JUG}` | sin 401/403 (sus categorías no incluyen al jugador → 404) | 404 | PASS |
| CRU-04 | CAND2 (rol deportivo «Administrador (Tesorero)», sin habilitar) | los 64 endpoints | todos 403 | 64/64 | PASS |

Lo que el **Administrador** puede usar (alcance «A» en `run_hu061.py`): Cuotas y Pagos (`/api/pagos/*` salvo anular,
`/api/players/{id}/enrollment*`), Deudas y Morosidad (`/api/jugadores` lista y detalle, `/api/pagos/deuda*`, reportes de
deudores y de pagos recientes), Becados y Descuentos (`/api/players/discount*` salvo editar, reportes de becados), y los
catálogos que leen esas pantallas (`/api/categorias`, `/api/aranceles/resumen`, `GET /api/enrollmentfees`). Lo que le
responde **403**: Usuarios y Permisos, Auditoría, Resumen General, Recaudación, Cuotas faltantes, historial y programación
de aranceles, alta y baja/reactivación de jugadores, historial de pagos, anular pagos, editar beneficios, configuración de
inscripción y reportes de aranceles y recaudación.

**Navegación (interfaz):** verificada en el código y con las pruebas del frontend, no en un navegador.
`app.routes.ts` protege `/admin/portal` con `adminGuard` (rol 1 o 2) y cada sección que no es de cobranzas con
`superAdminGuard`, incluso cuando se entra por URL directa. Un rol 0 que entra a `/admin/portal` va a `/portales`;
`portalesGuard` lo manda a `irAlInicio`, que lo deriva al Portal Deportivo. Lo cubren `auth.guards.spec.ts` y el resto de
la suite (433/433). **No se hizo la recorrida visual del menú en el navegador:** la seguridad está demostrada por la API,
pero el «no aparece en el menú» se verificó solo en el código.

### Cambio de rol y política de sesión

| ID | Precondición | Actor | Acción | Esperado | Real | Estado |
|---|---|---|---|---|---|---|
| ROL-01 | CAND Administrador con sesión abierta | CAND | `GET /api/estadisticas/resumen-general` | 403 | 403 | PASS |
| ROL-02 | ROL-01 | SA1 | `PUT /api/usuarios/{CAND}/rol` → 1 | 200; `rol_portal 1`; `token_version +1` | 200; versión 1→2 | PASS |
| ROL-03 | token de CAND emitido como Administrador | CAND | usar la sesión anterior | 401 en todo: la sesión vieja no se convierte en SuperAdmin | 401 / 401 | PASS |
| ROL-04 | ROL-02 | CAND | nueva sesión | role 1; endpoints de SuperAdmin 200 | role 1; 200; 200 | PASS |
| ROL-05 | dos SuperAdmin (SA1 y CAND) | SA1 | `PUT …/rol` → 2 y usar la sesión de SuperAdmin | 200; la sesión vieja da 401 | 200; 401 | PASS |
| ROL-06 | ROL-05 | CAND | nueva sesión | role 2; resumen-general 403; pagos 200 | coincide | PASS |

**Cómo funciona con `tokenVersion`:** el JWT no se modifica nunca. `CambiarRol` sube `token_version` en la misma
sentencia que cambia `rol_portal`. En el siguiente pedido, `OnTokenValidated` → `UsuarioActivoConRol` ya no encuentra
la combinación (id, rol del token, versión) y responde **401**. El frontend (`auth-interceptor`) cierra la sesión local, y
el rol nuevo se aplica cuando la persona vuelve a entrar. Esto cumple «el permiso nuevo se aplica en su próxima sesión»
y además **revoca la sesión de inmediato**, que es más estricto (una sesión con el rol viejo no sigue viva hasta que venza
la hora). Es el mismo mecanismo del logout (SES-04) y de la deshabilitación: es consistente.

### Deshabilitación y rehabilitación

| ID | Precondición | Actor | Acción | Esperado | Real | Estado |
|---|---|---|---|---|---|---|
| DES-01 | CAND Administrador con sesión abierta (pagos 200) | SA1 | `POST /api/usuarios/{CAND}/deshabilitar` | 200; `acceso_portal 0`; `token_version +1` | 200; versión 3→4 | PASS |
| DES-02 | token anterior a la baja, todavía no vencido | CAND | `GET pagos/resumen` y `POST …/discount/1/void` | 401 en las dos | 401 / 401 | PASS |
| DES-03 | CAND deshabilitado (conserva su rol deportivo) | CAND | login y uso del Administrativo | sin sesión Administrativa | login 200 **role 0**; pagos 403 | PASS |
| DES-04 | DES-01 | SA1 | listas | en deshabilitados, no en candidatos | coincide | PASS |
| REH-01 | la pantalla propone el rol que tenía | SA1 | `POST /api/usuarios/{CAND}/reactivar` rol 2 | 200; mismo USUARIO y PERSONA; mismo rol; sin filas nuevas | 200; persona 2; rol 2→2; 6→6 / 5→5 | PASS |
| REH-02 | token revocado en DES-01 | CAND | usar la sesión vieja | 401 (no revive) | 401 | PASS |
| REH-03 | REH-01 | CAND | login con la misma contraseña | 200 role 2; pagos 200; resumen-general 403 | coincide | PASS |

- DES-03: en el modelo unificado, una cuenta deshabilitada en el Administrativo **sigue pudiendo iniciar sesión** si tiene
  un rol deportivo. Recibe role 0 y el Administrativo le responde 403 a todo. Es lo correcto: deshabilitar quita el acceso
  Administrativo, no la cuenta. Una cuenta sin rol deportivo recibe 401 en el login.
- REH-01: la regla «recupera el mismo rol» la cumple **la pantalla**, que propone el rol anterior
  (`rolReactivar.set(usuario.rolPortal)`). El backend acepta el rol que elija el SuperAdmin (1 o 2). Es una decisión de
  diseño documentada en `UsuariosController.Reactivar`, no un defecto.
- La sesión vieja no revive porque `Reactivar` no toca `token_version`: el valor que subió `Deshabilitar` queda.

### Último SuperAdmin

| ID | Precondición | Actor | Acción | Esperado | Real (ej. 1) | Real (ej. 2) | Estado |
|---|---|---|---|---|---|---|---|
| LAST-01 | SA1 único SuperAdmin | SA1 | bajarse el rol / deshabilitarse | 409 y 409; SA1 sin cambios en base; sin eventos | 409, 409; base igual; 0 eventos | igual | PASS |
| LAST-02 | LAST-01 | SA1 | la sesión de SA1 sigue funcionando | 200 | 200 | 200 | PASS |
| LAST-03 | CAND2 candidato | SA1 | habilitar CAND2 como SuperAdmin y activarlo | 200, 200, login role 1 | coincide | coincide | PASS |
| LAST-04 | dos SuperAdmin activos | SA1 | bajar, subir y deshabilitar a CAND2 | 200, 200, 200 | coincide | coincide | PASS |
| LAST-05 | SA1 único SuperAdmin **operativo**; CAND3 SuperAdmin con la activación pendiente | SA1 | bajarse el rol | 409 | **200: SuperAdmin operativos = 0**; CAND3 no puede recuperar | 409; SA1 sigue con rol 1 | **FAIL → SCRUM-583 → corregido → PASS** |
| LAST-06 | LAST-05 falló | CAND3 | activar a tiempo y devolverle el rol a SA1 | restauración posible solo con el enlace vigente | 200 (con 15 min de espera por el límite) | no corre | — |

Validación frontend: no existe un chequeo de «último SuperAdmin». Solo se deshabilita el botón «Deshabilitar» sobre la
cuenta propia y se advierte al bajarse el rol uno mismo. La protección es del backend, como corresponde. Que la base no
cambió está comprobado leyendo `USUARIOS` antes y después, y contando los eventos de auditoría.

### Auditoría

| ID | Actor | Acción | Esperado | Real | Estado |
|---|---|---|---|---|---|
| AUD-01 | CAND (Administrador) | `POST /api/players/{JUG}/discount` (beca 50 %) | 201; 1 evento `JUGADORES_DESCUENTOS INSERT` con id, correo y DNI del operador y fecha | 201; `[JUGADORES_DESCUENTOS, INSERT, 2, cand@cacc.test, 90610003, 2026-10-27T18:09:56]` | PASS |
| AUD-02 | CAND (Administrador) | `PUT …/discount` (solo SuperAdmin) | 403 y ningún evento nuevo | 403; 0 eventos | PASS |
| AUD-03 | — | eventos de `USUARIOS` de CAND | uno por operación, con el operador correcto, sin duplicados | 7 = alta del seed (sin operador) + habilitar (SA1) + activar (CAND) + rol→1 (SA1) + rol→2 (SA1) + deshabilitar (SA1) + reactivar (SA1); cada uno con el antes/después de `accesoPortal`/`rolPortal` | PASS |
| AUD-04 | SA1 | `GET /api/auditoria/cambios?entidad=USUARIOS` (HU-041) | 200 con operador, fecha, acción y entidad | 200; `userId`, `userEmail`, `userName`, `userSurname`, `userDni`, `entity`, `entityId`, `action`, `timestampUtc`, `beforeJson`, `afterJson` | PASS |

Las operaciones rechazadas (LAST-01, AUD-02) no dejaron ningún evento. El «resultado» de cada operación se lee en el
antes/después del evento: la auditoría registra cambios de datos, no intentos. Un intento rechazado no se registra, y por
eso nunca aparece como modificación hecha. **Observación (no es defecto):** el logout también sube `token_version` y
deja un evento `USUARIOS UPDATE` con el antes y el después iguales, porque `token_version` no forma parte del JSON
auditado.

### JWT / sesión

| ID | Acción | Esperado | Real | Estado |
|---|---|---|---|---|
| SES-01 | login SA1 | 200, rol 1, token con `tokenVersion` | 200; `rol_portal 1`; `tokenVersion 0`; roles `["1","Administrador (Tesorero)"]` | PASS |
| SES-02 | algoritmo del JWT (HU-UNI-02) | RS256 | **HS256**, clave simétrica `Jwt:Key` | **BLOCKED** (dependencia no implementada) |
| SES-03 | transporte de la sesión (HU-UNI-03) | cookie HttpOnly | token en el cuerpo JSON → `sessionStorage` → `Authorization: Bearer`; al Deportivo, en `#sso=<token>` | **BLOCKED** (dependencia no implementada) |
| SES-04 | `POST /api/auth/logout` y reusar el token | 204; después 401 | 204; 401 | PASS |

SES-02 y SES-03 no se marcan FAIL de HU-061: lo que no cumple es la arquitectura que esta prueba toma como base, y esas
historias no existen. Igual, la revocación funciona con el esquema actual: un solo emisor (esta API) y `token_version`
comparado en cada pedido.

## 6. Evidencias

| Archivo | Contenido |
|---|---|
| `qa/HU-061/salida_ejecucion1.txt` / `evidencia_ejecucion1.json` | Ejecución 1 (código original): línea por caso y JSON con precondición, actor, acción, esperado, real, estado y, en las matrices, el código HTTP de cada uno de los 64 endpoints. |
| `qa/HU-061/salida_ejecucion2.txt` / `evidencia_ejecucion2.json` | Ejecución 2 (con la corrección de SCRUM-583). |
| `qa/HU-061/00_preparar_entorno.ps1`, `01_datos_prueba.sql`, `run_hu061.py` | Entorno, datos y casos, para repetir todo. |

El log de la API no se versiona: tiene los enlaces de activación de un solo uso.

## 7. Correcciones

| Bug | Archivo | Cambio |
|---|---|---|
| SCRUM-583 | `DaoLibrary/UsuariosPortalDao.cs` | `QuedaOtroSuperAdminHabilitado` exige además `activacion_pendiente = 0`. Se extrajeron `CambiarRolCore` y `DeshabilitarCore` (el mismo SQL y el mismo flujo, con el commit y el rollback en el método público, igual que `HabilitarCore`) para probarlos dentro de una transacción que se revierte. |
| SCRUM-583 | `ApiGestion.Tests/UsuariosPortalDao.Integration.Tests.cs` | `UltimoSuperAdmin_UnSuperAdminConActivacionPendienteNoCuentaComoElQueQueda`. Ciclo TDD: falló antes del cambio (`Assert.False` en la línea 190: el rol se cambió) y pasa después. |

SCRUM-584 **no se corrigió**: hace falta una decisión de producto con el otro equipo (opciones en el bug).

## 8. Bugs

| Jira | Título | Severidad | Caso | Estado |
|---|---|---|---|---|
| [SCRUM-583](https://alexandermonelli.atlassian.net/browse/SCRUM-583) | La protección del último SuperAdmin cuenta a un SuperAdmin con la activación pendiente y el club puede quedarse sin SuperAdmin | Alta | LAST-05 | Corregido en la rama `FramD` y re-testeado (PASS); falta el PR. |
| [SCRUM-584](https://alexandermonelli.atlassian.net/browse/SCRUM-584) | Habilitar el acceso Administrativo a una persona del Deportivo le bloquea el login deportivo hasta que activa la cuenta | Media | HAB-08 | Abierto: requiere decisión. |

Los dos quedaron vinculados en Jira como «bloquea a» SCRUM-337. Cada uno tiene descripción, precondiciones, pasos,
esperado, obtenido, evidencia, severidad y componente.

**Observaciones que no se cargaron como bug:**
- El mensaje de HAB-03 dice «Se envió un correo» aunque en Development con `LogResetLink` el correo solo se escribe en el
  log. Pasa únicamente en desarrollo.
- El token del SuperAdmin también lleva el rol deportivo de su cuenta (`"Administrador (Tesorero)"`). Es el diseño de
  `ClaimsDelToken`: un SuperAdmin del Administrativo es también usuario del Deportivo.

## 9. Regresión

| Suite | Comando | Resultado |
|---|---|---|
| Backend: unitarias + integración (SQL Server real) | `CACC_TEST_DB=<CCACUnificada_QA_HU061>` `dotnet test ApiGestion.Tests` | **599/599**, 0 omitidas: incluye login y bloqueo (`LoginThrottle`), claims (`ClaimsDelToken`), cuentas (`AccountAccessDao`, `UsuariosPortalDao`), auditoría (`AuditActorMiddleware`, `AuditReferences`, `AuditoriaController`) y permisos de la base (`UsuarioApi.Permisos`). |
| Frontend | `npx ng test --watch=false` | **433/433** (34 archivos): incluye `auth.spec`, `auth.guards.spec`, `auth-interceptor.spec` y `usuarios-y-permisos.spec`. |
| HU-061 (con la corrección) | `run_hu061.py` sobre `_R2` | 43 PASS · 1 FAIL (SCRUM-584, sin corregir) · 3 BLOCKED. Login, SuperAdmin, Administrador, usuarios deportivos y revocación siguen en PASS. |

## 10. Definición de Listo

| Criterio | Resultado |
|---|---|
| Se utilizó el entorno HU-059 | **NO / BLOCKED**: HU-059 está en curso; se usó una base aislada con el mismo esquema unificado. |
| Un usuario del otro portal puede habilitarse | **SÍ** (HAB-01…HAB-07, HAB-10, HAB-12): se reutilizan la misma persona y la misma cuenta, sin duplicados. **Con el defecto SCRUM-584** (HAB-08). |
| Se verificó la activación | **PARCIAL**: generada y completada SÍ (HAB-05, HAB-10, HAB-11); enviada y recibida **BLOCKED** (HAB-06, sin SMTP de prueba). |
| SuperAdmin accede a todas sus funciones | **SÍ por API** (SA-01, 64/64). La interfaz se verificó en el código y con las pruebas del frontend, no en el navegador. |
| Administrador solo accede a lo de HU-043 | **SÍ por API** (ADM-01, ADM-02). Navegación: guards verificados en el código. |
| Accesos prohibidos devuelven 403 | **SÍ** (ADM-01, CRU-02, CRU-04, AUD-02). |
| Cambio de rol según la política de sesión | **SÍ** (ROL-01…06): la sesión se revoca al instante y el permiso nuevo rige en la próxima. |
| Deshabilitación invalida sesiones anteriores | **SÍ** (DES-01…03). |
| Rehabilitación conserva la identidad y el rol | **SÍ** (REH-01…03); el rol lo propone la pantalla. |
| Último SuperAdmin protegido | **SÍ después de corregir SCRUM-583** (LAST-01…05, ejecución 2). |
| Un usuario solo deportivo no obtiene acceso Administrativo | **SÍ** (CRU-01…04). |
| La auditoría identifica al operador | **SÍ** (AUD-01…04). |
| Resultados documentados | **SÍ** (este documento y `qa/HU-061/`). |
| Cada error cargado como Bug vinculado a HU-061 | **SÍ** (SCRUM-583, SCRUM-584). |
| Regresión aprobada | **SÍ** (599/599, 433/433). |

### Estado final: **NO TERMINADA**

Quedan pendientes:
1. **SCRUM-584** abierto (FAIL en HAB-08): hay que decidir con el otro equipo cómo se habilita a alguien que ya usa el Deportivo.
2. **HU-059** sin terminar: hay que volver a correr `run_hu061.py` sobre la base compartida cuando exista.
3. **Correo de activación** sin verificar de punta a punta: hace falta un SMTP de prueba (por ejemplo, un buzón de pruebas) para comprobar envío y recepción.
4. **RS256 y cookie HttpOnly** (SES-02, SES-03): no hay HU que los implemente. Si es la arquitectura acordada, hay que crear esas historias.
5. **PR** con la corrección de SCRUM-583.
6. **Recorrida visual del menú** en el navegador con un Administrador (opcional; la seguridad ya está demostrada por la API).
