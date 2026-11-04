# QA HU-103: barra superior y menú lateral sin títulos duplicados y con el ítem activo claro

**Fecha:** 31/10/2026 · **Rama:** `feat/HU-103-navegacion-orientacion` (desde `main` 342bd86, sin commitear)

**Resultado: PASS en los 5 criterios.**

| Prueba | Resultado |
|---|---|
| Tests unitarios | **542/542** (38 archivos); 34 nuevos en `admin-portal.spec.ts` |
| QA en navegador | **56 PASS · 0 FAIL**, con los dos roles en Chrome real |
| Build | **No ejecutado**, por regla del equipo (ver §8) |

---

## 1. Relevamiento: lo que había y lo que fallaba

La HU describía cuatro problemas. Revisé cada uno contra el código antes de cambiar nada, y no todos existían tal como estaban descriptos.

| Afirmación de la HU | Estado real en `main` | Evidencia |
|---|---|---|
| La barra repite el `h1` | **Parcial.** En escritorio ya era contexto (`Portal administrativo · Cuotas y Pagos`). **En celulares (≤ 520 px) sí duplicaba**: se ocultaban el portal y el `·`, y quedaba `Cuotas y Pagos` encima del `h1` `Cuotas y Pagos`. | `admin-portal.css`, media query de 520 px |
| El activo se diferencia poco | **Sí.** Era un degradé `rgba(0,179,85,.35)` → `.08`. En la parte alta del sidebar (`#007f3e`) era verde sobre verde, y solo se distinguía la línea de 3 px. | `admin-portal.css`, `.nav-item.active` |
| Nombres distintos de los títulos | **Menú, barra y `h1` coincidían en las 10 secciones**, letra por letra. Pero el texto estaba escrito en **4 lugares**: el `<span>` del menú, el `title`, el mapa `pageTitles` y el `h1`. Sí había diferencias en el **asistente DT**, que manda al menú con otras mayúsculas: "Cuotas y pagos", "Becados y descuentos", "Actualización de aranceles" y "Usuarios y permisos". | `grep` sobre `src/app` |
| El menú colapsado debe identificar cada opción | Ya había `title` nativo en cada ítem, y el hover y el foco expanden el menú. | `admin-portal.html` |

**Problemas que no estaban en la HU y aparecieron en el relevamiento:**

- **Rutas hijas sin ítem activo.** En `jugadores/:id/deuda` (se llega desde Cuotas y Pagos y desde Deudas y Morosidad) y en `jugadores/:id` (desde Actividad y Movimientos), `routerLinkActive` no marcaba nada.
- **Título frágil.** Salía de `router.url.split('/')`, así que una URL con `?query` o `#fragmento` dejaba la barra vacía.
- **Foco por teclado invisible en el sidebar.** El contorno global (`--action-accent` = `#007f3e`) era del mismo verde que el fondo, y el `overflow` del sidebar le recortaba el borde izquierdo.
- **Texto cortado en tablets.** A 600 px, "Actualización de Aranceles" se cortaba con `…`, el nombre del portal se partía en dos líneas y el avatar quedaba pegado al borde.

## 2. Matriz de nombres: antes y después

Los nombres de las secciones no cambiaron: ya coincidían. Cambiaron la barra en celulares, las rutas hijas y el asistente.

