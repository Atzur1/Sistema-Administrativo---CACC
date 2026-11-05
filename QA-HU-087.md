# QA HU-087: política de contraseñas nuevas del portal

**Fecha:** 04/11/2026 · **Rama:** `FramD` (sin commitear)

**Resultado: PASS en lo automatizado · QA manual contra la API PENDIENTE (BLOCKED: entorno no levantado).**

| Prueba | Resultado |
|---|---|
| Tests backend (`dotnet test ApiGestion.Tests`) | **794 pasan · 1 falla · 167 omitidos** (usan base de datos). La falla es preexistente y ajena a la HU (ver §10). 33 casos nuevos o reescritos |
| Tests frontend (`ng test`) | **633/633** en 43 archivos. 5 tests nuevos o reescritos en `account-access.spec.ts` (**se verificó que fallan con el componente anterior**) |
| Build frontend (`ng build`) | **Correcto.** El aviso de presupuesto (1,23 MB) ya existía |
| QA manual (navegador y Postman) | **Pendiente**, ver §9 |

---

## 1. Relevamiento: lo que había

`PoliticaContrasenia` **ya existía** en `ServiceLibrary` y ya se aplicaba en `POST api/auth/account-access/complete`, el único endpoint que completa los dos enlaces: ACTIVACION y RECUPERACION, según el tipo del token. Revisé cada punto de la HU contra el código:

| Punto | Estado encontrado | Acción |
|---|---|---|
| Regla vieja de 8 caracteres | No quedaba ninguna. El mínimo ya era 10 en la política, en el DTO y en el front | Ninguna |
| Política en el backend | Sí: largo, letras y números, datos personales, comunes y club | Mensajes separados (§2) y nombre del club centralizado |
| Normalización | Sí, pero `internal` | Se hizo pública: es la función central |
| Un rechazo no consume el enlace | Sí: se lee el titular (sin consumir), se valida y recién después se consume | Ahora tiene tests (§6) |
| Login no aplica la política | Sí | Ahora tiene un test de compatibilidad (§7) |
| Hash | PasswordHasher (BCrypt), sin cambios | Ninguna |
| **400 con `problemas` para cualquier contraseña inválida** | **NO.** El DTO tenía `[MinLength(10)]`, que corre antes del controller: una contraseña corta enviada directo a la API volvía 400 **sin `problemas`** (antes con `ValidationProblemDetails`, después de HU-090 con `{ exito, mensaje }`). Y el front toma un 400 sin `problemas` como "enlace vencido" | **Bug corregido.** Se verificó que el test falla con el DTO anterior |
| Problemas como lista | No: se mostraban pegados en una línea ("Elegí otra contraseña: X Y") | Lista |
| Requisitos en vivo | Sí ("Al menos 10 caracteres", "Letras y números"), pero el estado cumplido o pendiente **solo se veía**: un lector de pantalla no lo leía | "Contiene letras" y "Contiene números" por separado, con estado en texto |
| Tests de flujo (activación y recuperación) | No había: el flujo vivía en el controller, atado a `AccountAccessDao` (sealed, contra la base) | Se pasó a `AccesoCuentaService` con la interfaz `ITokensAccesoCuenta` |

## 2. Política (`ServiceLibrary/PoliticaContrasenia`)

`Validar(contrasenia, DatosTitular)` devuelve la lista de problemas (vacía si la contraseña sirve), **todos a la vez** y sin lanzar excepciones:

| Regla | Mensaje |
|---|---|
| Menos de 10 caracteres | Tiene que tener al menos 10 caracteres. |
| Más de 128 (tope que **ya existía** por el costo del hash, no lo agrega esta HU) | No puede superar los 128 caracteres. |
| Sin letras | Tiene que contener al menos una letra. |
| Sin números | Tiene que contener al menos un número. |
| Contiene el DNI | No puede contener tu DNI. |
| Contiene el nombre | No puede contener tu nombre. |
| Contiene el apellido | No puede contener tu apellido. |
| Contiene el usuario del correo | No puede contener el usuario de tu correo. |
| Contiene el nombre del club | No puede contener el nombre del club. |
| Es común | Es una contraseña demasiado común. Elegí otra. |

No se exigen mayúsculas ni símbolos: no lo pide la HU ni ninguna otra política vigente. En pantalla figuran como "recomendado".

**Normalización** (`PoliticaContrasenia.Normalizar`, la única): pasa a minúsculas, quita los acentos y deja solo letras y dígitos, así que también caen los puntos, guiones y espacios. Ejemplos: `José` → `jose`, `PÉREZ` → `perez`, `juan.perez` → `juanperez`, `40.123.456` → `40123456`. Se aplica a la contraseña y a cada dato **solo para comparar**. La contraseña se hashea tal cual se escribió y el DNI guardado no se toca.

