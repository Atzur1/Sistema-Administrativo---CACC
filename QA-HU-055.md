# QA — HU-055: Autenticación entre sistemas para la API de integración

## Revisión de code review: 29/10/2026, noche

La revisión independiente de Copilot dejó tres hallazgos sobre esta HU. Los tres están **confirmados y corregidos**.

| Hallazgo | Qué pasaba | Corrección | Evidencia |
|---|---|---|---|
| **HIGH: HTTPS y credenciales** | En Production, `X-Integration-Key` se validaba **antes** de `UseHttpsRedirection`. Un pedido HTTP con credencial y JWT se procesaba y recién después recibía un 307: los secretos ya habían viajado en claro, y un cliente mal configurado seguía funcionando sin que nadie lo notara. | `HttpsObligatorioMiddleware`: fuera de Development y Testing **rechaza con 403** (no redirige) todo pedido que no sea HTTPS, antes de la credencial y de la autenticación. `Seguridad:PermitirHttpSinTls=true` lo desactiva solo para probar en la propia máquina, con aviso en el log. | Unitarias (`SeguridadTransporte.Tests.cs`); API real en Production: `pwsh qa/HU-055/https_produccion_hu055.ps1` **11/11 PASS** (`salida_https_produccion.txt`) |
| **MEDIUM: headers de proxy** | `KnownNetworks` y `KnownProxies` se vaciaban, así que **cualquier** origen era un proxy confiable. Con la API expuesta, un cliente podía falsear su IP (y saltear el límite de logins) o declararse HTTPS. | `ProxyConfiable`: solo se aceptan los headers de `ForwardedHeaders:KnownProxies` / `KnownNetworks`. Sin lista, solo de loopback y redes privadas (en docker compose solo nginx llega al backend), con aviso en el log. Una IP o red mal escrita no deja arrancar la API. | Unitarias: un cliente de Internet que manda `X-Forwarded-Proto: https` recibe 403 y su IP no cambia; también IPv4 mapeada, lista explícita y CIDR inválido. E2E: H5 a H9 |
| **LOW: OpenAPI de `/api/estado-deuda`** | El alias exige credencial + JWT (lo cubre el mismo middleware), pero quedaba en el documento del Portal, que borraba el esquema de la credencial: solo documentaba Bearer. | `IntegrationKeySecurityFilter` usa la misma regla que el pipeline (`IsIntegrationRequest`): esas operaciones piden credencial + JWT. **La seguridad real no cambió.** | Unitarias; con el filtro anterior la prueba falla por la aserción de los esquemas |

**Cambios de despliegue** (documentados en `database/produccion/README.md` y `README.md`):

- Con TLS terminado adelante, nginx tiene que mandar `PUBLIC_SCHEME=https`.
- Con `PUBLIC_SCHEME=http` (el valor por defecto del ejemplo de docker), la API responde 403 salvo que se active `PERMITIR_HTTP_SIN_TLS=true`.
- El Deportivo tiene que usar `https://` en `AdministrativoApi:BaseUrl`.

**Regresión:** backend 742/742, frontend 449/449, E2E 24/24, API→API 4/4.

---

## Actualización 29/10/2026, tarde: cierre técnico

**Estado: LISTA PARA REVISIÓN (técnicamente terminada); sigue BLOCKED para cerrarse.** Faltan el code review humano, la adopción coordinada en el Deportivo y la carga de las claves reales de Testing y Producción.

| Prueba (re-ejecutada el 29/10) | Resultado |
|---|---|
| Backend completo (`dotnet test ApiGestion.Tests` con `CACC_TEST_DB`) | **720 / 720 PASS** |
| Frontend (`ng test --watch=false`) | **449 / 449 PASS** (34 archivos) |
| `pwsh -File qa/HU-055/e2e_hu055.ps1` | **24 / 24 PASS** |
| `pwsh -File qa/HU-055/api_a_api_hu055.ps1 -DeportivoDll <Deportivo parcheado>` | **4 / 4 PASS** |
| La credencial también protege las rutas nuevas de HU-056 | HU-060 AUT-1 y HU-056 PUT-8: sin credencial o con una inválida, **401** y no se escribe nada |

