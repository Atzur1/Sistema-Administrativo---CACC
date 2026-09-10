# QA — Bugs y hallazgos de seguridad

Registro de bugs de UI y hallazgos de seguridad detectados durante el testeo
del Sistema Administrativo CACC, junto con su estado y la corrección aplicada
en cada caso. Mantenido en la rama `fix/ui-bugs-general` por Joel Galera (QA)
en conjunto con Claude Code (asistente de IA), que corrió y validó las
pruebas de cada fix.

Cada fila enlaza al commit donde se corrigió (cuando ya está resuelto) o
describe el estado si sigue pendiente.

---

## Bugs de UI

| # | Bug | Descripción | Estado | Commit |
|---|-----|-------------|--------|--------|
| 1 | Logo del login no cargaba | El `<img>` del login apuntaba a una URL externa de Google Drive (`lh3.googleusercontent.com`), que renderizaba de forma inconsistente. Se reemplazó por un asset local (`assets/imagen.png`); de paso se renombró `public/` a `assets/`. | ✅ Resuelto | `c14e7df` |
| 2 | Ícono de mostrar/ocultar contraseña no funcionaba para algunos usuarios | Algunos navegadores dibujan su propio ícono nativo dentro del input de contraseña (Chrome: llave de autocompletado; Edge/IE legacy: botón de revelar/limpiar), superpuesto en el mismo lugar que el ícono custom del ojo — el click le llegaba al del navegador, no al de la app. Se agregó `z-index` al ícono custom, padding reservado en el input, y se ocultan los íconos nativos de Chrome/Safari y Edge vía CSS. | ✅ Resuelto | `17a5dc0` |
| 3 | `ng test` no podía correr — `auth.spec.ts` roto | Importaba una clase `Auth` que no existe; el servicio real se llama `AuthService`. Esto rompía la compilación de toda la suite de tests, ningún test llegaba a ejecutarse. | ✅ Resuelto | `775b6b3` |
| 4 | `app.spec.ts` con test de scaffold sin actualizar | Buscaba un `<h1>` con el texto "Hello, frontend-cacc" (el template por defecto que genera Angular), que nunca existió en el componente `App` real (solo tiene `<router-outlet>`). Se reemplazó por un test que valida lo que el componente realmente renderiza. | ✅ Resuelto | `775b6b3` |
| 5 | Checkbox "Recordarme" no hacía nada | No tenía `formControlName` ni ningún handler, era decorativo. Ahora guarda el usuario en `localStorage` en un login exitoso (si está tildado) y lo precarga en la próxima visita. | ✅ Resuelto | `1e3381e` |
| 6 | Link "¿Olvidaste tu contraseña?" era un link muerto | Apuntaba a `href="#"`, no hacía nada al clickear. Ahora muestra un mensaje indicando contactar al administrador (no existe flujo de recuperación de contraseña en el backend todavía). | ✅ Resuelto | `1e3381e` |

---

## Hallazgos de seguridad (backend)

> Alcance: solo se documentan acá. No se modifica la base de datos ni la
> lógica de autenticación sin autorización explícita del equipo — eso es
> responsabilidad del equipo de backend, no de esta rama de QA.

| # | Hallazgo | Severidad | Descripción | Estado |
|---|----------|-----------|-------------|--------|
| S1 | Contraseñas en texto plano | 🔴 Crítica | `DaoLibrary/AuthDao.cs` compara `contrasenia = @contrasenia` directo contra la base, sin hash (bcrypt/PBKDF2/etc.). Cualquiera con acceso a la base ve las contraseñas de todos los usuarios en texto legible. | ⏳ Pendiente — requiere decisión de arquitectura del equipo (algoritmo de hash + migración de contraseñas existentes en la BD). No se toca sin autorización. |
| S2 | Contraseña de admin hardcodeada en el código fuente | 🔴 Crítica | `DaoLibrary/SeedAdmin.cs` tenía `"admin123"` como contraseña del admin, insertada literal en el código y visible en el repo de GitHub. El único lugar que llamaba a esta función ya estaba comentado en `Program.cs` (código muerto). | ✅ Resuelto — se eliminó el archivo completo (sin uso, cero impacto funcional). |
| S3 | Clave de firma JWT hardcodeada en un archivo versionado | 🟠 Alta | `ApiGestion/appsettings.Development.json` tenía la clave real de firma de tokens (`Jwt:Key`) en texto plano, trackeada en git y visible para cualquiera con acceso al repo. Esta sí está activa (Program.cs la usa para firmar todos los tokens de sesión). | ✅ Resuelto — se reemplazó por un placeholder en el archivo versionado, se generó una clave nueva (la anterior quedó expuesta en el historial de git, hay que tratarla como comprometida) y se cargó vía `dotnet user-secrets` (local, fuera de git). Cada dev del equipo necesita cargarla una vez en su máquina — ver instrucciones abajo. |
| S4 | CORS con `AllowAnyOrigin()` | 🟡 Media | `ApiGestion/Program.cs` permite requests desde cualquier origen. Aceptable en desarrollo, pero habría que restringirlo al dominio real antes de un despliegue en producción. | ⏳ Nota para el equipo, no urgente en esta etapa. |
| S5 | URL del backend hardcodeada en el frontend | 🟢 Baja | `AuthService` en Angular apunta a `http://localhost:5118` fijo. Funciona en desarrollo local, pero no va a andar tal cual una vez desplegado en un dominio real — conviene mover a un archivo de environment de Angular cuando se decida el dominio de producción. | ⏳ Nota para el equipo, no urgente en esta etapa. |

### Importante sobre S2 y S3

Eliminar o reemplazar estos valores en el código **limpia el estado actual
del repositorio de acá en adelante**, pero **no borra el historial de git**:
cualquiera que revise commits viejos todavía puede encontrar la contraseña
`admin123` y la clave JWT original. Borrar eso del historial requeriría
reescribir todos los commits (`git filter-repo` o similar) con force-push,
una operación mucho más invasiva que rompe los clones del resto del equipo
— no se hizo acá porque excede el alcance de un fix de QA y necesita
coordinación de todo el equipo antes de ejecutarse.

Por eso, además de esconder la clave JWT, se generó una **nueva**: una clave
que ya estuvo expuesta en el historial debe tratarse como comprometida,
esconderla sola no alcanza.

### Cómo levantar el backend en tu máquina después de este fix

`appsettings.Development.json` ya no tiene la clave real — solo un
placeholder (`"REEMPLAZAR-VIA-DOTNET-USER-SECRETS-VER-README"`). El backend
**arranca igual sin hacer nada más**: al no estar vacío ni ser `null`, ese
texto se toma como si fuera la clave real. La diferencia es que, sin este
paso, quedás firmando y validando tokens con un valor predecible en vez de
uno secreto — funciona, pero no es seguro. Por eso cada persona del equipo
que corra la API localmente debería cargar la clave real una sola vez (se
las pasa Joel por fuera de git):

```
cd ApiGestion
dotnet user-secrets set "Jwt:Key" "<la-clave-que-te-pasen-por-el-grupo>"
```

Esto guarda el valor en tu perfil de Windows, no en el repo — cada
integrante del equipo lo corre una vez y no vuelve a pisarlo un `git pull`.
