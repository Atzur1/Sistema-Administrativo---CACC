# Cierre técnico: HU-053, HU-055, HU-056, HU-057, HU-059, HU-060 y HU-061

**Fecha:** 29/10/2026 · **Rama:** `FramD` (`cc5e0d8` + cambios sin commitear) · **Sin commits, push ni merge.**
Todas las pruebas corrieron sobre bases de prueba de LocalDB (nombres terminados en `_Prueba` / `_QA_*`). No se escribió en
producción, en `CCACUnificada` ni en las bases de origen: sobre `CACC_B_Origen` y `ClubCamionerosPRUEBA` solo hubo consultas
de lectura.

## 1. Estado por HU

| HU | Estado | Evidencia (29/10, tarde) | Qué falta para cerrarla |
|---|---|---|---|
| **HU-056** Alta y cambio de categoría integrados | **Técnicamente terminada · lista para revisión** | xUnit 43/43 (HU-056) y 720/720 (total); E2E PUT 14/0/0 ×2; E2E alta (HU-060) 43/0/0 ×2 | Code review humano (la implementó Copilot y solo se verificó por comportamiento); adopción del parche por el Deportivo |
| **HU-060** Altas del Deportivo → Administrativo | **Técnicamente terminada · lista para revisión** | E2E **43 PASS · 0 FAIL · 0 BLOCKED** ×2 (antes 14/0/29) | Revisión |
| **HU-057** Baja integrada | **Técnicamente terminada · lista para revisión** (alcance Administrativo) | E2E 41/41 ×2; xUnit 720/720 | Decisión del club sobre bajas retroactivas; la baja *desde* el Deportivo depende de ellos (BUG-059-1) |
| **HU-055** Credencial entre sistemas | **Lista para revisión · BLOCKED para cerrar** | E2E 24/24; API→API 4/4; xUnit 720/720; frontend 449/449 | Code review humano; adopción coordinada en el Deportivo; claves reales de Testing y Producción |
| **HU-059** Entorno E2E compartido | **BLOCKED por el Deportivo** | E2E **35 PASS · 1 FAIL · 0 BLOCKED** ×2 (antes 32/1/5) | BUG-059-1, BUG-059-2 y BUG-059-4 (equipo Deportivo); validación conjunta |
| **HU-061** Permisos Admin/SuperAdmin | **NO lista** | 43 PASS · 1 FAIL · 3 BLOCKED (igual que el 27/10) | Decisión de producto sobre SCRUM-584; RS256 y cookie (HU-UNI-02/03) no existen |
| **HU-053** Unificación A + B | **BLOCKED (negocio)** | No re-ejecutada (sin cambios); última evidencia 91/91; `Unificacion.Integration.Tests` en 720/720 | Decisión del club sobre 4 conflictos + 3 DNI de 9 dígitos; validación conjunta; alcance de datos de producción |

**Terminadas técnicamente:** HU-056, HU-057 (en su alcance) y HU-060.
**Listas para revisión:** esas tres más HU-055.
**Bloqueadas:** HU-053 (negocio), HU-059 (Deportivo) y HU-061 (producto + dependencias inexistentes).

## 2. Funcionalidades implementadas (en la rama, sin commit)

- **HU-053:**
  - script reproducible de unificación (`database/unificacion/`);
  - V07 protegido para que no promueva a SuperAdmin a un usuario de B;
  - parche `ellos-ids-sin-max-mas-uno` para el Deportivo.
- **HU-055:**
  - `X-Integration-Key` además del JWT, con rotación (`ClaveAnterior`);
  - la API no arranca sin clave;
  - auditoría con `origen_sistema` (V20261028_01) y origen visible en la pantalla de auditoría.
- **HU-056:**
  - `POST /api/integracion/v1/jugadores` (alta idempotente en una transacción: persona, jugador, categoría, inscripción y cuota según el día 10);
  - `PUT .../{id}/categoria`;
  - `INTEGRACION_RECHAZOS` sin DNI ni nombres (V20261028_02).
- **HU-057:**
  - `POST .../{id}/baja` y `/reincorporacion`, con fecha efectiva;
  - la misma regla de elegibilidad de cuotas que el resto del sistema;
  - deuda e historial conservados.