**Efecto en otras suites:** el runner de HU-061 (27/10) llamaba a `/api/integracion` solo con el JWT, así que ADM-02, CRU-03 y HAB-09 daban 401 por esta HU. Se actualizó para mandar la credencial, como lo hace el backend del Deportivo. Sin otros cambios, vuelve a 43/1/3 (ver QA-HU-061).

---

Rama `FramD` (`cc5e0d8` + cambios sin commitear). Ejecutado junto con Claude Code el 2026-10-28 sobre SQL Server LocalDB,
con la API real levantada sobre una base de prueba (`ClubCamionerosUnificadaV2_HU055_Prueba`, esquema unificado + cuentas
ficticias de `cacc-unificacion/pruebas/seed_cuentas_prueba.sql`). Todas las credenciales de las pruebas se generaron al azar
en cada corrida; ninguna figura en este documento ni en un archivo del repositorio.

**Resultado: HU-055 queda BLOCKED.** La parte técnica está terminada y probada (637/637 pruebas del proyecto, 449/449 del
frontend, 24/24 de punta a punta contra la API real, 4/4 API→API con el Portal Deportivo). Se hizo un code review automático
(sección 7.1) y se corrigió lo que encontró, pero:

1. falta el **code review humano** del equipo;
2. el **otro equipo** tiene que adoptar `ellos-credencial-integracion.patch` en su repo **antes o junto con** el despliegue de
   este cambio: si no, su backend recibe `401` y el QR no deja pasar a nadie.

---

## 1. Conflicto con HU-054 y decisión

El contrato HU-054 (`docs/integracion/README.md`) autenticaba la integración **solo con el JWT del usuario**, y había
descartado explícitamente una credencial de sistema. Ya estaba implementado (`[Authorize(Roles = …)]`) y el Deportivo lo
consumía reenviando el `Bearer`. No existía ninguna API key, ningún header propio ni ningún secreto compartido.

HU-055 pide que no se elija en silencio una alternativa incompatible, así que el conflicto se planteó antes de escribir código.
**Decisión del responsable del equipo: credencial de sistema + JWT del usuario.** La integración exige las dos cosas:

- la credencial identifica al **sistema** que llama (solo el backend del Deportivo la tiene);
- el token sigue identificando a la **persona**, con su alcance por categoría, su auditoría y el corte al cerrar sesión.

El contrato HU-054 y el `openapi-v1.json` se actualizaron con este cambio.

Hallazgo de contexto: la HU dice que el JWT de usuarios usa RS256, pero hoy es **HS256** (`SymmetricSecurityKey` +
`HmacSha256` en `Program.cs` y `AuthController`). No se tocó: no es parte de esta HU.

## 2. Mecanismo

| | |
|---|---|
| Header | `X-Integration-Key` (definido en el contrato HU-054 desde esta versión) |
| Dónde se valida | `IntegrationClientAuthenticationMiddleware`, montado **solo** en la rama de integración (el mismo `UseWhen` de HU-058), **antes** de la autenticación JWT, CORS y rate limiting |
| Comparación | SHA-256 de lo enviado contra SHA-256 de cada clave configurada, con `CryptographicOperations.FixedTimeEquals`, sin cortar en el primer acierto: el tiempo no depende de la clave ni de su largo. La clave no se guarda en memoria (solo su hash) |
| Sin credencial / inválida | `401` `UNAUTHORIZED` con el cuerpo del contrato (`errorCode`, `message`, `exito`, `mensaje`), sin datos; no llega al endpoint |
| Credencial válida | el pedido sigue igual que antes: JWT, roles, validaciones y reglas del endpoint |
| Identidad | ninguna: la credencial no crea `ClaimsPrincipal`, ni roles, ni cookie, ni sesión |
| Rotación | `ClaveAnterior` opcional: mientras está configurada, valen las dos |
| Arranque | sin `Clave`, o con una vacía, de menos de 32 bytes, con espacios o saltos de línea en los extremos, o el placeholder `REEMPLAZAR…`, la API **no arranca**. El mensaje nombra la configuración, nunca el valor. Mismo patrón que `Jwt:Key` |

**Endpoints protegidos** (todos los del contrato):

