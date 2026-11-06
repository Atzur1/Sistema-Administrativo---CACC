# QA HU-059: entorno de prueba compartido y pruebas de punta a punta de la integración

## Revisión de code review: 29/10/2026, noche

**HIGH: seguridad de `reset_base.ps1`. CONFIRMADO, y más grave de lo informado. Corregido.**

- **El problema:**
  - el chequeo `(?i)(prueba|_e2e)$` **aceptaba `ClubCamionerosPRUEBA`, la base A de origen real**;
  - también aceptaba `x]; DROP DATABASE [ClubCamionerosPRUEBA]; --Prueba`;
  - el nombre iba sin escapar al `DROP DATABASE`, y el servidor no se validaba.
- **Cómo se verificó:** evaluando solo la condición vieja, sin ejecutar el script: los dos nombres pasaban.
- **Corrección** (`qa/HU-059/seguridad_base.ps1`, validación previa a cualquier SQL):
  - el nombre es un identificador simple que termina exactamente en `_Prueba` o `_E2E`;
  - hay una lista de bases protegidas;
  - el servidor tiene que ser local o estar en `CACC_E2E_SERVIDORES_PERMITIDOS` (coincidencia exacta);
  - el entorno tiene que ser Development o Testing;
  - una base existente solo se borra si tiene la marca `CACC_BaseDePrueba`, que el script le pone al crearla. `-AdoptarBaseSinMarca` sirve para adoptar, una vez, una base de prueba anterior;
  - el `DROP` usa `QUOTENAME`.
- **Pruebas negativas:** `pwsh qa/HU-059/pruebas_seguridad_reset.ps1` da **28/28 PASS** (`salida_seguridad_reset.txt`).
  - 21 validaciones sin SQL.
  - 3 corridas de punta a punta con nombres maliciosos que cortan antes de crear su log.
  - Las bases reales quedan con la misma fecha de creación (consulta de solo lectura).
  - La marca se prueba sobre una base descartable propia: una base con nombre de prueba que el script no creó **no se borra**.
- `ClubCamionerosUnificadaV2_HU059_Prueba` (creada por `reset_base` el 29/10) se adoptó una vez con `-AdoptarBaseSinMarca`.

**Regresión:** E2E 35/1/0, sin cambios (el FAIL sigue siendo BJD-2 = BUG-059-1).

---

## Actualización 29/10/2026, tarde: cierre técnico

**Estado: BLOCKED por el Deportivo.** **35 PASS · 1 FAIL · 0 BLOCKED**, igual en dos corridas (`pwsh -File qa\HU-059\e2e_hu059.ps1`; evidencia en `qa/HU-059/salida_ejecucion3.txt`, `salida_ejecucion4.txt` y sus `evidencia_ejecucion{3,4}.json`). El entorno y la suite están terminados.

- **Los 5 BLOCKED pasaron a PASS:** HU-056 ya está implementada en el Administrativo.
- **El único FAIL es BJD-2 = BUG-059-1:** la baja del Deportivo hace un `UPDATE` directo y deja la auditoría con origen `NULL`. Se resuelve en el código del Deportivo.
- **Nuevo:** la suite exige PowerShell 7 (`#Requires -Version 7.0`); con 5.1 da resultados falsos (ver QA-HU-060).

**Triage de los BUG-059** (quién puede resolverlos):

