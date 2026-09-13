# QA — Bugs y hallazgos de seguridad

Registro de bugs de UI y hallazgos de seguridad detectados durante el testeo
del Sistema Administrativo CACC, junto con su estado y la corrección aplicada
en cada caso. Mantenido en la rama `fix/ui-bugs-general` por Joel Galera (QA), que
corrió y validó las pruebas de cada fix.

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
| 7 | "Registrar pago" y "Asignar beneficio" no llaman a ningún backend | Detectado en la regresión de HU-014 (2026-09-12): al registrar un pago, el mensaje "Pago registrado correctamente" aparece pero nada cambia — la grilla de "Pendientes de cobro", "Últimos pagos" y los contadores del header quedan iguales. Motivo: `cuotas-pagos.ts` y `becados-descuentos.ts` tienen sus propios comentarios (`// No backend yet: the submit only simulates a...`) — `onSubmit()` solo resetea el formulario y muestra el cartel de éxito; `players`, `pendingRows`, `paymentRows` y los `headerMetrics` son arrays hardcodeados en el componente, no vienen de la API. No es un problema de refresco de UI, es que la escritura nunca se conectó al backend. | ⏳ Parcial — la **lectura** ya quedó conectada (ver bug 8): las pantallas muestran datos reales de `ClubCamionerosPRUEBA`. Sigue pendiente la **escritura**: `onSubmit()` de ambos formularios continúa simulando el alta, faltan los endpoints POST de pago y de asignación de beneficio. | |
| 8 | La grilla mezclaba bonificaciones reales con nombres de jugadores falsos | Detectado al revalidar HU-014 (2026-09-12): el jugador con id 1 figura en pantalla como "Sánchez, Bautista D.", pero en `ClubCamionerosPRUEBA` el id 1 es "ARRASCAETA, JULIAN". El componente cruzaba las bonificaciones que sí venían de la API con el array de jugadores hardcodeado, por lo que mostraba datos reales pegados a nombres inventados. | ✅ Resuelto — se agregaron `GET /api/players`, `GET /api/payments/latest`, `GET /api/payments/pending`, `GET /api/payments/metrics` y `GET /api/players/discounts/all`, y se eliminaron los arrays hardcodeados de `cuotas-pagos.ts` y `becados-descuentos.ts`. | |
| 9 | No existe el dato de cuota adeudada | Los 1258 registros de `PAGOS` tienen `estado = 1` (pagado) y `fecha_vencimiento` en NULL; `VISTA_ESTADO_DEUDA` está vacía. No hay forma de calcular "Pendientes de cobro" con los datos actuales: depende de HU-009 (generación de cuotas mensuales) y HU-010 (mantener vigente el pago adeudado), ambas en Por hacer. | ⏳ Pendiente de HU-009 / HU-010. El panel quedó conectado al endpoint real y muestra "No hay cuotas pendientes registradas" en lugar de filas de ejemplo. Cuando esas historias se implementen, se llena solo. | |
| 10 | Pago con fecha del año 2926 | `PAGOS.PK_id_pago = 374` tiene `fecha_pago = 2926-08-12`, evidentemente un error de tipeo de 2026. Distorsiona cualquier orden por fecha y aparece primero en "Últimos pagos". | ⏳ Pendiente — no se modifica la base sin autorización del equipo. El contador "Recaudado del año" no se ve afectado porque filtra por año corriente. | |
| 11 | Ninguna bonificación está aplicada a un pago | Los 1258 registros de `PAGOS` tienen `FK_id_jugador_descuento` en NULL, aunque existan 6 bonificaciones asignadas y la columna `monto_final` esté pensada para reflejar el descuento. Hoy `monto_base` y `monto_final` son siempre iguales. | ⏳ Nota para el equipo: la bonificación se muestra como etiqueta pero todavía no impacta en el importe cobrado. | |
| 12 | No existe clasificación Beca / Descuento en la base | La pantalla de Becados y Descuentos distinguía con dos colores entre "Beca" y "Descuento", pero `TIPO_DESCUENTO` no tiene ninguna columna que haga esa clasificación: solo guarda el nombre libre del tipo (`Becado 100%`, `Media Beca`, `Desc. Hermanos`). | ⏳ Pendiente de definición — mientras tanto la tabla muestra el nombre real del tipo en una etiqueta neutra, en vez de inventar la categoría en el frontend. | |

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