| Ruta |
|---|
| `GET /api/integracion/v1/estado-deuda/dni/{dni}` |
| `POST /api/integracion/v1/estado-deuda/lote` |
| `POST /api/integracion/v1/jugadores/{idJugador}/cargos-alta` |
| Alias `GET /api/estado-deuda/dni/{dni}` y `POST /api/estado-deuda/lote`: también quedan protegidos. Si no, el alias saltearía la credencial |

## 3. Configuración y almacenamiento

| API | Configuración | Dónde |
|---|---|---|
| Administrativo | `Integracion:ClienteDeportivo:Clave` (+ `ClaveAnterior` opcional) | Development: user-secrets. Testing/Producción: variable `Integracion__ClienteDeportivo__Clave` o secreto del despliegue. Docker: `INTEGRACION_CLAVE_DEPORTIVO` (obligatoria en `docker-compose.yml`) |
| Deportivo (backend) | `AdministrativoApi:ClaveIntegracion` | user-secrets o `AdministrativoApi__ClaveIntegracion`. Sin ella, su API no arranca |

- **Development de esta máquina**: configurado con una clave al azar de 43 caracteres en user-secrets (verificado: la API
  arranca con ella, la acepta y rechaza los pedidos sin ella).
- **Versionado**: solo hay placeholders (`REEMPLAZAR…` en `appsettings.Development.json.example`, `CHANGE_ME…` en el ejemplo
  del Deportivo), y los dos impiden el arranque. Se revisaron 28.339 archivos del repo: **0** contienen el secreto de
  Development, **0** versionados y **0** bundles JS.
- **Frontend**: 0 referencias a la credencial, al header o a `/api/integracion` en `frontend-cacc/src`. El frontend del
  Deportivo tampoco llama a la integración: lo hace su backend (`EstadoDeudaClient`).
- Configuración, cómo probar, rotación y qué no versionar: `database/InstructivoEquipo.md`, sección 7.1.

## 4. Auditoría

Ahora quedan dos dimensiones en `AUDITORIA_CAMBIOS`:

- **Sistema de origen.** Migración `V20261028_01__auditoria_origen_sistema` agrega la columna `origen_sistema`. La API la
  pasa en `SESSION_CONTEXT('origen')` al abrir cada conexión (`SqlConnectionFactory`, igual que `idUsuario`), y la congela el
  trigger `TR_AUDITORIA_IDENTIDAD_ACTOR`. Para la integración el valor es `PortalDeportivo`; para el resto de esta API
  (pantallas y procesos automáticos) es `PortalAdministrativo`. `NULL` = **no pasó por esta API**: la base es compartida con
  el Portal Deportivo (HU-053), así que su backend y los scripts de mantenimiento escriben sin sellar el origen; los eventos
  anteriores a la migración también quedan en `NULL`, porque no se sabe con certeza de dónde vinieron.
- **Operador.** Sigue siendo el usuario del token (`id_usuario` y su identidad congelada).
- **Pantalla de Auditoría.** `GET /api/auditoria/cambios` devuelve el origen en `sourceSystem`. En la lista, lo que llegó
  desde otro sistema lleva la etiqueta *"Desde el Portal Deportivo (integración)"* (lo hecho desde este portal no lleva
  ninguna, porque es lo habitual). El panel de detalle muestra *"Desde"* cuando hay origen; sin origen no se inventa uno.
- **Rollback.** El evento se escribe en la misma transacción que el cambio. Si la operación se revierte, el evento también;
  probado contra SQL Server.
- **Conexiones del pool.** Cada conexión fija el origen explícitamente, así que un pedido del Portal no hereda el origen
  del pedido anterior; probado.
- **Lecturas.** Los `GET` de integración no generan auditoría de negocio (HU-040 no lo exige). Los rechazos de credencial
  quedan en el log técnico.

## 5. Errores

Formato común de integración (HU-054/HU-058):

| Código | Caso |
|---|---|
| `401 UNAUTHORIZED` | Credencial ausente, vacía, inválida, alterada o ya rotada; también la credencial válida sin token |
| `400 INVALID_INPUT` | Credencial válida con un payload inválido |
| `403 FORBIDDEN` | Credencial válida con un rol sin permiso |
| `404 NOT_FOUND` | Recurso inexistente |
| `500` / `503` | Sin cambios (HU-058) |