**Datos personales:**
- **DNI:** normalizado, así que se comparan solo los dígitos.
- **Usuario del correo:** solo lo anterior a la `@`. El dominio no se controla.
- **Nombre y apellido:** se compara cada palabra ("Camila Andrea" → `camila`, `andrea`) y cada tramo de un apellido compuesto con guion ("Pérez-Gómez" → `perez`, `gomez`).
- **Nombres cortos (decisión):** las partes de menos de 3 letras no se controlan ("Li", "De"). Si no, "Olivares" quedaría rechazada para alguien de apellido "Li".

**Contraseñas comunes:** la lista local está en la misma clase. Son alrededor de 60 contraseñas exactas y 40 palabras base que se comparan quitando los números del final, así "Qwerty12345" y "Argentina2027" caen igual que "qwerty1234". Es determinista, sin llamadas externas y no se expone al cliente.

**Nombre del club (decisión):** se toman `EntityLibrary.Club.Nombre` ("Camioneros") y `Club.Sigla` ("CACC"), que son nuevos y centralizados. El nombre oficial ("Club Atlético Camioneros de Córdoba") ya contiene "camioneros". La HU dice "no ser el nombre del club"; se aplica **"no contenerlo"**, que es más estricto, porque "Camioneros2026" es tan adivinable como "Camioneros". Cae con cualquier separador ("C.A.C.C.2026").

## 3. Endpoint y contrato

`POST api/auth/account-access/complete` (sin cambios de ruta, rate limit ni status):

| Caso | HTTP | Cuerpo |
|---|---|---|
| Contraseña que no cumple | **400** | `{ "exito": false, "mensaje": "La contraseña no cumple los requisitos: …", "problemas": ["…", "…"] }` |
| Enlace vencido, usado o inexistente | **400** | `{ "exito": false, "mensaje": "El enlace no es válido o venció." }` (sin `problemas`, como antes) |
| Éxito | **200** | `{ "mensaje": "La contraseña se actualizó. Ya puede iniciar sesión." }` |
| Demasiados intentos | 429 | Sin cambios |

Así `problemas` (HU-087) convive con `mensaje` (HU-090): `ResultadoOperacion` tiene ahora una lista opcional `Problemas`, y `ResultadoHttp` la agrega solo cuando trae algo.

## 4. Flujo (`ServiceLibrary/AccesoCuentaService`, el mismo para activación y recuperación)

1. Se lee el titular del enlace (`ITokensAccesoCuenta.ObtenerTitularDelToken`), que **no consume** el enlace. Si no sirve, se responde "no es válido o venció" **sin evaluar la contraseña**, para no confundir un enlace vencido con una contraseña inválida.
2. Se aplica `PoliticaContrasenia.Validar` con nombre, apellido, DNI y correo del titular.
3. Si hay problemas, responde 400 con `problemas` y **el enlace sigue sirviendo** mientras no venza.
4. Si cumple, se guarda el hash (`PasswordHasher`, sin cambios) y se consume el enlace en una transacción, que vuelve a controlar que siga vigente.

## 5. Frontend (`account-access`: /crear-contrasena y /restablecer-contrasena)

- **Requisitos en vivo:** "Al menos 10 caracteres", "Contiene letras", "Contiene números" (obligatorios) y "Mayúsculas y minúsculas" y "Un símbolo" (recomendados).
- **El estado no depende del color:** el ícono cambia de círculo vacío a tilde y hay un texto "Cumplido:" o "Pendiente:" para lectores de pantalla.
- **Rechazo de la API:** aparece "La contraseña no cumple los requisitos:" con un motivo por línea (`role="alert"`). El formulario sigue abierto y el reintento usa el **mismo token**.
- **El front solo ayuda, no protege:** sin largo o sin letras y números el botón queda deshabilitado, pero la API vuelve a validar todo (§6, "API directa").
- La nota bajo los requisitos aclara que tampoco puede contener DNI, nombre, apellido, usuario del correo, el club, ni ser común. Eso lo controla solo el servidor, porque el front no conoce esos datos.

## 6. Tests de flujo (backend)

