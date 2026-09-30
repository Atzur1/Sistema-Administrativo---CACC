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