- **HU-059:** `reset_base.ps1` y la suite E2E compartida (`comun.ps1`), con secretos al azar y sin correos.
- **HU-060:** la suite E2E de altas, más el **reloj de negocio para pruebas** (`Pruebas:FechaNegocio`, solo en Development o Testing).
- **HU-061:** corrección de SCRUM-583 (el último SuperAdmin no cuenta las cuentas con la activación pendiente).

## 3. Lo que se hizo en este cierre

| # | Objetivo | Resultado |
|---|---|---|
| 1 | Discrepancia del DNI | **Resuelta sin cambiar reglas.** El 6–9 es el rango de **identidades** (CHECK de `PERSONAS`, login, consulta de estado de deuda). El alta de un **jugador** ya era 7 u 8 en el portal (`PlayerRequestDTO`, en `HEAD`) y en el Deportivo. El 7–9 de Copilot no tenía fuente y se alineó a 7 u 8, con un test nuevo (`"123456789"` → 400) y el contrato actualizado (README + OpenAPI). |
| 2 | Calendario de HU-060 | **Resuelto.** `Pruebas:FechaNegocio` fija el "hoy" del servicio de alta. Fuera de Development o Testing la API no arranca (REL-1 lo prueba con `Production`). CUO-2 pasó de BLOCKED a PASS: alta el 05/10, sin cuota del mes. La hora de la máquina no se toca. |
| 3 | El PUT de HU-056 | **Resuelto.** La prueba de Copilot era un comando suelto que no quedó guardado. Se reemplazó por `qa/HU-056/e2e_hu056_categoria.ps1` (14 casos, de punta a punta por el Deportivo). Recrea la base al empezar, así que no hay limpieza final que pueda fallar. Dio 14/0/0 en dos corridas, con código de salida 0. |
| 4 | QA actualizados | QA-HU-053, 055, 057, 059, 060 y 061 tienen una sección "Actualización 29/10, tarde" con PASS, FAIL y BLOCKED actuales; el historial queda debajo. **QA-HU-056 es nuevo.** |
| 5 | Triage de los BUG-059 | Sección 6. |
| 6 | Archivos por commit | Sección 7. |

### Problemas técnicos encontrados y corregidos en el cierre

| Problema | Causa | Corrección |
|---|---|---|
| El alta por integración aceptaba un DNI de 9 dígitos | Rango 7–9 sin fuente (Copilot) | 7 u 8 + test |
| NOJ-1 y NOJ-4 en FAIL en HU-060 | **Error mío:** la suite se corrió con Windows PowerShell 5.1. Lee el `.ps1` sin BOM como ANSI ("Médico" llega roto) y `@(ConvertFrom-Json "[]").Count` da 1. El instructivo pide `pwsh`. | `#Requires -Version 7.0` en las tres suites E2E; con `pwsh` 7.6 los dos pasan |
| HU-061 dio 40/4/3 al re-ejecutarla | Su runner (27/10) es anterior a HU-055: llamaba a `/api/integracion` sin la credencial y recibía 401 antes del chequeo de rol | El runner manda `X-Integration-Key` en esas rutas, como el backend del Deportivo. Vuelve a 43/1/3. |
| Una prueba de servicio falló tras un cambio mío | Validé el nacimiento contra el reloj inyectado, fuera del alcance | **Revertido** (no se tocó la prueba) |

## 4. Decisiones de negocio pendientes (no son técnicas)

1. **HU-053:** qué hacer con los 4 DNI en conflicto (47265479, 49404226, 56893324, 56893332) y con los **3 DNI de 9 dígitos de la base B** (2 jugadores); el alcance de datos de producción, que contradice "base vacía".
2. **HU-061 / SCRUM-584:** si habilitar el acceso Administrativo puede bloquear el login deportivo hasta la activación.
3. **HU-057:** la política de bajas retroactivas, y si hay que llamar a `cargos-alta` después de una reincorporación (RIESGO-1).
4. **HU-059 / BUG-059-4:** qué versión del Deportivo es la de referencia (`DELETE` deshabilitado o implementado).

## 5. Dependencias externas