| HU | Caso | Test |
|---|---|---|
| Activación 1-4 / Recuperación 1-4 | Contraseña inválida → `Invalido`, con `problemas`, el enlace **no** se consume y se sigue pudiendo leer | `AccesoCuentaTests.UnaContraseniaInvalida_DevuelveLosProblemasYNoConsumeElEnlace` (ACTIVACION y RECUPERACION) |
| Activación 5-6 / Recuperación 5-7 | Mismo enlace: rechazo → éxito → se guarda el hash (no el texto) → enlace consumido → un tercer intento da "no es válido" | `…ConElMismoEnlace_SeCorrigeYSeCompleta_YDespuesElEnlaceYaNoSirve` |
| Token vencido | Responde enlace inválido sin `problemas`, aunque la contraseña también falle | `…UnEnlaceVencido_…` |
| 400 + `problemas` | El endpoint con una contraseña corta, con el DNI cargado con puntos, común y con el club | `…AlEndpointDirecto_UnaContraseniaQueNoCumple_Responde400ConProblemas` |
| Mismo enlace en el endpoint | 400 y después 200 con el mismo token | `…ElEndpoint_TrasUn400_AceptaElMismoEnlace…` |
| DTO | El DTO deja pasar `abc12345` y `""` para que los juzgue la política (**falla con el DTO anterior**) | `…ElDto_DejaPasarLaContrasenia…` |

## 7. Compatibilidad: contraseña histórica

`AccesoCuentaTests.UnaContraseniaHistoricaQueNoCumpleLaPolitica_SigueSirviendoParaEntrar`. Se prepara una cuenta con el hash real (BCrypt) de `clave123`, que tiene 8 caracteres y que la política rechaza (el test lo comprueba). `POST api/auth/login` con esa contraseña responde **200 con token**, y con una contraseña equivocada sigue respondiendo **401**, no un 400 de política. Además, `LoginDTO` no tiene ningún largo mínimo de la política. **No se migró ni se tocó ninguna contraseña existente.**

## 8. Logs y auditoría

- Un rechazo registra solo "Contraseña nueva rechazada por la política (N reglas sin cumplir)", sin la contraseña, el enlace ni el hash.
- La auditoría no cambia: solo la escritura efectiva (`CompleteAccountAccess`) pasa por el actor de auditoría, como antes.
- **Observación:** `EmailLinkSender` registra el enlace completo **solo** en `Development` con `Email:LogResetLink = true` (atajo de prueba local, marcado `SECURITY TODO`). No lo cambia esta HU. Hay que confirmar que ese flag no esté activo fuera de desarrollo.

## 9. QA manual (pendiente de ejecutar)

| # | Escenario | Esperado | Resultado |
|---|---|---|---|
| 1 | Abrir un enlace de **activación** y escribir `abc` | Los requisitos se van tildando en vivo; el botón queda deshabilitado | ☐ |
| 2 | Activación con `Perez-12345` (titular Pérez) | 400. La lista muestra "No puede contener tu apellido." y el formulario sigue | ☐ |
| 3 | Mismo enlace con `Tribuna-Norte-77` | Éxito y redirige al login; ingresa con la nueva | ☐ |
| 4 | Volver a abrir el mismo enlace | "Enlace inválido" (ya consumido) | ☐ |
| 5 | **Recuperación**: "¿Olvidaste tu contraseña?", abrir el enlace y probar `password123` | 400 con "demasiado común"; reintentar con una válida funciona | ☐ |
| 6 | Postman directo al endpoint con un token válido y `Corta-7` | 400 con `problemas` (largo) | ☐ |
| 7 | Postman con `Clave-<DNI>` | 400 con "No puede contener tu DNI." | ☐ |
| 8 | Postman con `Camioneros-1931` | 400 con "nombre del club" | ☐ |
| 9 | Postman con un token vencido | 400 "El enlace no es válido o venció." **sin** `problemas` | ☐ |
| 10 | Login de una cuenta existente con contraseña de 8 caracteres | Entra normalmente | ☐ |
| 11 | Revisar el log de la API después de 2 y 6 | No aparece la contraseña ni el token | ☐ |
| 12 | Lector de pantalla en la lista de requisitos | Lee "Pendiente: Contiene números" y después "Cumplido: …" | ☐ |

## 10. Pendientes y observaciones

- **QA manual (§9): BLOCKED.** Necesita la API, SQL Server, SMTP o `Email:LogResetLink` en desarrollo, y cuentas de prueba.
- **Falla preexistente** `AssignDiscount_StartingWithAnOldFee_IsRefused(-1)` (`ConnectionString`). Es la misma de HU-096 y HU-090.
- **Nombre del club duplicado:** los reportes repiten "Club Atlético Camioneros de Córdoba" en 6 lugares y los correos usan "Club Atlético Social y Deportivo Camioneros". Conviene unificar en `EntityLibrary.Club` (fuera de alcance).
- **Cambios de diseño para poder testear:** `AuthDao.ValidarLogin` pasó a `virtual` (el mismo patrón que `DiscountDao`), y `AccountAccessDao` implementa la interfaz nueva `ITokensAccesoCuenta`.