| Ruta | Menú | Barra antes (escritorio / ≤ 520 px) | Barra después (escritorio / ≤ 520 px) | H1 | Rol |
|---|---|---|---|---|---|
| `resumen-general` | Resumen General | Portal administrativo · Resumen General / **Resumen General** | Portal administrativo · Resumen General / Portal administrativo | Resumen General | SA |
| `cuotas-pagos` | Cuotas y Pagos | … · Cuotas y Pagos / **Cuotas y Pagos** | … · Cuotas y Pagos / Portal administrativo | Cuotas y Pagos | SA, Adm |
| `deudas-morosidad` | Deudas y Morosidad | … · Deudas y Morosidad / **Deudas y Morosidad** | … · Deudas y Morosidad / Portal administrativo | Deudas y Morosidad | SA, Adm |
| `becados-descuentos` | Becados y Descuentos | … · Becados y Descuentos / **Becados y Descuentos** | … · Becados y Descuentos / Portal administrativo | Becados y Descuentos | SA, Adm |
| `recaudacion-mensual` | Recaudación Mensual | … · Recaudación Mensual / **Recaudación Mensual** | … · Recaudación Mensual / Portal administrativo | Recaudación Mensual | SA |
| `actividad-movimientos` | Actividad y Movimientos | … · Actividad y Movimientos / **Actividad y Movimientos** | … · Actividad y Movimientos / Portal administrativo | Actividad y Movimientos | SA |
| `reportes` | Reportes | … · Reportes / **Reportes** | … · Reportes / Portal administrativo | Reportes | SA |
| `actualizacion-aranceles` | Actualización de Aranceles | … · Actualización de Aranceles / **Actualización de Aranceles** | … · Actualización de Aranceles / Portal administrativo | Actualización de Aranceles | SA |
| `usuarios` | Usuarios y Permisos | … · Usuarios y Permisos / **Usuarios y Permisos** | … · Usuarios y Permisos / Portal administrativo | Usuarios y Permisos | SA |
| `auditoria` | Auditoría | … · Auditoría / **Auditoría** | … · Auditoría / Portal administrativo | Auditoría | SA |
| `jugadores/:id` | *(ninguno activo)* → **Actividad y Movimientos** | … · Perfil de Jugador | … · Actividad y Movimientos · Perfil de Jugador | *nombre del jugador* | SA |
| `jugadores/:id/deuda` | *(ninguno activo)* → **Cuotas y Pagos** | … · Deuda Pendiente | … · Cuotas y Pagos · Deuda Pendiente | *nombre del jugador* | SA, Adm |

En **negrita**, la duplicación que había. "…" = `Portal administrativo`. SA = SuperAdmin, Adm = Administrador.

**Asistente DT:** 23 textos pasaron a tomar el nombre de la fuente única. Por ejemplo, `Entrá a "Cuotas y pagos"` pasó a `Entrá a "Cuotas y Pagos"`, e `Ir a Usuarios y permisos` pasó a `Ir a Usuarios y Permisos`.

**No se tocaron dos textos con minúscula, porque no son nombres de navegación:**

- la etiqueta "Cuotas y pagos" de la tarjeta del portal en `/portales`, que describe qué hace el portal;
- el panel "Recaudación mensual" de Resumen General, que es un gráfico.

Lo mismo con las palabras clave en minúscula del asistente (`'resumen general'`, `'auditoria'`): sirven para hacer *match* con lo que escribe el usuario.

## 3. Decisiones

- **Barra superior: contexto, no título.**
  - En las secciones: `Portal administrativo · <sección>`.
  - En las pantallas de detalle se agrega la subpágina, porque ahí el `h1` es el nombre del jugador y no hay duplicación.
  - En tablets (≤ 768 px), la subpágina se oculta: el `h1` y el botón Volver ya dan ese contexto.
  - En celulares (≤ 520 px) queda solo `Portal administrativo`, porque no entra el contexto completo y el nombre de la pantalla es justo el `h1` de abajo.
  - El nombre del portal es el que ya se usaba ("Portal administrativo").
- **Los `h1` se conservan.** Siguen siendo el encabezado semántico de cada pantalla; solo leen su texto de la fuente única.
- **Fuente única de nombres:** `frontend-cacc/src/app/admin-portal/secciones.ts`. La clave es el path de la ruta.
  - La usan el menú (texto y `title`), la barra, el `h1` de las 10 pantallas y el asistente DT.
  - No se pasó el menú a un `@for` con configuración: los íconos SVG están en línea en la plantilla, y moverlos era un refactor sin beneficio para esta HU.
- **Ruta activa:** `esActiva()` compara la sección de la ruta más profunda del `routerState` (`data.seccion` o el `path`), por igualdad exacta de clave.
  - No hay `includes()` sobre la URL, así que no puede haber dos ítems activos.
  - El ítem activo lleva `.active` y **`aria-current="page"`**, que antes no tenía.
- **Rutas hijas:** `data: { seccion, subpagina }` en `jugadores/:id` y `jugadores/:id/deuda`.
  - La sección es la misma a la que vuelve el botón **Volver** de cada pantalla (`volver()` → `actividad-movimientos` y `cuotas-pagos`).
  - No se inventó una jerarquía nueva, y **no cambian paths, guards ni componentes**.
- **Ítem activo:** fondo sólido `var(--color-brand-darkest)` (`#004d25`), barra lateral de 3 px en `var(--color-brand-light)` y texto blanco semibold. Usa tokens existentes.
  - Contraste del texto: **≈ 10:1**. Contraste de la barra contra el fondo del activo: **≈ 3,6:1**, por encima del 3:1 que pide WCAG para componentes.
  - Se descartó un fondo blanco translúcido porque deja el texto en **3,7:1** sobre el verde de arriba, por debajo del AA.