- **Equipo Deportivo:** BUG-059-1 y BUG-059-2; adoptar los parches `ellos-credencial-integracion` y `ellos-alta-por-integracion`; validación conjunta.
- **Equipo:** code review humano de HU-055, HU-056 y HU-057.
- **Infraestructura:** las claves `INTEGRACION_CLAVE_DEPORTIVO` de Testing y Producción.
- **Inexistentes en Jira:** HU-UNI-02 (RS256) y HU-UNI-03 (cookie), de las que dependen SES-02 y SES-03 de HU-061.

## 6. Triage de los BUG-059

| Bug | ¿Desde nuestro repo? | Situación |
|---|---|---|
| BUG-059-1 baja por `UPDATE` directo | **No.** El código es del Deportivo. Nuestro contrato ya está (HU-057, 41/41). Podemos *proponer* `ellos-baja-por-integracion` en el banco, pero lo tienen que adoptar ellos. | Abierto: BJD-2 FAIL ×2 |
| BUG-059-2 parches vs. su `main` | **No en este repo.** Los parches son nuestros (banco `cacc-unificacion`), pero rehacerlos contra su `main`, o que mergeen PR #22, requiere coordinación. | Abierto, confirmado: 14 fallas de `ellos-definitivo.patch` sobre `3a97b8b` y 0 sobre `afa4e60` |
| BUG-059-3 404 → 503 | **Sí, y ya está.** La causa era que HU-056 no existía. Con la ruta implementada, un 404 con `errorCode` llega como 404 (PUT-6). El 503 solo queda para un 404 sin cuerpo (ruta inexistente), con un log que lo explica. | **Resuelto** |
| BUG-059-4 `DELETE` contradictorio | **No.** Es una decisión conjunta. | Abierto |
| OBS-1 sin `/health` | Sí (mejora, no bloquea) | Abierta |
| OBS-2 clave en `.env.docker.example` | — | No verificable por Claude (acceso denegado). `docker-compose.yml` exige la variable (verificado). |

## 7. Archivos por HU para futuros conventional commits (no ejecutados)

Orden sugerido por dependencias: 007 → 061 → 053 → 055 → docker → 057 → 056 → 059 → 060. Los marcados con **(parcial)** tienen hunks
de varias HU y van con `git add -p`.

**HU-007**, trabajo previo del 27/10 que no está en esta lista, pero que está en la rama y se preserva:
`feat(HU-007): cuentas con acceso a varios portales`
- `ApiGestion/Services/PortalesDeLaCuenta.cs`
- `ApiGestion.Tests/PortalesDeLaCuenta.Tests.cs`
- `ApiGestion/Controllers/AuthController.cs`
- `ApiGestion/Models/LoginResponseDTO.cs`
- `frontend-cacc/src/app/login/login.ts` y `login.spec.ts`
- `frontend-cacc/src/app/models/LoginModel.ts`
- `frontend-cacc/src/app/services/auth.ts`, `auth.spec.ts` y `auth.guards.spec.ts`
- `frontend-cacc/src/app/interceptors/auth-interceptor.spec.ts`
- `qa/HU-007/`

**HU-061** `fix(HU-061): el ultimo SuperAdmin no cuenta cuentas con activacion pendiente (SCRUM-583)`
- `DaoLibrary/UsuariosPortalDao.cs`
- `ApiGestion.Tests/UsuariosPortalDao.Integration.Tests.cs`
- `QA-HU-061.md`
- `qa/HU-061/`, **excluyendo `__pycache__/`**, que no está en `.gitignore`

**HU-053** `feat(HU-053): unificacion reproducible de las bases A y B`
- `database/unificacion/`
- `database/V20260929_07__acceso_administrador_rol_portal.sql`
- `database/README.md`
- `README.md` (SQL Server 2017)
- `ApiGestion.Tests/Unificacion.Integration.Tests.cs`
- `QA-HU-053.md`

