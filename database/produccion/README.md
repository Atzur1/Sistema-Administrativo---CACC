# Base de datos en producción

Guía para quien despliega el Portal Administrativo del CACC: cómo se maneja la base, qué ejecutar al salir a producción y en cada actualización, y qué no hay que hacer para no romperla.

## 1. Quién entra a la base y con qué cuenta

| Cuenta | Quién la usa | Qué puede hacer |
|---|---|---|
| **Administradora** (`sa` o un login con rol `sysadmin`) | La persona que administra el SQL Server, solo para tareas puntuales | Todo. Se usa para crear la base, aplicar migraciones, ejecutar estos scripts y hacer respaldos. **Nunca va en la configuración de la API.** |
| **`cacc_api`** (usuario técnico) | La API, siempre | Leer y escribir datos de la base del club. **No** puede borrar ni modificar la auditoría ni los pagos anulados, ni cambiar la estructura, ni tocar otras bases. |
| Usuarios del portal (por ejemplo, el SuperAdmin) | Las personas del club | No son usuarios de SQL Server: entran al portal con DNI y contraseña. Esto no cambia. |

Con `cacc_api`, aunque alguien consiga ejecutar SQL a través de la API (un error en el código o una contraseña de conexión filtrada), la bitácora de auditoría no se puede borrar.

## 2. Primera puesta en producción

Requisitos: SQL Server 2016 o superior, acceso con la cuenta administradora y la configuración de la API lista (ver el paso 5).

1. **Crear la base.** Con la cuenta administradora, ejecutá `database/esquema/ClubCamioneros_crear_base.sql`. Si la base de producción va a tener otro nombre, cambiá `ClubCamionerosCACC` en las líneas `CREATE DATABASE` y `USE`.
2. **Aplicar las migraciones.** Desde la carpeta publicada de la API, con la cuenta administradora:

   ```powershell
   dotnet ApiGestion.dll --DatabaseMigrations:SoloMigrar=true --ConnectionStrings:ConexionMigraciones="Server=<servidor>;Database=<base>;User Id=<admin>;Password=<contraseña admin>;TrustServerCertificate=True;"
   ```

   Aplica las migraciones pendientes y termina, sin levantar la API. Necesita la misma configuración que la API (por ejemplo, `Jwt:*`), porque se valida al arrancar.
3. **Crear el usuario técnico y sus permisos.** Con la cuenta administradora, en este orden:
   - `01_crear_login_api.sql` en **master**. Antes, reemplazá la contraseña por una larga y aleatoria, y el nombre de la base.
   - `02_crear_usuario_en_base.sql` en la **base del club**.
   - `03_permisos_usuario_api.sql` en la **base del club**.
4. **Verificar.** Ejecutá `04_verificar_permisos.sql` en la base del club. Todas las filas tienen que decir **OK**.
5. **Configurar la API** (variables de entorno o el gestor de secretos del hosting, nunca en el repositorio):

   | Variable | Valor |
   |---|---|
   | `ASPNETCORE_ENVIRONMENT` | `Production` |
   | `ConnectionStrings__ConexionSQL` | Cadena con **`cacc_api`** y su contraseña |
   | `Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience` | Clave de al menos 32 bytes, propia de producción |
   | `Email__*` | SMTP del club y `Email__PublicBaseUrl` con la URL pública del frontend (HTTPS) |
   | `Cors__AllowedOrigins__0` | URL pública del frontend |
   | `ForwardedHeaders__Enabled` | `true` solo si la API está detrás de un proxy |

   No configures `DatabaseMigrations__Enabled`: fuera de desarrollo, la API no migra sola, y con `cacc_api` no podría.
6. **Arrancar la API** y revisar el log: no tiene que aparecer la advertencia *"La API está conectada a SQL Server con un usuario que puede borrar la auditoría"*.
7. **Primer SuperAdmin.** En `ClubCamioneros_crear_base.sql` hay un bloque opcional al final: se carga la cuenta **sin contraseña**, con el DNI y un correo real. Después, desde la pantalla de ingreso, "¿Olvidaste tu contraseña?" con ese DNI. El enlace llega por correo (requiere el SMTP del paso 5).

## 3. En cada actualización

1. **Respaldo** de la base (sección 4).
2. Publicar la versión nueva de la API **sin arrancarla todavía**.
3. **Aplicar las migraciones** con la cuenta administradora (el comando del paso 2 de la sección anterior).
4. Volver a ejecutar `03_permisos_usuario_api.sql` y `04_verificar_permisos.sql`.
5. Arrancar la API nueva y revisar el log.

Las migraciones se aplican de a una, cada una en su propia transacción: si una falla, se revierte, la API no arranca y el error explica el motivo. Las anteriores quedan aplicadas.

## 4. Respaldos

- Antes de cada actualización y de cualquier cambio manual: `BACKUP DATABASE [<base>] TO DISK = N'<ruta>\<base>_<fecha>.bak' WITH CHECKSUM, COMPRESSION;`
- Recomendado: un respaldo completo diario y guardar copias fuera del servidor.
- Probar la restauración en otro servidor al menos una vez antes de salir a producción. Un respaldo que nunca se restauró no está comprobado.

## 5. Lo que no hay que hacer

- **No** poner la cuenta administradora en la configuración de la API, ni agregar `cacc_api` a `db_owner` o `db_ddladmin`.
- **No** editar, renombrar ni borrar una migración ya aplicada, ni modificar la tabla `__CaccMigraciones`. Cada cambio va en una migración nueva.
- **No** borrar ni modificar filas de `AUDITORIA_CAMBIOS` ni de `PAGOS_ANULADOS`.
- **No** calcular a mano los ids de `PAGOS` ni de `JUGADORES` (`MAX + 1`): los asigna la base con las secuencias `SEQ_PAGOS` y `SEQ_JUGADORES`. Si el otro equipo escribe en esas tablas, tiene que hacer lo mismo.
- **No** cargar contraseñas en `USUARIO.contrasenia`: el portal solo acepta contraseñas creadas con el enlace de activación o de recuperación.
- **No** corregir pagos ni deudas con SQL a mano: para eso están "Anular" (pagos) y la baja de jugadores, que dejan rastro.

## 6. Problemas frecuentes

| Situación | Qué hacer |
|---|---|
| La API no arranca: "migración … fue modificada" | Alguien cambió un script ya aplicado. Restaurarlo desde Git; la corrección va en una migración nueva. |
| Advertencia de permisos en el log | La API está usando la cuenta administradora. Cambiá `ConnectionStrings__ConexionSQL` a `cacc_api`. |
| "Permiso denegado" después de una actualización | Volvé a ejecutar `03_permisos_usuario_api.sql`. |
| Se perdió la contraseña de `cacc_api` | Con la cuenta administradora: `ALTER LOGIN [cacc_api] WITH PASSWORD = '<nueva>';` y actualizar el secreto de la API. |
| Ningún SuperAdmin puede entrar | Usar "¿Olvidaste tu contraseña?". Si el correo de la cuenta está mal, lo corrige el equipo que administra las cuentas. |