- **Foco por teclado en el sidebar:** contorno blanco de 2 px hacia adentro (`outline-offset: -2px`).
- **Tablet:** el nombre y el correo del usuario se ocultan desde 768 px (antes desde 520 px). Queda el avatar con las iniciales.

## 4. Archivos modificados

| Archivo | Cambio |
|---|---|
| `frontend-cacc/src/app/admin-portal/secciones.ts` | **Nuevo.** Nombres oficiales y nombre del portal |
| `frontend-cacc/src/app/admin-portal/admin-portal.ts` | Sección activa y título desde el `routerState`; se elimina `pageTitles` y el `split('/')`; sale `RouterLinkActive` |
| `frontend-cacc/src/app/admin-portal/admin-portal.html` | Ítems con `esActiva()`, `aria-current`, `title` y texto desde la fuente única; `aria-label` en el `<nav>`; barra con subpágina; separadores `aria-hidden` |
| `frontend-cacc/src/app/admin-portal/admin-portal.css` | Activo con tokens; foco en el sidebar; barra en tablet y celular |
| `frontend-cacc/src/app/app.routes.ts` | Solo `data` en las 2 rutas hijas |
| 10 pantallas (`*.ts` + `*.html`): Resumen General, Cuotas y Pagos, Deudas y Morosidad, Becados y Descuentos, Recaudación Mensual, Actividad y Movimientos, Reportes, Actualización de Aranceles, Usuarios y Permisos, Auditoría | `h1` = `{{ tituloPantalla }}`, que sale de `SECCIONES`. En Auditoría el import va con alias (`SECCIONES_PORTAL`), porque ese archivo ya tenía un `SECCIONES` propio con las entidades auditadas. |
| `frontend-cacc/src/app/shared/dt-admin-chat/bot-knowledge-base.ts` | 23 textos con el nombre de la fuente única |
| `frontend-cacc/src/app/admin-portal/admin-portal.spec.ts` | **Nuevo.** 34 tests |

**Sin cambios:** permisos, guards, paths, APIs, base de datos, autenticación, lógica de los módulos, el hover de apertura y el drawer.

## 5. Tests automatizados (`admin-portal.spec.ts`, 34)

Usan **las rutas reales de `app.routes.ts`**, con sus paths y su `data`. Solo se reemplazan las pantallas por una vacía y se sacan los guards, que ya prueba `auth.guards.spec.ts`. Las aserciones van sobre `aria-current`, `aria-expanded`, `title` y el texto visible, no sobre clases CSS.

| Lo que pide la HU | Tests |
|---|---|
| 1. La barra no repite el `h1` sin contexto | 10 (una por sección) + query/fragmento + pantalla de detalle |
| 2. La ruta correcta da el título correcto | ídem |
| 3. El ítem activo tiene su estado | 10 (un solo `aria-current="page"`, el correcto) |
| 4. Las rutas hijas conservan el módulo activo | `jugadores/:id/deuda`, `jugadores/:id`, cambio de navegación sin dos activos, `data` válida en `app.routes.ts` |
| 5. El menú colapsado da el nombre | `title` = texto = nombre oficial; el texto sigue en el árbol accesible; `aria-label` del `<nav>` |
| 6. SuperAdmin ve sus opciones | las 10 en orden, más "Cambiar portal" |
| 7. El Administrador conserva sus restricciones | solo las 3 de Cobranza; ninguna de SuperAdmin; el menú coincide con los `superAdminGuard` reales |
| 8. El drawer sigue funcionando | hamburguesa y overlay; elegir una opción cierra; Esc cierra |

**Resultado:** `npx ng test --watch=false` → **38 archivos, 542/542** (`qa/HU-103/salida_tests.txt`).

Varios de estos tests fallarían con el código anterior: el de query y fragmento, los de rutas hijas y el de "nunca dos activos".

## 6. QA en navegador (los dos roles)

**Entorno:**

- `ng serve` (:4200) y `ApiGestion` (:5118) sobre la base de prueba de HU-059 (`ClubCamionerosUnificadaV2_HU059_Prueba`), con las cuentas ficticias 20111222 (SuperAdmin) y 30000005 (Administrador).
- Navegador: Chrome instalado, en modo headless, con un perfil temporal, controlado por CDP desde `qa/HU-103/qa_navegador_hu103.mjs`. No necesita dependencias.
- Entra por el login real y recorre el menú **haciendo clic**.
- El mouse y el teclado son eventos reales de Chrome (`Input.dispatchMouseEvent` / `dispatchKeyEvent`).