**HU-055** `feat(HU-055): credencial de sistema para la API de integracion`
- `ApiGestion/IntegrationClientAuthenticationMiddleware.cs`
- `ApiGestion/Services/IntegrationClientCredentials.cs`
- `ApiGestion/Services/IntegrationKeySecurityFilter.cs`
- `DaoLibrary/SqlAuditActorContext.cs`
- `DaoLibrary/AuditDao.cs`
- `EntityLibrary/AuditEvent.cs`
- `ApiGestion/Models/AuditEventDTO.cs`
- `frontend-cacc/src/app/admin-portal/auditoria/` (los 4 archivos)
- `database/migrations/V20261028_01__auditoria_origen_sistema.sql`
- `ApiGestion.Tests/IntegrationClientAuthentication.Tests.cs`
- `ApiGestion.Tests/IntegracionAuditoriaOrigen.Integration.Tests.cs`
- `ApiGestion/appsettings.Development.json.example`
- `database/produccion/README.md`
- `QA-HU-055.md`
- `qa/HU-055/`
- **(parcial)** `ApiGestion/Program.cs`: Swagger, la credencial y el middleware
- **(parcial)** `ApiGestion/Services/IntegrationErrors.cs`: id de correlación
- **(parcial)** `docs/integracion/README.md` y `openapi-v1.json`: autenticación
- **(parcial)** `database/InstructivoEquipo.md`: §7.1

**Docker**, sin HU identificada (28/10): `chore(docker): despliegue con docker compose`. Va **después** de HU-055, porque usa su clave.
- `.dockerignore`
- `.env.docker.example`
- `docker-compose.yml`
- `ApiGestion/Dockerfile`
- `frontend-cacc/Dockerfile`
- `frontend-cacc/.dockerignore`
- `frontend-cacc/nginx/`
- `.gitignore`
- `Guia-iniciar-Docker-WSL.pdf`, a confirmar si se versiona un PDF

**HU-057** `feat(HU-057): baja y reincorporacion informadas por el Portal Deportivo`
- `ServiceLibrary/JugadoresBajaService.cs`
- `ApiGestion/Models/BajaIntegracionDTO.cs`
- `DaoLibrary/JugadoresDao.cs`
- `DaoLibrary/IJugadoresDao.cs`
- `ApiGestion.Tests/BajaJugadoresIntegracion.Integration.Tests.cs`
- `ApiGestion.Tests/BajaJugadoresIntegracionController.Tests.cs`
- `ServiceLibrary/ResultadoOperacion.cs` (`field`)
- `QA-HU-057.md`
- `qa/HU-057/`
- **(parcial)** `IntegracionJugadoresController.cs`: baja y reincorporación
- **(parcial)** `Program.cs`: DI de la baja
- **(parcial)** `IntegrationErrors.cs`: `field` en los 400
- **(parcial)** README y OpenAPI: baja y reincorporación

**HU-056** `feat(HU-056): alta y cambio de categoria informados por el Portal Deportivo`
- `DaoLibrary/AltaIntegracion.cs`
- `ServiceLibrary/AltaIntegracion.cs`
- `ApiGestion/Models/AltaIntegracionDTO.cs`
- `ApiGestion/Services/RegistroRechazosIntegracion.cs`
- `DaoLibrary/IntegracionRechazosDao.cs`
- `database/migrations/V20261028_02__alta_jugadores_integracion.sql`
- `DaoLibrary/PlayerDao.cs`
- `ServiceLibrary/JugadoresAltaService.cs`
- `ApiGestion.Tests/AltaJugadoresIntegracion.Integration.Tests.cs`
- `ApiGestion.Tests/AltaJugadoresIntegracionController.Tests.cs`
- `ApiGestion.Tests/IntegrationErrorHandling.Tests.cs` (fake de la interfaz)
- `QA-HU-056.md`
- `qa/HU-056/`
- **(parcial)** `IntegracionJugadoresController.cs`: POST y PUT
- **(parcial)** `Program.cs`: DI de los rechazos y del alta
- **(parcial)** README y OpenAPI: alta y categoría

**HU-059** `test(HU-059): entorno compartido y pruebas de punta a punta de la integracion`
- `qa/HU-059/`
- `QA-HU-059.md`
- **(parcial)** `database/InstructivoEquipo.md`: §7.2