Ninguna respuesta trae la credencial, tokens ni stack traces (verificado en cada caso de la sección 6).
Log de cada rechazo: fecha y hora UTC, endpoint (la plantilla de la ruta, sin valores), `UNAUTHORIZED`, el motivo
("ausente" o "inválida") y el id de correlación. **Nunca la credencial.**

## 6. Pruebas ejecutadas

### Unitarias y de integración del proyecto (`dotnet test ApiGestion.Tests`, con `CACC_TEST_DB` sobre la base de prueba)
**637 / 637.** Las 607 anteriores más 30 nuevas (25 de la primera entrega y 5 del code review, sección 7.1):

| Clase | Pruebas | Qué cubre |
|---|---|---|
| `IntegrationClientAuthenticationMiddlewareTests` | 12 | Sin credencial; vacía, con espacios, incorrecta, con un carácter de más y en otro caso de letra; enviada dos veces; válida (llega al endpoint y fija el origen, que después se restaura); no crea identidad, roles ni cookie; rotación (valen la actual y la anterior); la vieja después de rotar; log con endpoint, resultado y correlación y sin credencial ni DNI |
| `IntegrationClientCredentialsConfigurationTests` | 14 | No arranca sin clave, vacía, con espacios, `1234`, corta o `REEMPLAZAR…`, y el mensaje no muestra el valor; no arranca con espacios o salto de línea en los extremos (clave actual y anterior); clave anterior débil; clave anterior vacía = no hay; clave fuerte aceptada |
| `IntegracionAuditoriaOrigenIntegrationTests` (SQL Server real) | 4 | Escritura auditada con `PortalDeportivo` y el operador; rollback sin evento; el pool no hereda el origen (vuelve a `PortalAdministrativo`); una escritura del Portal queda con `PortalAdministrativo` y una conexión que no pasa por la API queda en `NULL` |

### Frontend (`ng test`): **449 / 449**
3 pruebas nuevas en `auditoria.spec.ts`: la lista marca lo que llegó desde el Portal Deportivo y no lo hecho desde este
portal; el panel dice desde qué sistema se hizo (2 casos); sin origen no muestra ninguno.

### De punta a punta contra la API real (`qa/HU-055/e2e_hu055.ps1`): **24 / 24 PASS**
| # | Caso | Esperado | Obtenido |
|---|---|---|---|
| 1 | Sin credencial (con token válido) | 401 | 401 |
| 2 | Credencial vacía | 401 | 401 |
| 3 | Credencial inválida / alterada en un carácter | 401 | 401 / 401 |
| 3c–3d | Alias `/api/estado-deuda` y lote sin credencial | 401 | 401 / 401 |
| 4 | Credencial válida + token (DNI y lote) | 200 | 200 / 200 |
| 4c | Credencial válida **sin** token | 401 | 401 |
| 4d | Credencial válida + rol sin permiso (Médica en cargos-alta) | 403 | 403 |
| 5 | Credencial válida + DNI inválido / jugador inexistente | 400 / 404 | 400 / 404 |
| 6 | **Solo la credencial** contra `/api/usuarios/habilitados`, `/api/auditoria/cambios` y `/api/pagos/pendientes` | 401 | 401 / 401 / 401 |
| 7 | Escritura: `cargos-alta` con credencial + SuperAdmin | 200 | 200 |
| 7b | Eventos de esa escritura con origen `PortalDeportivo` y operador SuperAdmin | todos | 1/1 y 1/1 (la inscripción; la cuota la generó antes el proceso automático de la API, que no es parte de la escritura) |
| 7c | La API de Auditoría devuelve `sourceSystem: "PortalDeportivo"` | sí | sí |
| 7d | Repetir `cargos-alta` no duplica | `false/false` | `false/false` |
| 8 | Credencial y tokens en el log de la API | 0 | 0 credenciales, 0 tokens |
| 8b | Rechazos en el log con endpoint y correlación | ≥ 6 | 7 |
| 9 | Development con user-secrets: arranca, acepta su credencial, rechaza sin ella | 200 / 401 | 200 / 401 |
| 10 | Arranque con la credencial vacía / `1234` | no arranca | terminó con error, el mensaje nombra la configuración |