**Resultado: 56 PASS · 0 FAIL** (`qa/HU-103/salida_qa_navegador.txt`, `resultado_qa_navegador.json`, capturas en `qa/HU-103/capturas/`).

| Bloque | SuperAdmin | Administrador |
|---|---|---|
| Opciones visibles y su orden | 10 ✓ | 3 (Cobranza) ✓ |
| `title` = texto de cada ítem | ✓ | ✓ |
| Pie del menú | Cambiar portal, Cerrar sesión ✓ | Cerrar sesión ✓ |
| Por sección: barra, `h1`, un solo activo, `title` del activo | 10/10 ✓ | 3/3 ✓ |
| Hover real: 72 px (texto oculto) → mouse adentro: 252 px (texto visible) → mouse afuera: 72 px | ✓ | ✓ |
| Tab: el foco entra al sidebar, lo expande y el contorno es blanco hacia adentro | ✓ | ✓ |
| Rutas hijas: activo, barra y `h1` = jugador | `jugadores/1` y `jugadores/1/deuda` ✓ | `jugadores/1/deuda` ✓ |
| URL con `?query` y `#fragmento` | ✓ | ✓ |
| Responsive: 1920, 1366×768, 1024, 768, 600 y 375. Sin scroll horizontal, sin texto cortado, la barra no se sale de la ventana y el portal ocupa una línea | 12/12 ✓ | 6/6 ✓ |
| Drawer a 375 px: hamburguesa (`aria-expanded`, overlay) → elegir una opción navega y cierra → el overlay cierra → Esc cierra → el hover no lo abre; el menú scrollea | ✓ | ✓ |
| Rutas de SuperAdmin por URL (8): el guard rechaza con el aviso de siempre y devuelve a Cuotas y Pagos | — | 8/8 ✓ |

La primera corrida dio 55/1. El FAIL era el texto cortado a 600 px (§1), que se corrigió (§3). La segunda corrida dio 56/0 y sumó el chequeo de que nada de la barra se salga de la ventana.

## 7. Criterios de aceptación

| Criterio | Estado | Evidencia |
|---|---|---|
| CA1. Barra superior sin duplicación | **PASS** | §2: contexto en escritorio y tablet, solo el portal en celular; tests de barra; QA RESP |
| CA2. Ítem activo claro | **PASS** | Fondo sólido + barra lateral con tokens, ≈ 10:1 y ≈ 3,6:1; `aria-current`; capturas `*_colapsado.png` y `*_hover_expandido.png`; también en rutas hijas |
| CA3. Nombres consistentes, con mayúsculas | **PASS** | Fuente única; menú = `title` = barra = `h1` en las 10 secciones (QA SEC); asistente DT corregido |
| CA4. Menú colapsado identificable | **PASS** | `title` nativo con el mismo nombre; el hover y el foco expanden el menú y muestran el texto; el nombre sigue en el árbol accesible |
| CA5. Comportamiento existente | **PASS** | Hover de escritorio (QA HOVER) y drawer (tests y QA DRAWER), sin cambios |

## 8. Pendientes y observaciones

- **Build no ejecutado.** La regla del equipo es no correr builds después de los cambios; queda para quien integre (`npx ng build`).
  - Los tests compilan todas las plantillas (incluidas las 10 pantallas y el layout) sin errores.
  - Los diagnósticos del IDE están limpios.
- **El backend del QA es `ApiGestion.dll` del 29/10**, compilado desde otra rama. Para esta HU solo importan el login, los guards y los datos que llenan cada pantalla, y todo respondió. Ningún cambio de HU-103 toca el backend.
- **OBS-103-1 (fuera de alcance):** desde Deudas y Morosidad, el botón "Volver" de la deuda del jugador lleva a Cuotas y Pagos (`deuda-jugador.ts`, `volver()`). HU-103 lo respeta: deja activa Cuotas y Pagos, que es adonde vuelve. Si se quisiera volver al módulo de origen, el cambio va en esa pantalla, no en la navegación.
- **OBS-103-2:** en tablets (521 a 768 px) ya no se ven el nombre y el correo del usuario; quedan las iniciales en el avatar. Fue necesario para no cortar el contexto (§1).