**HU-060** `test(HU-060): pruebas E2E de altas y reloj de negocio para pruebas`
- `qa/HU-060/`
- `QA-HU-060.md`
- **(parcial)** `ApiGestion/Program.cs`: el bloque `Pruebas:FechaNegocio`
- **(parcial)** `database/InstructivoEquipo.md`: las líneas de HU-056 y HU-060 y PowerShell 7

**Fuera de los commits, a decidir:**
- `frontend-cacc/package-lock.json` está **staged** desde antes. Solo borra campos `libc`, ruido de versión de npm. Conviene des-stagearlo salvo que sea intencional.
- `CIERRE-TECNICO-HU-053-061.md` (este informe).

## 8. Cómo repetir la evidencia

```powershell
$env:CACC_TEST_DB = "Server=(localdb)\MSSQLLocalDB;Database=CACC_HU056_IT_20261029_1340_Prueba;Trusted_Connection=True;TrustServerCertificate=True;"
dotnet test ApiGestion.Tests                              # 720/720
cd frontend-cacc; npx ng test --watch=false; cd ..        # 449/449
pwsh -File qa\HU-056\e2e_hu056_categoria.ps1              # 14/0/0
pwsh -File qa\HU-060\e2e_hu060.ps1                        # 43/0/0
pwsh -File qa\HU-059\e2e_hu059.ps1                        # 35/1/0 (BUG-059-1)
pwsh -File qa\HU-057\e2e_hu057.ps1                        # 41/41
pwsh -File qa\HU-055\e2e_hu055.ps1                        # 24/24
```

## 9. Revisión de code review (Copilot): 29/10/2026, noche

| # | Hallazgo original | ¿Confirmado? | Corrección aplicada | Archivos | Pruebas | Resultado | Riesgo que queda |
|---|---|---|---|---|---|---|---|
| 1 | HIGH: `reset_base.ps1` inserta `$Base` sin escape y solo exige el sufijo | **Sí, y más grave:** aceptaba `ClubCamionerosPRUEBA`, la base A real, y nombres con inyección | Validación previa a cualquier SQL: identificador estricto `_Prueba`/`_E2E`, bases protegidas, servidor local o lista exacta, entorno Development/Testing, **marca** `CACC_BaseDePrueba` para poder borrar, `QUOTENAME` | `qa/HU-059/seguridad_base.ps1` (nuevo), `reset_base.ps1`, `pruebas_seguridad_reset.ps1` (nuevo) | 28 negativas y positivas; ningún DROP sobre bases reales | **28/28 PASS** | Hay que adoptar una vez (`-AdoptarBaseSinMarca`) cada base de prueba anterior a la marca |
| 2 | HIGH: HTTPS y credenciales | **Sí:** la credencial se validaba antes de la redirección; un HTTP con secretos se procesaba y recibía 307 | `HttpsObligatorioMiddleware`: 403 sin redirección fuera de Development/Testing, antes de la credencial y del JWT; `Seguridad:PermitirHttpSinTls` solo para la máquina local, con aviso | `ApiGestion/HttpsObligatorioMiddleware.cs` (nuevo), `Program.cs`, `docker-compose.yml`, `README.md`, `database/produccion/README.md` | Unitarias + API real en Production (`qa/HU-055/https_produccion_hu055.ps1`) | 9 unitarias PASS; **E2E 11/11** | No se conoce dónde termina el TLS en el despliegue real. El Deportivo debe usar `https://`. El ejemplo de docker usa `PUBLIC_SCHEME=http` por defecto: sin TLS o sin el flag, la API responde 403. Un pedido HTTP ya mandó sus secretos: el servidor no puede impedir eso, solo hacer que falle siempre. |
| 3 | MEDIUM: DNI literal vs. `LTRIM/RTRIM` | **Sí**, como inconsistencia. En la base unificada no se manifiesta (CHECK confiable de solo dígitos) | Búsqueda canónica; el DNI histórico con espacios se reutiliza sin modificarlo; dos personas con el mismo DNI canónico dan 409 de revisión manual (`VariasPersonas`) | `DaoLibrary/PlayerDAO.cs` (= `PlayerDao.cs`), `DaoLibrary/AltaIntegracion.cs`, `ServiceLibrary/JugadoresAltaService.cs`, `AltaJugadoresIntegracion.Integration.Tests.cs` | 2 de integración, rojo con el código anterior y verde con la corrección | PASS | `EstadoDeudaDao.cs:59` (consulta de estado de deuda por DNI) también compara literal; no estaba en el hallazgo y queda mitigado por el mismo CHECK |
| 4 | MEDIUM: headers de proxy | **Sí, como riesgo de configuración:** `KnownNetworks/KnownProxies` vacíos = cualquier origen es proxy. En el docker del repo está mitigado (solo nginx llega al backend) | `ProxyConfiable`: lista explícita (`KnownProxies`/`KnownNetworks`) o, por defecto, loopback y redes privadas, con aviso en el log; una IP o CIDR inválido no deja arrancar | `ApiGestion/Services/ProxyConfiable.cs` (nuevo), `Program.cs`, documentación | Unitarias (spoofing, IPv4 mapeada, lista explícita, CIDR inválido) + E2E H5 a H9 | PASS | **No conocemos el proxy real** (el README menciona Railway). Si llega desde una red pública hay que configurar `KnownNetworks`; si no, todo responde 403. Es seguro, pero rompe hasta configurarlo. |
| 5 | LOW: reloj de negocio | **Sí:** el nacimiento se valida contra `RelojNegocio.Hoy` y el alta contra el reloj inyectado | **Decisión del usuario (opción A):** "no futura" es respecto de la fecha real. La lógica no cambió; se agregó un comentario con la regla y 3 pruebas que la fijan | `ServiceLibrary/JugadoresAltaService.cs` (comentario), `AltaJugadoresIntegracion.Integration.Tests.cs` | 3 de integración; con la opción B aplicada fallan 2 | PASS | En producción las dos fechas son la misma: no hay riesgo |
| 6 | LOW: OpenAPI de `/api/estado-deuda` | **Sí:** la implementación exige credencial + JWT y el documento solo mostraba Bearer | El filtro usa la misma regla que el pipeline (`IsIntegrationRequest`); la seguridad real no cambió | `ApiGestion/Services/IntegrationKeySecurityFilter.cs`, `SeguridadTransporte.Tests.cs` | 3 unitarias, rojo con el filtro anterior | PASS | — |