En todos los `401` se verificó que no viniera ningún dato del jugador, ni la credencial, ni un token, ni una traza.

### API → API con el Portal Deportivo (`qa/HU-055/api_a_api_hu055.ps1`): **4 / 4 PASS**
Las dos APIs levantadas sobre la misma base. El Deportivo es `afa4e60` + `ellos-definitivo` + `ellos-nombres-backend` +
`ellos-ids-sin-max-mas-uno` + **`ellos-credencial-integracion`**.

| Caso | Resultado |
|---|---|
| A. El Deportivo con la credencial correcta | su `GET /api/jugadores/{id}` trae `estadoCuota = "AL DÍA"`, que informa el Administrativo |
| B. Con una credencial equivocada | el Administrativo responde 401 (1 rechazo registrado) y el Deportivo muestra `SIN VERIFICAR` |
| C. Sin credencial | el Deportivo no arranca; el mensaje nombra `AdministrativoApi:ClaveIntegracion` |
| D. Credencial en los logs de las dos APIs | no aparece |

Pruebas del Portal Deportivo con el parche: **100 / 100** (95 anteriores + 5 nuevas).

### Regresión
- `ApiGestion.Tests`: 637/637 y e2e 24/24, las dos vueltas a correr después del code review.
- Unificación HU-053 (`database/unificacion/00_ejecutar_prueba.ps1 -ConResolucionDePrueba`), que ya incluye la migración
  nueva: validación PASS y orígenes intactos. **No se volvió a correr** después del code review: la migración solo cambió en
  comentarios. Las dos bases de prueba que la tenían aplicada (`…_HU055_Prueba` y `…_HU053`) se actualizaron: se reaplicó el
  trigger y se registró el hash nuevo, calculado igual que `Migracion.cs`.
- Frontend: 449/449.

## 7. Bugs

| # | Bug | Cómo se encontró | Estado |
|---|---|---|---|
| 1 | El id de correlación del log quedaba truncado: `LogSanitizer` confundía su sufijo de 8 dígitos (`0HN…:00000001`) con un DNI y lo ocultaba | Prueba contra la API real; las unitarias usaban un id ficticio (`correlacion-123`) | Corregido: el id de correlación (que genera el servidor) va como parámetro propio, sin sanitizar. Prueba con el formato real de Kestrel |
| 2 | Una `ClaveAnterior` vacía al terminar una rotación impedía el arranque | Code review propio | Corregido: vacía = no hay clave anterior. Prueba agregada |
| 3 | `cacc-unificacion/scripts/probar.ps1` llamaba a la integración solo con el token: con HU-055 sus verificaciones darían 401 | Revisión del banco | Corregido: manda la credencial que genera `armar.ps1` y suma 2 verificaciones de HU-055. **No ejecutado** (ver 9) |
| 4 | La pantalla de Auditoría **no mostraba** el origen: la API lo devolvía en `sourceSystem`, pero el frontend no lo leía. La primera versión de este documento decía "se ve en la pantalla", y eso no era cierto | Code review (7.1) | Corregido: etiqueta en la lista y fila *"Desde"* en el panel. 3 pruebas |
| 5 | `NULL` en `origen_sistema` se leía como "Portal Administrativo", pero con la base compartida (HU-053) también es lo que deja el backend del Deportivo cuando escribe directo, o un script de mantenimiento | Code review (7.1) | Corregido: la API siempre sella su origen (`PortalAdministrativo` por defecto) y `NULL` queda para lo que no pasó por ella. 1 prueba nueva y 1 ajustada |
| 6 | Una clave configurada con un espacio o un salto de línea en un extremo arrancaba bien, pero nunca coincidía (el servidor recorta el header) y todas las llamadas daban 401 sin ninguna pista | Code review (7.1) | Corregido: la API no arranca y el error lo dice. 4 pruebas |
| 7 | `database/produccion/README.md` no listaba la credencial entre las variables de producción, y el modo `SoloMigrar` también la exige | Code review (7.1) | Corregido en la documentación |

