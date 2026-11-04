# Instructivo: cambios de base de datos con migraciones

Los cambios de estructura de la base viajan en Git junto con el código, como archivos SQL
en `database/migrations/`. Al iniciar la API en desarrollo, cada integrante recibe
automáticamente los que le faltan y quedan registrados en su base. **Ya no nos pasamos la
base ni scripts por TXT.**

Este documento sirve para el equipo y para cualquier IA que trabaje en el repositorio
(ver la [sección 8](#8-instrucciones-para-una-ia)).

## 1. Cómo funciona

- La API lee los `.sql` de `database/migrations/` (van incluidos al compilar) y, antes de
  aceptar peticiones, ejecuta en orden los que todavía no figuran en `dbo.__CaccMigraciones`.
- Cada script se ejecuta con su registro en **una transacción**: si falla, se revierte
  completo, no queda registrado y la API no inicia. Los anteriores exitosos quedan aplicados.
- El historial guarda nombre, SHA-256 y fecha UTC. Si alguien edita un script ya aplicado,
  o falta uno que la base tiene registrado, la API se detiene con un mensaje explicativo.
- Un bloqueo en SQL Server impide que dos instancias migren a la vez.
- Está habilitado por defecto solo en `Development`.

**Validación realizada el 29/09/2026** sobre una copia de `ClubCamionerosPRUEBA`: 18 pruebas
unitarias correctas; la API aplicó y registró un script nuevo; un segundo inicio informó cero
pendientes; un script con error se revirtió por completo y detuvo el inicio; editar un script
aplicado detuvo el inicio; la verificación de base común (sección 2.3) rechazó una base
desactualizada y aceptó una actualizada.

## 2. Preparación inicial (una vez por equipo)

### 2.1 Requisitos

- SQL Server (LocalDB, Express o Developer) con una base de desarrollo existente.
  **Las migraciones no crean la base desde cero.**
- SDK de .NET 9 (el que pide `ApiGestion.csproj`). Para el frontend, Node.js 24.15 o superior.

### 2.2 Configuración local (fuera de Git)

1. Copiá `ApiGestion/appsettings.Development.json.example` como
   `ApiGestion/appsettings.Development.json`. Ese archivo está en `.gitignore`.
   Sin él la API no inicia: exige `Jwt:Issuer` y `Jwt:Audience`.
2. Configurá la clave JWT y tu conexión con user secrets, desde la raíz del repositorio.
   Ajustá servidor y base a tu instalación (con LocalDB: `(localdb)\MSSQLLocalDB`;
   con Express: `localhost\SQLEXPRESS`):

```powershell
dotnet user-secrets set "Jwt:Key" "<clave-propia-de-al-menos-32-caracteres>" --project ApiGestion
dotnet user-secrets set "ConnectionStrings:ConexionSQL" "Server=(localdb)\MSSQLLocalDB;Database=ClubCamionerosPRUEBA;Trusted_Connection=True;TrustServerCertificate=True" --project ApiGestion
```

3. Generá la credencial de integración con el Portal Deportivo (HU-055; sin ella la API no inicia).
   Ver la [sección 7.1](#71-credencial-de-integración-con-el-portal-deportivo-hu-055).

Los user secrets tienen prioridad sobre `appsettings.Development.json`, y la variable de
entorno `ConnectionStrings__ConexionSQL` sobre ambos. Si la API se conecta a otra base de la
esperada, revisá esas tres fuentes. No subas claves ni conexiones al repositorio.
Tu usuario SQL necesita permisos para modificar el esquema y usar `sys.sp_getapplock`.

### 2.3 Base común del equipo

Las migraciones parten de una base que ya tiene los scripts manuales históricos de
`database/` hasta `V20260929_07__acceso_administrador_rol_portal.sql` inclusive.
La migración `V20260930_01__verificar_base_equipo.sql` lo comprueba sin modificar nada:
si a tu base le falta algo, la API no inicia y el error lista **qué scripts aplicar**.

En ese caso, una sola vez:

1. Respaldá tu base (`BACKUP DATABASE ... WITH CHECKSUM`).
2. Aplicá manualmente, en orden, **solo los scripts listados** desde SSMS o sqlcmd.
   Reemplazá la línea `USE ClubCamionerosPruebaCuatro;` por el nombre de tu base.
   Con sqlcmd, activá los identificadores entre comillas (los índices filtrados lo requieren):

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -d ClubCamionerosPRUEBA -E -I -b -i "database\V20260929_07__acceso_administrador_rol_portal.sql"
```

3. Volvé a iniciar la API. La verificación pasa y queda registrada.

Los scripts históricos son idempotentes (verifican antes de crear), pero **no los copies a
`migrations/`**: usan `GO` y `USE`, que el ejecutor rechaza. Desde `V20260930_01`, todo
cambio nuevo va únicamente como migración.

## 3. Cuando recibís cambios de los demás

1. Detené la API (`Ctrl+C`) y guardá tu trabajo (commit o stash). Revisá con `git status`.
2. Traé los cambios según el flujo del equipo (por ejemplo, `git pull` de tu rama o
   integrar `main`). Si hay conflictos, resolvelos con quien corresponda; no fuerces.
3. Iniciá la API desde la raíz:

```powershell
dotnet run --project ApiGestion --launch-profile http
```

4. Mirá la consola: por cada script nuevo aparece `Migración aplicada: <nombre>` y al final
   `Migraciones verificadas. Scripts aplicados en este inicio: <cantidad>`.

No uses `--no-build` para recibir migraciones: los SQL se incluyen al compilar, y un binario
anterior no los tiene. Iniciar Angular no aplica migraciones.

## 4. Cuando necesitás cambiar la base

### 4.1 Nombre del archivo

Creá el archivo directamente en `database/migrations/` (sin subcarpetas):

```text
VAAAAMMDD_NN__descripcion.sql
V20261001_01__agregar_observaciones_pagos.sql
```

Fecha de ocho dígitos, secuencia de dos, **doble guion bajo**, descripción en minúsculas
sin espacios ni tildes (letras, números y `_`), hasta 200 caracteres en total. La versión
debe ser única y **mayor que todas las existentes**. Avisá al equipo qué versión tomaste:
dos ramas con la misma versión, o una versión menor que otra ya aplicada, detienen el inicio.

### 4.2 Reglas del SQL

Cada archivo es **un único lote** de T-SQL y el ejecutor maneja la transacción. No incluyas:

- `GO`, `USE`, instrucciones SQLCMD (`:r`, `!!`, `$(...)`);
- `BEGIN TRAN`, `SAVE TRAN`, `COMMIT`, `ROLLBACK`;
- `SET NOEXEC`, `SET PARSEONLY`, `SET IMPLICIT_TRANSACTIONS`, `SET XACT_ABORT`.

La validación es conservadora: rechaza esas palabras **también dentro de comentarios o textos**
(por ejemplo, un comentario con la palabra "use" suelta). No uses SQL dinámico para eludir
estas reglas ni operaciones que no puedan participar de una transacción.

Preferí cambios incrementales e idempotentes (`IF COL_LENGTH(...) IS NULL`, `IF OBJECT_ID(...) IS NULL`).
Un `IF NOT EXISTS` evita duplicados pero no garantiza compatibilidad: verificá tipos,
nulabilidad y restricciones que espera el código. No borres ni recrees tablas con datos.
Para columnas obligatorias en tablas con registros, definí antes cómo completar los existentes.
No inventes importes, contraseñas, usuarios ni otros valores de negocio.

### 4.3 Cómo convertir un script que usaba `GO`

`GO` separaba lotes. Cada lote pasa a ser **su propio archivo**, con versiones consecutivas.
Hace falta separar cuando:

- agregás una columna y en el mismo script la usás (UPDATE, índice, restricción): SQL Server
  compila el lote completo antes de ejecutarlo y no conoce la columna nueva;
- creás o modificás un `TRIGGER`, `PROCEDURE`, `VIEW` o `FUNCTION`: debe ser la primera
  instrucción de su lote (se admiten comentarios antes).

Ejemplo: un script con `ALTER TABLE ... ADD` + `GO` + `UPDATE` + `GO` + `CREATE OR ALTER TRIGGER` se escribe así:

```text
V20261001_01__agregar_columna_x.sql      -> IF COL_LENGTH(...) IS NULL ALTER TABLE ... ADD ...;
V20261001_02__completar_columna_x.sql    -> UPDATE ... SET ...;
V20261001_03__trigger_auditoria_x.sql    -> CREATE OR ALTER TRIGGER ...
```

Eliminá la línea `USE ...;` y cada `GO`. Las instrucciones que sí comparten lote pueden
quedar en el mismo archivo.

### 4.4 Probar y subir

1. Iniciá la API contra tu base de desarrollo y comprobá la estructura y la funcionalidad.
   Si el script falla, corregilo **solo si no se aplicó en ninguna otra base**.
2. Agregá el SQL y el código relacionado **en el mismo commit**, ruta por ruta:

```powershell
git add -- database/migrations/V20261001_01__agregar_observaciones_pagos.sql
git diff --cached
git commit -m "feat(db): agregar observaciones de pagos"
```

3. Subí tu rama e integrala por el proceso habitual. Los demás la reciben con la sección 3.

## 5. Cómo comprobar qué se aplicó

Consulta de solo lectura en SSMS o sqlcmd sobre tu base:

```sql
SELECT Nombre, AplicadaEnUtc FROM dbo.__CaccMigraciones ORDER BY Nombre;
```

**Nunca edites ni borres este historial**, ni registres un script a mano para ocultar errores.

## 6. Reglas y problemas frecuentes

| Situación | Qué hacer |
|---|---|
| Un script aplicado necesita corrección | No se edita, renombra ni borra, aunque solo lo haya aplicado un compañero. Creá otro script. |
| "La base no tiene la estructura común del equipo" | Seguí la sección 2.3. |
| "Falló la migración ..." | La transacción se revirtió. Leé el error SQL interno. Corregí el archivo solo si nadie más lo aplicó. |
| "... fue modificada" | Restaurá el contenido original del archivo (`git checkout -- <ruta>`) y corregí con un script nuevo. |
| "... no está en esta versión del código" | Tu rama no tiene un script que tu base ya aplicó. Actualizá la rama. Cambiar de rama no deshace cambios en la base. |
| Versión duplicada o anterior a la última aplicada | Coordiná con el equipo. Solo se renumeran scripts que nadie aplicó. |
| Error de conexión, login o permisos | Revisá servidor, base y las tres fuentes de configuración (2.2). No se resuelve deshabilitando migraciones. |
| Otra instancia migrando | El bloqueo espera hasta 60 s; cada script tiene 120 s de límite. Reintentá al terminar. |
| Falta `Jwt:Issuer`/`Jwt:Audience` al iniciar | Falta `appsettings.Development.json` (2.2). |

Cada integrante conserva sus propios registros: las migraciones sincronizan **estructura** y
datos incluidos explícitamente en scripts, no los datos que carga cada uno.

## 7. Configuración y producción

**Producción:** la API no migra sola ni se conecta con la cuenta administradora. Las migraciones se
aplican en cada despliegue con `--DatabaseMigrations:SoloMigrar=true` y la cuenta administradora, y la API
trabaja con el usuario técnico `cacc_api`. Pasos completos y scripts en `database/produccion/README.md`.

`DatabaseMigrations:Enabled` (variable `DatabaseMigrations__Enabled`) permite forzar el
comportamiento: `true` las habilita en cualquier entorno, `false` las desactiva. Sin
configurar, solo corren en `Development`. Desactivarlas omite también la verificación del
historial; no revierte cambios ni corrige una base incompatible.

En producción se requiere un procedimiento controlado: identificar la base destino,
respaldar, habilitar explícitamente y verificar compatibilidad antes de usar el código nuevo.

Al terminar el proyecto conviene conservar las migraciones y `dbo.__CaccMigraciones` como
registro de la evolución de la base. Retirar el mecanismo requiere un cambio de código
coordinado (llamada en `Program.cs`, clases de `ApiGestion/Database/`, recursos embebidos del
`.csproj`, pruebas y documentación): no alcanza con borrar una carpeta, y no afecta los datos.

### 7.1 Credencial de integración con el Portal Deportivo (HU-055)

La API de integración (`/api/integracion/v1/...` y su alias `/api/estado-deuda/...`) exige, además del token del usuario, la
credencial del **sistema** Deportivo en el header `X-Integration-Key`. Solo esas rutas: en el resto del Portal la credencial
no sirve para nada. No es un usuario ni da permisos. Contrato: `docs/integracion/README.md`.

| API | Configuración | Variable de entorno |
|---|---|---|
| Portal Administrativo (la valida) | `Integracion:ClienteDeportivo:Clave` (y `Integracion:ClienteDeportivo:ClaveAnterior`, solo durante una rotación) | `Integracion__ClienteDeportivo__Clave`, `Integracion__ClienteDeportivo__ClaveAnterior` |
| Portal Deportivo (la manda, desde su backend) | `AdministrativoApi:ClaveIntegracion` | `AdministrativoApi__ClaveIntegracion` |
| `docker-compose.yml` (Administrativo) | | `INTEGRACION_CLAVE_DEPORTIVO` |

Las dos APIs tienen que tener **el mismo valor**. Tiene que ser una clave al azar de al menos 32 bytes: sin ella (o con
`1234`, vacía o el texto `REEMPLAZAR...` del ejemplo) la API **no arranca** y el error nombra la configuración, nunca el valor.
Tampoco arranca si la clave tiene espacios o un salto de línea al principio o al final (pasa al copiarla de un archivo): el
servidor recorta esos espacios del header, así que nunca coincidiría y todas las llamadas darían 401.

**Development** (cada uno la suya, en user-secrets, nunca en un archivo del repo):

```powershell
$b = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
$clave = [Convert]::ToBase64String($b)
dotnet user-secrets set "Integracion:ClienteDeportivo:Clave" $clave --project ApiGestion
# y la misma en el Portal Deportivo, si lo corrés en tu máquina:
dotnet user-secrets set "AdministrativoApi:ClaveIntegracion" $clave --project BackEnd\CACC.API
```

**Testing y producción**: una clave **distinta por entorno**, cargada como variable de entorno o secreto del despliegue en
las dos APIs. Nunca la misma que en Development.

**Cómo probar**: `qa/HU-055/e2e_hu055.ps1` (contra la API real, sobre una base de prueba) y
`qa/HU-055/api_a_api_hu055.ps1` (las dos APIs). A mano: un `GET /api/integracion/v1/estado-deuda/dni/<dni>` con token y
sin `X-Integration-Key` tiene que responder `401` con `errorCode: "UNAUTHORIZED"`.

**Rotación** (sin cortar la integración):
1. Generar la clave nueva. En el Administrativo: la actual pasa a `ClaveAnterior` y la nueva va a `Clave`. Reiniciar: acepta las dos.
2. Poner la nueva en el Deportivo (`AdministrativoApi:ClaveIntegracion`) y reiniciarlo.
3. Borrar `ClaveAnterior` del Administrativo y reiniciar: la vieja deja de valer (`401`).
Si se saltea el paso 1, entre el cambio de una API y el de la otra la integración responde `401` y el QR no deja pasar a nadie
(`AdministrativoApi:SiNoResponde = Denegar`). Una clave filtrada se rota igual, sin el paso 1.

**No se versiona**: ni la clave ni archivos que la tengan (`appsettings.Development.json` está en `.gitignore`; el `.example`
solo trae `REEMPLAZAR...`). Nunca va en el frontend, en un `Dockerfile` ni en un log: la API registra los rechazos con endpoint
y correlación, nunca la clave.

### 7.2 Entorno de prueba compartido y pruebas de punta a punta de la integración (HU-059)

Levanta **las dos APIs reales** (Administrativo y Deportivo) sobre **SQL Server real**, con la base unificada de HU-053, y prueba
los flujos de la integración por sus endpoints: autenticación entre sistemas (HU-055), alta (HU-056), baja y deuda (HU-057),
generación de cuotas y formato del contrato (HU-054). El detalle y los resultados están en `QA-HU-059.md`.

**Requisitos.**
- Windows con .NET SDK 10 (compila los proyectos `net9.0`).
- `sqlcmd` y SQL Server LocalDB (u otro SQL Server con autenticación de Windows).
- PowerShell 7 (`pwsh`).
- Git para Windows (usa su `patch.exe`).
- El banco `cacc-unificacion` (<https://github.com/Atzur1/cacc-unificacion>), que tiene los parches del Deportivo.

No hacen falta Docker, Node ni los frontends: ningún flujo de HU-059 pasa por la interfaz.

**Configuración.** Ninguna clave se escribe en un archivo. Cada corrida genera al azar, en memoria, la clave JWT compartida y la
credencial de integración, y se las pasa a las dos APIs como variables de entorno. Cada valor de la tabla se puede cambiar con
su variable de entorno o con el parámetro equivalente:

| Variable | Para qué | Por defecto |
|---|---|---|
| `CACC_E2E_DEPORTIVO_DLL` | `CACC.API.dll` del Deportivo, ya parcheado y compilado | `%TEMP%\cacc-hu059\trabajo\ellos\BackEnd\CACC.API\bin\Debug\net9.0\CACC.API.dll` |
| `CACC_E2E_ADMINISTRATIVO_DLL` | `ApiGestion.dll` | `ApiGestion\bin\Debug\net9.0\ApiGestion.dll` de este repo |
| `CACC_E2E_SQL_SERVIDOR` / `CACC_E2E_BASE` | Servidor y base de prueba | `(localdb)\MSSQLLocalDB` / `ClubCamionerosUnificadaV2_HU059_Prueba` |
| `CACC_E2E_PUERTO_ADMINISTRATIVO` / `CACC_E2E_PUERTO_DEPORTIVO` | Puertos | `5418` / `5419` (y `5421` para el Deportivo con credencial equivocada) |
| `CACC_E2E_CLAVE_CUENTAS` | Clave de las cuentas **ficticias** de `qa/HU-059/datos/01_cuentas_prueba.sql` | la del banco de pruebas |

El nombre de la base tiene que terminar en `Prueba` o `_E2E`: el reset se niega a borrar cualquier otra.

**Arranque (una vez por versión del código).**

```powershell
# 1. El Administrativo
dotnet build ApiGestion
# 2. El Deportivo, en la versión probada. Los parches del banco se probaron sobre afa4e60; el main actual no los acepta (ver QA-HU-059).
git clone https://github.com/NejoCHZ/App-Club-Atletico-Camioneros.git $env:TEMP\cacc-hu059\deportivo
git -C $env:TEMP\cacc-hu059\deportivo checkout afa4e60
# 3. Copia parcheada y compilada del Deportivo, hecha por el banco (no modifica ninguno de los dos repos)
pwsh -ExecutionPolicy Bypass -File <cacc-unificacion>\scripts\armar.ps1 -RepoNuestro <este repo> -RepoEllos $env:TEMP\cacc-hu059\deportivo `
     -Destino $env:TEMP\cacc-hu059\trabajo -Servidor "(localdb)\MSSQLLocalDB" -Base ClubCamionerosUnificadaV2_HU059_Prueba -Recrear
```
`-ExecutionPolicy Bypass` solo hace falta si el banco se bajó como zip: Windows marca sus scripts como descargados.

**Preparación (la hace la suite en cada corrida).** `qa/HU-059/reset_base.ps1`:
1. Borra la base y la vuelve a crear con el esquema unificado (`database/esquema/ClubCamioneros_unificada_crear_base.sql`).
2. Aplica `database/migrations` con el migrador de la propia API (`--DatabaseMigrations:SoloMigrar=true`).
3. Carga las cuentas ficticias y los fixtures (`qa/HU-059/datos/`, DNI 9859xxxx).
4. Verifica el estado inicial.

Tarda unos segundos.

**Ejecución E2E (un solo comando).**

```powershell
pwsh -File qa\HU-059\e2e_hu059.ps1
```
Muestra un resumen por historia (PASS / FAIL / BLOCKED) y el total, y deja `resultado.json` en `%TEMP%\cacc-hu059\ejecucion_<fecha>`.
Con `-Evidencia <archivo>` guarda además una copia.

Sobre el mismo entorno corre también la suite de HU-060 (las altas del Deportivo impactan en el Administrativo): `pwsh -File qa\HU-060\e2e_hu060.ps1`. Hace el mismo reset, carga además `qa/HU-060/datos/03_fixtures_hu060.sql` y comparte con HU-059 la infraestructura de `qa/HU-059/comun.ps1`. Ninguna de las dos suites manda correos: el SMTP apunta a un puerto local cerrado. El detalle está en `QA-HU-060.md`.
El cambio de categoría de HU-056 tiene su propia suite sobre el mismo entorno: `pwsh -File qa\HU-056\e2e_hu056_categoria.ps1` (detalle en `QA-HU-056.md`).
Las suites exigen **PowerShell 7** (`#Requires -Version 7.0`): con Windows PowerShell 5.1 dan resultados falsos (lee los `.ps1` sin BOM como ANSI y `ConvertFrom-Json "[]"` no se enumera).

| Código de salida | Significa |
|---|---|
| `0` | Todo PASS. |
| `1` | Algún FAIL o BLOCKED. |
| `2` | El entorno no se pudo preparar. |

**BLOCKED** quiere decir que una dependencia no está implementada (por ejemplo, HU-056). Nunca cuenta como PASS.

**Reset.** Cada corrida empieza con el reset, así que repetir el comando ya vuelve al estado inicial. Para resetear solo la base,
sin correr pruebas: `pwsh -File qa\HU-059\reset_base.ps1`. No hay DELETE manuales: la base se recrea.

**Diagnóstico.**
- **Logs.** Los de cada API y el del reset quedan en la carpeta de la corrida (`administrativo.log`, `deportivo.log`,
  `reset_base.log`). No tienen la credencial, la clave JWT ni tokens; la suite lo comprueba (`LOG-1`).
- **Healthchecks.** Antes de probar, la suite espera a que estén listos:
  - SQL Server: una consulta a la base.
  - Administrativo: el login, que lee `USUARIOS`.
  - Deportivo: `GET /api/jugadores`, que lee `JUGADORES`.

  Hace polling con un timeout de 90 s (`-TimeoutSegundos`). Si un proceso termina, deja de esperar enseguida.
- **Errores comunes.**
  - Puerto ocupado: cambiar `CACC_E2E_PUERTO_*`.
  - "No encuentro ...dll": falta el paso de Arranque.
  - `armar.ps1` dice que un parche no aplica: el clon del Deportivo no está en `afa4e60`.
  - El reset falla en el migrador: ver `reset_base.log`.

**Cierre.** La suite frena sus APIs al terminar, aunque falle. La base de prueba queda para inspeccionarla. Para borrarla:
`sqlcmd -S "(localdb)\MSSQLLocalDB" -E -Q "DROP DATABASE [ClubCamionerosUnificadaV2_HU059_Prueba]"`. La copia de trabajo se borra
eliminando `%TEMP%\cacc-hu059`.

## 8. Instrucciones para una IA

Si sos una IA trabajando en este repositorio, aplicá estas reglas además de las secciones
anteriores:

1. **Código y migración juntos.** Si la tarea cambia la persistencia (tablas, columnas,
   índices, restricciones, triggers o datos iniciales obligatorios), entregá el código y una
   migración nueva en `database/migrations/` en el mismo cambio. No dejes SQL suelto para
   ejecutar a mano ni pidas recrear la base. Un cambio sin impacto en la persistencia no
   necesita migración.
2. **Usá el mecanismo existente.** SQL Server, SQL versionado y acceso por los DAO de
   `DaoLibrary/` (ADO.NET). No agregues Entity Framework, otra herramienta de migraciones
   ni un ejecutor alternativo.
3. **Antes de escribir,** leé este documento, las migraciones existentes y los archivos
   afectados. Verificá el contrato en `ApiGestion/Database/Migracion.cs` (nombre y SQL
   admitido) y `EjecutorMigraciones.cs`. Elegí una versión mayor que todas las existentes.
4. **Respetá la sección 4.2** al pie de la letra, incluidos comentarios: la validación es por
   expresiones regulares, no un parser.
5. **Consultá antes de actuar.** Explicá qué archivos vas a agregar o modificar y esperá
   aprobación. No hagas commits, no ejecutes cambios en bases compartidas o de producción y
   no modifiques el historial `dbo.__CaccMigraciones`.
6. **Compilar y probar.** Si las reglas de tu entorno o del usuario prohíben compilar o
   ejecutar pruebas, no lo eludas aplicando SQL directamente: entregá los archivos y reportá
   la validación pendiente con el comando de la sección 3.
7. **Informe final.** Indicá archivos de código y SQL, motivo, versión elegida, conservación
   de datos y verificaciones realizadas. Distinguí **preparado** (archivo creado),
   **aplicado** (ejecutado en una base) y **verificado** (comprobado su resultado).

Mensaje breve para pasarle a otra IA:

> Leé database/InstructivoEquipo.md y aplicá sus reglas. Incluí cada cambio de base como una
> migración nueva en database/migrations/ junto con el código, usando el ejecutor existente.
> Conservá los datos y no modifiques migraciones ya aplicadas. Consultá antes de crear
> archivos e informá qué preparaste, qué aplicaste y qué quedó pendiente de validar.