| Bug | Estado al 29/10 | ¿Se resuelve en este repo? | Evidencia |
|---|---|---|---|
| **BUG-059-1** baja por `UPDATE` directo | **Abierto** | **No.** El cambio es en el Deportivo: su `DELETE /api/jugadores/{id}` tiene que llamar a `POST /api/integracion/v1/jugadores/{id}/baja`. Nuestro lado ya está: el contrato de HU-057 pasa 41/41. Podemos **proponer** el parche `ellos-baja-por-integracion` en el banco `cacc-unificacion`, como hicimos con el alta, pero lo tiene que adoptar el equipo Deportivo. | BJD-2 FAIL ×2 (`1|NULL`); `JugadorDao.EliminarAsync` del Deportivo hace `UPDATE JUGADORES SET fecha_baja` |
| **BUG-059-2** parches no aplican sobre su `main` | **Abierto, confirmado** | **No en este repo.** Los parches viven en el banco `cacc-unificacion` (nuestro equipo), pero hay que rehacerlos contra el `main` del Deportivo, o que ellos mergeen la rama de base unificada (PR #22). Requiere coordinación. | `ellos-definitivo.patch`, aplicado desde `BackEnd/`: **14 fallas sobre `main` (3a97b8b), 0 sobre `afa4e60`** (`patch --dry-run`, 29/10) |
| **BUG-059-3** 404 → 503 "no responde" | **Resuelto (causa en este repo)** | **Sí, ya está.** El 404 venía de que HU-056 no existía. El cliente del Deportivo traduce a propósito un 404 **sin cuerpo** (ruta inexistente: versión equivocada del Administrativo) como "no responde" y deja un log explícito; un 404 con `errorCode` lo traduce a 404. | `qa/HU-056/e2e_hu056_categoria.ps1` PUT-6: `404 NOT_FOUND` del Administrativo → **404** del Deportivo; HU-060 ALT-1: 201 |
| **BUG-059-4** `DELETE` deshabilitado en el clon vs. implementado en el parche | **Abierto** | **No.** Hay que decidir con el equipo Deportivo cuál es la versión de referencia. Es una decisión conjunta, no técnica. | Sin cambios desde la mañana |
| OBS-1 sin `/health` | Abierta | Sí, en las dos APIs (mejora; no bloquea) | Sin `MapHealthChecks` en `Program.cs` |
| OBS-2 clave en `.env.docker.example` | **No verificable por Claude** (el acceso al archivo está denegado por configuración) | — | `docker-compose.yml` exige `INTEGRACION_CLAVE_DEPORTIVO` y no arranca sin ella (verificado) |

Lo que sigue abajo es la corrida de la mañana, que se conserva como historial.

---

**Fecha:** 29/10/2026 · **Rama:** `FramD` (cambios sin commitear) · **Base:** `ClubCamionerosUnificadaV2_HU059_Prueba` (LocalDB, recreada en cada corrida)

**Resultado: NO TERMINADA.** El entorno y la suite están hechos, son reproducibles y no necesitan pasos manuales. La última
corrida da **32 PASS · 1 FAIL · 5 BLOCKED**:
- **BLOCKED:** el alta HU-056 no está implementada del lado Administrativo. Bloquea los flujos E2E 1, 4 y 5 del alta.
- **FAIL:** la baja que se inicia en la API del Deportivo no pasa por el contrato de HU-057 (BUG-059-1).
- **Pendiente:** la validación conjunta con el equipo del Deportivo.

---

## 1. Estado de las dependencias

Revisé el estado real de cada una; no di ninguna por terminada solo porque hubiera código.

| Dependencia | Estado real | Efecto en HU-059 |
|---|---|---|
| HU-053 base unificada | Hecha. `database/esquema/ClubCamioneros_unificada_crear_base.sql` es idéntico al del banco `cacc-unificacion` (0 diferencias sin contar CRLF). 25 migraciones. | La base de prueba se crea desde ahí. |
| HU-054 contrato | `estado-deuda`, `cargos-alta` y, desde HU-057, `baja` y `reincorporacion`. | Se valida el formato de errores (CON-1 a CON-4). |
| HU-055 autenticación | Implementada (sin commitear) en el Administrativo. El Deportivo la manda solo con el parche `ellos-credencial-integracion.patch`. | Probada API → API: AUT-1 a AUT-6. |
| HU-056 alta | **No implementada en el Administrativo**: `POST /api/integracion/v1/jugadores` no existe (404 sin cuerpo) y el servicio tiene stubs. El Deportivo ya la llama (parche `ellos-alta-por-integracion.patch`). | **BLOCKED**: ALT-1/2/3, DNI-1, REA-1. |
| HU-057 baja | Implementada en el Administrativo (contrato). **El Deportivo no lo llama**: su baja escribe `fecha_baja` directo en la base. | Contrato PASS. Desde el Deportivo, **FAIL** (BUG-059-1). |
| HU-UNI-08 Docker | `docker-compose.yml` levanta solo el Administrativo (backend y frontend) para producción, sin SQL Server ni Deportivo. No hay HU-UNI-08 en el repo. | No se usa para HU-059. Ver §9. |
| E2E previos | `qa/HU-055/*.ps1`, `qa/HU-057/e2e_hu057.ps1` y, en el banco, `cacc-unificacion/scripts/probar.ps1` y `pruebas/e2e/e2e.js` (Puppeteer, con los frontends). | Se reutiliza el patrón y `armar.ps1` del banco. |
| Seeds / fixtures | `cacc-unificacion/pruebas/seed_cuentas_prueba.sql` (cuentas ficticias). | Copiado al repo, más fixtures propios. |
| CI | No existe en ninguno de los dos repos. | La suite se corre a mano o desde un pipeline futuro (sale con 0/1/2). |

## 2. Arquitectura del entorno

```
                          e2e_hu059.ps1  (orquesta, valida, informa)
                         /        |          \
   HTTP + X-Integration-Key       |           HTTP + JWT
               v                  |                v
  API Administrativa :5418  <---- HTTP ----  API Deportiva :5419   (+ :5421 con credencial equivocada, AUT-6)
  ApiGestion.dll (este repo)  (credencial)   CACC.API.dll (afa4e60 + 5 parches del banco)
               \                                   /
                +-------- SQL Server LocalDB -----+
                 ClubCamionerosUnificadaV2_HU059_Prueba (esquema HU-053 + 25 migraciones + fixtures)
```

- Los servicios se levantan como procesos `dotnet` con su configuración por **variables de entorno**. La clave JWT
  compartida y la credencial de integración se generan al azar en cada corrida, en memoria: no se imprimen, no se guardan y
  no aparecen en los logs (LOG-1).
- El Deportivo se arma con `cacc-unificacion/scripts/armar.ps1` sobre una copia del clon (no modifica ningún repo).
- **Versiones usadas:**
  - Administrativo: `cc5e0d8` más el working tree (con HU-055/057 sin commitear).
  - Deportivo: `afa4e60` más los parches `ellos-definitivo`, `ellos-nombres-backend`, `ellos-ids-sin-max-mas-uno`,
    `ellos-credencial-integracion` y `ellos-alta-por-integracion`.
  - Banco: `cacc-unificacion-main` (zip del 28/10).
  - Herramientas: .NET SDK 10.0.400, PowerShell 7.6.0, sqlcmd 15.0, SQL Server LocalDB 17.0.4025.3.

## 3. Datos de prueba

`qa/HU-059/datos/` contiene dos archivos:
- `01_cuentas_prueba.sql`: cuentas **ficticias** del banco. SuperAdmin 20111222, Médica 30000003, DT 30000004 y dos administrativos.
- `02_fixtures_e2e.sql`: los datos de negocio.

DNI reservados 9859xxxx; ningún dato es real.

| Fixture | Escenario |
|---|---|
| Arancel masculino 1000 y arancel de inscripción masculino 500, ambos desde 01/2026 | Tarifa aplicable (cuota y cargos del alta). |
| F1 98590001: activo desde 01/08, agosto y septiembre pendientes | Baja por el contrato y deuda histórica. |
| F2 98590002: igual que F1 | Baja desde la API del Deportivo. |
| F3 98590003: activo, sin deuda | Control de la generación de cuotas y estado `habilitado`. |
| F4 98590004: persona **sin** jugador | HU-056 "DNI existente". |
| F5 98590006: activo desde 01/07, julio pendiente | Baja el 15/07, para probar la generación manual en meses cerrados posteriores. |
| 98590005: no existe | Persona nueva del alta. |
| Categoría 3 (catálogo del esquema) | Categoría válida. |
| Sin `X-Integration-Key` / con una al azar | Escenarios sin credencial y con credencial inválida. |

Los jugadores de los fixtures se insertan directo porque son **precondiciones**. Los flujos que se prueban (alta, baja,
generación) se ejecutan **siempre por los endpoints**. La base solo se consulta para validar.

## 4. Reset

`qa/HU-059/reset_base.ps1` (unos 2 s):
1. Borra la base y la vuelve a crear con el esquema HU-053: tablas, constraints, SEQUENCE, índices, triggers y vistas.
2. Aplica las migraciones con el **migrador de la propia API** (`--DatabaseMigrations:SoloMigrar=true`).
3. Carga las cuentas y los fixtures.
4. Verifica el estado inicial (`5|4|5|0|1|1` = usuarios | jugadores | pagos | bajas | origen de auditoría | objetos clave).
   Si algo no coincide, falla.

Sin DELETE: la base se recrea. Guarda: solo acepta bases que terminen en `Prueba` o `_E2E`. Comprobado: con `-Base CCACUnificada`
se niega y no toca nada.

La suite hace el reset al empezar cada corrida, así que **repetirla no requiere ningún paso manual**. Lo manual es solo el
*arranque* (compilar y armar el Deportivo), una vez por versión del código: ver el Instructivo, §7.2.

## 5. Escenarios y resultados

Comando: `pwsh -File qa\HU-059\e2e_hu059.ps1`. Corrió dos veces desde cero; la evidencia está en
`qa/HU-059/evidencia_ejecucion{1,2}.json` y `salida_ejecucion{1,2}.txt`.

| Id | Escenario | Resultado |
|---|---|---|
| ENV-1/2/3 | Healthchecks: SQL, Administrativo (login → `USUARIOS`), Deportivo (`GET /api/jugadores` → `JUGADORES`), con polling y timeout de 90 s | PASS |
| AUT-1 | Sin credencial → 401 `UNAUTHORIZED`, sin datos | PASS |
| AUT-2 | Credencial inválida → 401 sin datos | PASS |
| AUT-3 | Credencial válida + token → 200 `habilitado` | PASS |
| AUT-4 | Credencial válida **sin** token → 401 (la credencial no es un usuario) | PASS |
| AUT-5 | API → API: el Deportivo obtiene del Administrativo `INHABILITADO` para F1 (2 cuotas vencidas) | PASS |
| AUT-6 | API → API con un **segundo Deportivo** con credencial equivocada → `SIN VERIFICAR` | PASS |
| CON-1/2/3/4 | Contrato: 400 `INVALID_INPUT` + `field`, 404 `NOT_FOUND` con cuerpo, 403 `FORBIDDEN`; ningún rechazo modificó datos | PASS |
| ALT-1 | Alta de persona nueva desde el Deportivo: persona, jugador, categoría, inscripción, cuota | **BLOCKED** (el Deportivo responde 503; el Administrativo, 404 en `POST /api/integracion/v1/jugadores`) |
| ALT-1b | El alta rechazada no dejó datos a medias (`0\|0\|0\|0`) | PASS |
| ALT-2 / ALT-3 | Aparece en el Administrativo / auditoría con origen | **BLOCKED** |
| DNI-1 | DNI con persona existente: misma persona y un jugador | **BLOCKED**. DNI-1b PASS: la persona no se duplicó ni cambió. |
| REA-1 | Reintento del alta sin duplicar nada | **BLOCKED** |
| BAJ-1 | Baja por el contrato con fecha efectiva 20/09: 200, baja lógica, jugador y persona intactos | PASS |
| BAJ-2 | Un evento de auditoría `UPDATE`, origen `PortalDeportivo`, operador del token | PASS |
| BAJ-3 | Reintento: 200 `yaEstabaDadoDeBaja`, sin un segundo evento | PASS |
| BAJ-4 | Otra fecha → 409, se conserva la primera | PASS |
| BJD-1 | `DELETE /api/jugadores/{id}` del Deportivo: baja lógica (el jugador sigue) | PASS |
| BJD-2 | Esa baja la procesa el Administrativo (auditoría con origen `PortalDeportivo`) | **FAIL**: origen `NULL` (BUG-059-1) |
| BJD-3 | Reintento de la baja desde el Deportivo: sin segunda baja ni otro evento | PASS |
| DEU-1 | Agosto y septiembre de F1 y F2, intactos después de la baja | PASS |
| DEU-2 | Deudas y Morosidad (`GET /api/pagos/pendientes`) sigue mostrando a los dos con 2 cuotas, $2.000 y `fechaBaja` | PASS |
| DEU-3 | `estado-deuda` → `baja` | PASS |
| GEN-1 | Detección de faltantes de septiembre: incluye al control y no a F5 (baja 15/07) | PASS |
| GEN-1b | **Generación manual** de faltantes de agosto y septiembre: el control las recibe (1\|1) y F5 no (0\|0) | PASS |
| GEN-1c | La deuda de julio de F5 sigue | PASS |
| GEN-2 | **Generador automático** (se reinicia el Administrativo con la generación encendida y se espera por polling): octubre al control, 0 a F1 y F5 | PASS |
| GEN-3 | **Cobro por adelantado** de noviembre a F1: 400 y ninguna cuota emitida | PASS |
| GEN-4 | Ninguna cuota posterior al mes de la baja (F1 y F5) | PASS |
| GEN-5 | F2 (baja hoy) recibe solo la cuota del mes de la baja (regla de HU-057) | PASS |
| LOG-1 | Los logs no tienen la credencial, la clave JWT ni tokens | PASS |

**Por historia:**

| Historia | Resultado |
|---|---|
| Entorno | PASS |
| HU-054 contrato | PASS |
| HU-055 autenticación | PASS |
| HU-056 alta | BLOCKED |
| HU-056 DNI existente | BLOCKED |
| HU-056 reintento | BLOCKED |
| HU-057 baja (contrato) | PASS |
| HU-057 baja (desde el Deportivo) | FAIL |
| HU-057 deuda histórica | PASS |
| HU-057 generación de cuotas | PASS |
| Logs | PASS |

**Total: 32 PASS · 1 FAIL · 5 BLOCKED.** Código de salida 1.

**Reproducibilidad:** las dos corridas desde cero dan los 38 casos con el **mismo estado y el mismo valor obtenido**
(0 diferencias). Tardan 9 s y 8 s, sin contar la compilación ni el armado.

**Fallas del entorno:** sin el binario del Deportivo, la suite sale con código **2** ("ENTORNO NO DISPONIBLE"); no corre en verde.

**Condiciones:** se corrió el 29/10/2026, día ≥ 10. Las expectativas que dependen del calendario (último período emitible,
cuota del mes de la baja) se calculan con la fecha del club en cada corrida.

## 6. Bugs y hallazgos

| Id | Severidad | Descripción | Dueño / acción |
|---|---|---|---|
| **BUG-059-1** | Alta | La baja del Deportivo (`DELETE /api/jugadores/{id}`, del parche `ellos-definitivo.patch` del banco) hace `UPDATE JUGADORES SET fecha_baja = hoy` **directo en la base compartida**. No llama a `POST /api/integracion/v1/jugadores/{id}/baja` (HU-057). Consecuencias: (a) la auditoría queda con `origen_sistema = NULL`; (b) la fecha es siempre "hoy", no la efectiva; (c) no se aplican las reglas de la baja del Administrativo (quitar cuotas sin pagos de meses posteriores, cancelar becas vigentes); (d) no queda registro de que la operación vino del Deportivo. Las reglas de cuotas siguen excluyéndolo (`JugadorEnElClub` lee `fecha_baja`) y la deuda queda (DEU-1/2). | Banco `cacc-unificacion` (este equipo) + equipo Deportivo: un parche `ellos-baja-por-integracion` que haga que la baja del Deportivo llame al contrato con la fecha efectiva, igual que hizo `ellos-alta-por-integracion` con el alta. |
| **BUG-059-2** | Alta | Los parches del banco **no aplican sobre el `main` actual del Deportivo** (`3a97b8b`): `ellos-definitivo.patch` falla en `AsistenciasController`, `Program.cs`, `AsistenciaDao`, `IAsistenciaDao`, dos pruebas y `LOCAL_CONFIGURATION.md`. El entorno quedó fijado a `afa4e60`, la versión con la que se probaron los parches, así que **la integración con el `main` vigente del Deportivo no está probada**. | Actualizar los parches contra `main`, o que el Deportivo mergee la rama de base unificada (PR #22). |
| BUG-059-3 | Media | HU-056 no está implementada en el Administrativo. Además, el Deportivo traduce ese 404 del Administrativo (ruta inexistente) como **503 "el Portal Administrativo no responde"**: un mensaje engañoso que va a hacer perder tiempo diagnosticando. | HU-056 (en curso, TDD en rojo). |
| BUG-059-4 | Media | Dos versiones del Deportivo se contradicen. El clon de `feature/HU-UNI-01` (en esta máquina, sin commitear) **deshabilita** el `DELETE` (409, "la baja la gestiona Administración"), y el parche del banco lo implementa como `UPDATE` directo. Ninguna de las dos llama al contrato. | Decidir cuál es la versión de referencia antes de la validación conjunta. |
| OBS-1 | Baja | Ninguna de las dos APIs expone un healthcheck (`/health`). La suite usa sondas funcionales que pasan por SQL. | Recomendado: `MapHealthChecks` con un chequeo de la base en las dos APIs. |
| OBS-2 | Baja | `.env.docker.example` sigue sin `INTEGRACION_CLAVE_DEPORTIVO` (pendiente de QA-HU-055). En esta sesión tampoco se pudo leer: el acceso a ese archivo está denegado por configuración. | Agregarla sin valor. |

**Ajustes de la propia suite durante el trabajo**, para que quede la trazabilidad:
- La primera versión pedía "generar faltantes" del **mes en curso**, y el endpoint solo acepta meses cerrados. Era un error
  del test, no del producto. Se rediseñó con F5 (baja en julio, faltantes de agosto y septiembre).
- Los scripts del banco venían de un zip con marca de descarga y la política de ejecución los bloqueaba. Se corren con
  `-ExecutionPolicy Bypass` solo para ese proceso.

## 7. Configuración y secretos

- No se versiona ningún secreto:
  - La clave JWT y la credencial de integración son aleatorias por corrida y pasan por variables de entorno.
  - La clave de las cuentas ficticias es la publicada en el banco y se puede reemplazar con `CACC_E2E_CLAVE_CUENTAS`.
  - `armar.ps1` escribe un `appsettings.Development.json` con claves al azar **en la copia de trabajo de `%TEMP%`**, fuera de
    los repos. La suite igual las reemplaza por las suyas.
- Todo lo configurable (URLs y puertos, servidor, base, DLLs, timeout, salida) se toma de variables `CACC_E2E_*` o de
  parámetros: Instructivo, §7.2.

## 8. Criterios de la Definición de Listo

| Criterio | Resultado |
|---|---|
| Existe una base unificada de prueba | PASS |
| Contiene el esquema HU-053 | PASS: esquema + 25 migraciones, verificado al resetear |
| Datos reproducibles de ambos sistemas | PASS: cuentas de los dos portales + fixtures |
| El Administrativo puede conectarse | PASS (ENV-2) |
| El Deportivo puede conectarse | PASS (ENV-3) |
| La autenticación de integración funciona | PASS (AUT-3, AUT-5) |
| Sin credencial → 401 | PASS (AUT-1, AUT-2, AUT-6) |
| Alta HU-056 probada E2E | **BLOCKED** (HU-056 no implementada) |
| El jugador aparece en el Administrativo | **BLOCKED** |
| Generación / cargo esperado validado | BLOCKED para el alta. PASS para la generación después de la baja (GEN-*) |
| DNI existente no duplica PERSONA | **BLOCKED** (solo se comprobó que el rechazo no la duplicó) |
| Baja HU-057 probada E2E | **FAIL** iniciada desde el Deportivo (BUG-059-1). PASS por el contrato. |
| El dado de baja deja de recibir cuotas futuras | PASS (automática, manual y adelantada) |
| La deuda histórica permanece | PASS |
| Los tests usan APIs reales | PASS |
| Se usa SQL Server real de test | PASS |
| La suite se repite sin pasos manuales | PASS (el reset va incluido; el arranque es una vez por versión) |
| Existe estrategia de reset | PASS |
| Los resultados son reproducibles | PASS: 2 corridas, 0 diferencias |
| El Instructivo está actualizado | PASS (§7.2) |
| `QA-HU-059.md` está completo | PASS |
| Validado junto con el otro equipo | **PENDIENTE**: no se hizo |

## 9. Limitaciones y pendientes

1. **HU-056** (Administrativo) hay que terminarla. Después la suite corre los escenarios del alta sin cambios: dejan de ser
   BLOCKED solos, porque la sonda detecta que la ruta existe.
2. **BUG-059-1:** falta el parche de baja por integración para el Deportivo. Hasta entonces, BJD-2 sigue en FAIL.
3. **BUG-059-2:** probar contra el `main` vigente del Deportivo.
4. **Validación conjunta:** correr `e2e_hu059.ps1` con el equipo del Deportivo, revisar juntos BUG-059-1/2/4 y acordar la
   versión de referencia.
5. Docker: HU-059 corre sobre LocalDB y procesos `dotnet`. No hay un compose de integración (SQL Server + las dos APIs). Si se
   quiere, un `docker-compose.e2e.yml` puede reutilizar `reset_base.ps1` y la suite con otras URLs (las dos son configurables).
6. Frontends: ningún flujo E2E de HU-059 los necesita. El E2E de interfaz del banco (`pruebas/e2e/e2e.js`, Puppeteer) no se
   corrió.
7. El banco está como zip descargado, fuera de git. Conviene usar un clon de `Atzur1/cacc-unificacion` en un commit fijo.

## Archivos

- `qa/HU-059/e2e_hu059.ps1`: la suite: healthchecks, escenarios, validación de lo persistido, resumen y códigos de salida.
- `qa/HU-059/reset_base.ps1`: el reset.
- `qa/HU-059/datos/01_cuentas_prueba.sql` y `02_fixtures_e2e.sql`.
- `qa/HU-059/evidencia_ejecucion{1,2}.json` y `salida_ejecucion{1,2}.txt`.
- `database/InstructivoEquipo.md`: §7.2 (requisitos, configuración, arranque, preparación, ejecución, reset, diagnóstico, cierre).