### 7.1 Code review automático (2026-10-28)

`/code-review` (nivel *high*) sobre los archivos de HU-055: 10 hallazgos. Cada uno se verificó contra el código antes de
decidir. **No reemplaza el code review humano.**

| # | Hallazgo | Decisión |
|---|---|---|
| 1 | `NULL` en el origen se confunde con el Portal Administrativo | **Corregido** (bug 5) |
| 2 | La credencial se valida antes del rate limiting: los rechazos no se limitan | **No se cambia.** El límite de integración es un cupo de concurrencia **compartido** (HU-058): si los pedidos sin credencial lo consumieran, cualquiera dejaría sin cupo al Deportivo legítimo. Hoy un rechazo no toca la base y cuesta un SHA-256. Un límite propio para los rechazos sigue en pendientes (9.6) |
| 3 | La credencial se valida antes de la redirección a HTTPS | **No se cambia.** Reordenar no evita que la clave haya viajado en claro: eso lo decide el cliente que llama por `http://`. Con la clave válida, el pedido sigue igual a la redirección y no se ejecuta nada |
| 4 | Una clave con espacios en los extremos nunca coincide | **Corregido** (bug 6) |
| 5 | El alias `/api/estado-deuda` ahora exige la credencial y rompe a quien lo llamaba desde el navegador | **No se cambia: es a propósito.** Si el alias quedaba abierto, la credencial se salteaba. No hay llamadas desde el navegador a estas rutas: 0 coincidencias en el frontend de este portal y en el del Deportivo (clon local, que está en otra rama, no en `main`) y la credencial nunca tiene que estar en un navegador |
| 6 | `SoloMigrar` exige la credencial | **Documentado** (bug 7). Es el mismo patrón que `Jwt:*`, que ya se exigía en ese modo |
| 7 | El frontend no muestra el origen | **Corregido** (bug 4) |
| 8 | La sobrecarga nueva de `IntegrationErrors.Log` duplica la existente | **No se cambia ahora.** Unificarlas cambia el formato del log de HU-058 para los otros errores; queda para el code review humano |
| 9 | El script de esquema unificado no tiene `origen_sistema` | **No se cambia.** Ese script es una foto del 2026-10-17 y las migraciones posteriores se aplican encima (así trabaja el runner de HU-053 y la API al arrancar). Tampoco tiene las otras migraciones posteriores |
| 10 | El 401 no trae `WWW-Authenticate`, y el mensaje decía "caracteres" cuando se cuentan bytes | El mensaje ahora dice **bytes**. `WWW-Authenticate` no se agrega: no hay un esquema estándar para claves en un header propio, y el cuerpo ya trae `UNAUTHORIZED` del contrato |

## 8. Archivos

| Archivo | |
|---|---|
| `ApiGestion/Services/IntegrationClientCredentials.cs` | nuevo: configuración, validación al arrancar y comparación en tiempo constante |
| `ApiGestion/IntegrationClientAuthenticationMiddleware.cs` | nuevo |
| `ApiGestion/Services/IntegrationKeySecurityFilter.cs` | nuevo: el header en el Swagger de integración, solo ahí |
| `ApiGestion/Program.cs` | registro, montaje en la rama de integración, Swagger |
| `ApiGestion/Services/IntegrationErrors.cs` | log con id de correlación |
| `DaoLibrary/SqlAuditActorContext.cs` | origen del pedido y `SESSION_CONTEXT('origen')` (`PortalAdministrativo` por defecto) |
| `frontend-cacc/src/app/admin-portal/auditoria/auditoria.{ts,html,css,spec.ts}` | origen en la lista y en el panel |
| `database/produccion/README.md` | la credencial entre las variables de producción y de `SoloMigrar` |
| `DaoLibrary/AuditDao.cs`, `EntityLibrary/AuditEvent.cs`, `ApiGestion/Models/AuditEventDTO.cs` | origen en la auditoría (`sourceSystem`) |
| `database/migrations/V20261028_01__auditoria_origen_sistema.sql` | nuevo |
| `ApiGestion/appsettings.Development.json.example`, `docker-compose.yml` | placeholder / variable obligatoria |
| `docs/integracion/README.md`, `docs/integracion/openapi-v1.json` | contrato HU-054 actualizado (el OpenAPI regenerado desde Swagger: +11/−2 líneas) |
| `database/InstructivoEquipo.md` | sección 7.1 |
| `ApiGestion.Tests/IntegrationClientAuthentication.Tests.cs`, `ApiGestion.Tests/IntegracionAuditoriaOrigen.Integration.Tests.cs` | nuevos |
| `qa/HU-055/e2e_hu055.ps1`, `qa/HU-055/api_a_api_hu055.ps1` | nuevos |
| `cacc-unificacion/parches/ellos-credencial-integracion.patch` | nuevo: backend del Deportivo (header, configuración obligatoria, 5 pruebas) |
| `cacc-unificacion/scripts/armar.ps1`, `scripts/probar.ps1` | aplican el parche, generan la credencial compartida y la usan |