**Regresión completa después de las correcciones:**

| Suite | Resultado |
|---|---|
| Backend xUnit | **745/745**: 720 anteriores + 2 DNI + 20 de transporte y OpenAPI + 3 de la regla del nacimiento |
| Frontend | **449/449** |
| HU-055 | E2E 24/24 · API→API 4/4 · HTTPS en Production 11/11 |
| HU-056 PUT | 14/0/0 |
| HU-057 | 41/41 |
| HU-059 | 35/1/0 (BJD-2 = BUG-059-1, del Deportivo) · seguridad del reset 28/28 |
| HU-060 | 43/0/0 |

**Para la revisión independiente** (sumar a los commits de la sección 7):

- **HU-055:**
  - `ApiGestion/HttpsObligatorioMiddleware.cs`
  - `ApiGestion/Services/ProxyConfiable.cs`
  - `ApiGestion/Services/IntegrationKeySecurityFilter.cs`
  - `ApiGestion.Tests/SeguridadTransporte.Tests.cs`
  - `qa/HU-055/https_produccion_hu055.ps1` y su salida
  - **(parcial)** `Program.cs`: el bloque del proxy y el de HTTPS
  - **(parcial)** `docker-compose.yml`
  - **(parcial)** `README.md` y `database/produccion/README.md`
- **HU-056:**
  - **(parcial)** `PlayerDAO.cs`: la búsqueda canónica
  - **(parcial)** `DaoLibrary/AltaIntegracion.cs`: `VariasPersonas`
  - **(parcial)** `JugadoresAltaService.cs`: el mapeo a 409
  - **(parcial)** `AltaJugadoresIntegracion.Integration.Tests.cs`: las pruebas y los helpers del DNI crudo
- **HU-059:**
  - `qa/HU-059/seguridad_base.ps1`
  - `qa/HU-059/pruebas_seguridad_reset.ps1` y su salida
  - `qa/HU-059/reset_base.ps1`