## 9. Pendientes

| # | Pendiente |
|---|---|
| 1 | **Code review** humano. El automático ya se hizo (7.1); quedan para esa revisión los puntos 2, 3, 8 y 10 de la tabla, que se decidió no cambiar. |
| 2 | **Otro equipo**: aplicar y revisar `ellos-credencial-integracion.patch`, y cargar su `AdministrativoApi:ClaveIntegracion`. Los dos despliegues tienen que salir coordinados. |
| 3 | **Testing y producción**: generar una clave distinta por entorno y cargarla en las dos APIs. Sin ella el Administrativo no arranca. |
| 4 | `.env.docker.example`: falta agregar `INTEGRACION_CLAVE_DEPORTIVO=` (sin valor). No se pudo editar en esta sesión: el acceso a ese archivo está denegado por configuración. |
| 5 | Correr `cacc-unificacion/scripts/armar.ps1` + `probar.ps1` completos (necesitan un clon de B en `main`; el de esta máquina está en otra rama con cambios sin commitear). |
| 6 | Posible hardening, fuera de alcance: límite de intentos para los rechazos de credencial. Hoy un rechazo no toca la base (se resuelve antes del JWT y del cupo de integración) y cuesta un SHA-256. |
| 7 | Fuera de alcance: la HU asume JWT RS256; el código usa HS256. |

## 10. Definición de Listo

| Criterio | Estado | Evidencia |
|---|---|---|
| El mecanismo coincide con HU-054 | PASS | conflicto planteado y decidido; contrato HU-054 y OpenAPI actualizados (sección 1) |
| Los endpoints de integración exigen credencial | PASS | 3 endpoints + alias (casos 1–3d) |
| Sin credencial responden 401 | PASS | casos 1, 3c, 3d |
| Credencial incorrecta responde 401 | PASS | casos 2, 3, 3b; rotada: prueba unitaria |
| No se devuelven datos en esos casos | PASS | verificado en cada 401 |
| La credencial válida permite continuar | PASS | casos 4, 4b, 5, 5b, 7; y no saltea reglas (400, 403, 404) |
| El Portal Administrativo no la acepta como sesión/permisos | PASS | caso 6 (3 rutas), caso 4c, prueba de identidad |
| La credencial no está en el frontend | PASS | sección 3 |
| No está hardcodeada | PASS | solo placeholders, que impiden arrancar |
| No está versionada | PASS | 0 archivos versionados |
| Development configurado | PASS | caso 9 |
| La configuración está documentada | PASS | Instructivo 7.1, contrato HU-054 |
| Escrituras auditadas con origen Deportivo | PASS | casos 7b, 7c y prueba con SQL Server |
| Un rollback no queda auditado como exitoso | PASS | prueba con SQL Server |
| Los tests de autenticación pasan | PASS | 637/637, 449/449, 24/24, 4/4, 100/100 |
| Hubo code review | **BLOCKED** | revisión propia (bug 2) y automática (7.1, bugs 4 a 7 corregidos); falta la del equipo |
| No se duplicó la autenticación de usuario | PASS | el login, el JWT y los roles no cambiaron; la credencial no crea usuarios |

### Estado final: **BLOCKED**
Para cerrarla: code review humano, adopción del parche por el otro equipo (despliegue coordinado) y claves de
Testing/Producción.
