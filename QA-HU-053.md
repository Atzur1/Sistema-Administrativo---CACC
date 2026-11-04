# QA — HU-053: Unificación de las bases de datos A y B

## Actualización 29/10/2026, tarde: cierre técnico

**Estado: BLOCKED (sin cambios).** La unificación **no se re-ejecutó**: no cambió nada en `database/unificacion/` ni en el esquema, y el bloqueo es de negocio, no técnico. Las 91 verificaciones de abajo siguen siendo la última evidencia.

- La suite del proyecto `Unificacion.Integration.Tests.cs` sigue en verde dentro de la regresión completa: **720 / 720**.
- **Dato nuevo para la decisión de identidades.**
  - La base B (`CACC_B_Origen`, consulta de solo lectura) tiene **2 jugadores con DNI de 9 dígitos** (prefijos `321…` y `312…`) y una persona no jugadora con `123…`. Parecen datos de prueba cargados antes de que el Deportivo validara 7 u 8 dígitos. La base A tiene los 578 DNI con 8 dígitos.
  - La base unificada los acepta (`CK_PERSONAS_dni_digitos`: 6 a 9), pero **el alta de jugadores (portal, integración y Deportivo) exige 7 u 8**.
  - El club tiene que decidir si se corrigen, se excluyen o se importan igual. Se suma a los 4 conflictos de DNI.

**Pendiente:** la decisión del club sobre los 4 conflictos de DNI y los 3 DNI de 9 dígitos de B; la validación conjunta con el equipo Deportivo; y el alcance de datos para producción (contradice "base vacía").

---

Rama `FramD` (`cc5e0d8` + cambios sin commitear). Ejecutado junto con Claude Code el 2026-10-28, sobre SQL Server LocalDB,
con bases de prueba creadas desde cero. **No se escribió en ninguna base de referencia ni de producción**: las de origen
solo se leyeron, y una huella por tabla (filas + checksum) tomada antes y después lo demuestra.

**Resultado: HU-053 queda BLOCKED.** La unificación es reproducible y pasa las 91 verificaciones, pero:

1. los 4 conflictos de DNI se destrabaron con decisiones **de prueba**: falta la decisión real del club;
2. falta la validación conjunta con el otro equipo (su parche de ids todavía no está en su repo) y el code review;
3. el alcance de datos para producción contradice una decisión previa (`cacc-unificacion/docs/decisiones.md`: "base vacía").

Detalle en las secciones 14 y 15.

---

## 1. Objetivo

Que el Portal Administrativo (A) y el Portal Deportivo (B) trabajen sobre una sola base con las mismas personas, usuarios y
jugadores: sin duplicar identidades, sin perder registros de ninguno de los dos y sin que dos altas simultáneas choquen.

## 2. Entorno

| | |
|---|---|
| Motor de las pruebas | SQL Server 2025 (17.0.4025.3) Express, LocalDB `(localdb)\MSSQLLocalDB` |
| Base A (origen, solo lectura) | `ClubCamionerosPRUEBA` — Portal Administrativo, nombres `PERSONA`/`USUARIO`, compatibilidad 170 |
| Base B (origen, solo lectura) | `CACC_B_Origen` — Portal Deportivo, nombres `PERSONAS`/`USUARIOS`, compatibilidad 160 |
| Esquema unificado | `database/esquema/ClubCamioneros_unificada_crear_base.sql` (= `cacc-unificacion/base-de-datos/…v2…`, idéntico salvo CRLF) + migraciones `V20261018_01`, `V20261018_02`, `V20261021_01` |
| Base unificada de prueba | `ClubCamionerosUnificadaV2_HU053` (queda creada para la validación conjunta) |
| Código B revisado | `NejoCHZ/App-Club-Atletico-Camioneros` `afa4e60` + `ellos-definitivo.patch` + `ellos-nombres-backend.patch` (exportado con `git archive`; el clon del otro equipo no se tocó) |
| Log de la corrida final | `%TEMP%\cacc-hu053\hu053_20261028_191248.log` |

Cómo repetirlo: [`database/unificacion/README.md`](database/unificacion/README.md).

## 3. Acuerdos

### Motor
- El código ya exige **SQL Server 2017 o superior**: `V20260930_01` (migración aplicada en todas las bases) usa
  `STRING_AGG`. La documentación decía 2016; se corrigió en `README.md` y `database/produccion/README.md`.
  Los scripts de unificación también usan `STRING_AGG`, `CONCAT_WS` y `sys.sequences.last_used_value` (2017+).
- **Pendiente**: confirmar con el otro equipo la versión del servidor de producción (B documenta `SQLEXPRESS`, sin versión).

### Esquema objetivo
El contrato v2 ya acordado: nombres del Portal Deportivo (`PERSONAS`, `USUARIOS`, `TIPOS_DESCUENTO`,
`JUGADORES_RESPONSABLES`, `STAFF_CATEGORIAS`). El código del Administrativo de esta rama ya usa esos nombres.

### Identificadores (decidido en esta HU)
| Tabla | Mecanismo | Motivo |
|---|---|---|
| `JUGADORES`, `PAGOS` | `SEQUENCE` (`SEQ_JUGADORES`, `SEQ_PAGOS`, `NO CACHE`) como DEFAULT de la PK | ya acordado (V20261011_04/05) |
| Resto (incluidas las 6 tablas con MAX+1 de B) | `IDENTITY` | es el contrato v2 y el esquema original de B; igual de seguro ante concurrencia; convertir a SEQUENCE obligaba a reconstruir 6 tablas y romper el `SCOPE_IDENTITY()` que ya usa el código |

No conviven dos mecanismos para la misma entidad.

### Roles
| | A | B | Unificado |
|---|---|---|---|
| `ROLES` | 1 Administrador General (`ALL`), 2 Usuario Común (`READ`) | 1 Administrador (Tesorero) … 7 Coordinador | los 7 de B, **ids fijos** (están escritos en el código de B) |

- Ningún id de rol cambia. Los de B se verifican por id **y** nombre antes de importar.
- Traducción de A (tabla cerrada en `03_importar.sql`): 1 → 1 (mismo alcance total; es lo que usa la cuenta SuperAdmin de
  prueba del contrato), 2 → **sin rol deportivo** (no hay equivalente; el 2 unificado es "Administrativo"). Queda anotado.
- La autorización del Portal Administrativo **no** sale de `FK_id_rol`: sale de `acceso_portal` + `rol_portal`
  (1 SuperAdmin, 2 Administrador). Los usuarios de B entran con `acceso_portal = 0`; el SuperAdmin los habilita a mano.

### Persona, Usuario y Jugador
- `PERSONAS` es la identidad compartida: **una persona por DNI**.
- `PERSONAS 1 — 0..1 USUARIOS` (índice único `UX_USUARIOS_persona`). Nombre, apellido y DNI del usuario se leen de
  `PERSONAS`; no se materializan (ver sección 8).
- `PERSONAS 1 — 0..1 JUGADORES` en la base importada (verificado: 0 personas con más de un jugador).

### Decisiones de datos tomadas en esta HU (con el responsable del equipo)
| Tema | Decisión |
|---|---|
| Alcance | HU-053 manda: se importan A y B a una base de **prueba**. Qué datos van a producción se decide aparte (ver 15). |
| DNI repetido | Se une solo si los registros son idénticos y a lo sumo uno tiene dependientes; si no, se frena hasta una decisión humana. Nada se borra. |
| Mismo DNI en A y B con datos distintos | Gana A; B completa lo vacío. Cada valor descartado queda anotado. |
| Jugadores sin género (577 de A, 5 de B) | Se infiere por categoría: "Femenino" → Femenino; el resto → Masculino. Solo donde falta, y anotado uno por uno. |

## 4. Inventario

### A — `ClubCamionerosPRUEBA`
| Tabla | Filas | PK | Notas |
|---|---|---|---|
| PERSONA | 578 | IDENTITY | `Dni`, `QR_TOKEN`, `FOTO` (vacíos), sin `domicilio` |
| USUARIO | 2 | int (sin IDENTITY) | `password_hash` (PBKDF2), `contrasenia` en texto plano en 1 cuenta, `nombre/apellido/dni` materializados (vacíos), `acceso_portal`, `rol_portal` |
| ROLES | 2 | int | |
| JUGADORES | 577 | int | `FK_id_jugador_responsable`, `FK_id_Ficha_Medica` (vacíos), `fecha_alta` (vacía en los 577) |
| RESPONSABLES | 487 | int | los 487 sin persona y sin jugador vinculado |
| Jugadore_Responsable | 0 | int | |
| STAFF / STAFF_CATEGORIA | 0 / 0 | int | `STAFF.FK_id_staff_categoria` (relación circular) |
| CATEGORIAS | 13 | int | |
| FICHAS_MEDICAS / ASISTENCIAS | 0 / 0 | int | `ASISTENCIAS.FK_id_estado_deuda` |
| TIPO_DESCUENTO | 2 | IDENTITY | Becado = 1, **Descuento = 3** |
| JUGADORES_DESCUENTOS | 9 | IDENTITY | |
| PAGOS | 1258 | int | todas cuotas pagadas (`estado = 1`) |
| AUDITORIA_CAMBIOS | 2 | IDENTITY | |
| TOKEN_ACCESO_CUENTA, ARANCELES, ARANCELES_INSCRIPCION | 0 | IDENTITY | |
| VISTA_ESTADO_DEUDA | 0 | — | es una **tabla**, no una vista |
| `__CaccMigraciones` | 4 | | |

11 triggers (`TR_AUDIT_*` y `TR_AUDITORIA_IDENTIDAD_ACTOR`). Sin secuencias, vistas ni procedimientos. FK completas.

### B — `CACC_B_Origen`
| Tabla | Filas | Notas |
|---|---|---|
| PERSONAS | 29 | 8 DNI repetidos (ids 1–8 sin dependientes, 11–18 con jugador) |
| USUARIOS | 3 | `contrasenia` BCrypt; `email` = nombre de usuario (`mzarate`…), no es un correo |
| ROLES | 7 | |
| JUGADORES | 15 | `peso`, `altura`, `pie_habil`, `grupo_sanguineo` |
| RESPONSABLES / JUGADORES_RESPONSABLES | 2 / 2 | `parentesco` |
| STAFF / STAFF_CATEGORIAS | 3 / 0 | |
| CATEGORIAS | 13 | mismos ids y nombres que A |
| FICHAS_MEDICAS | 6 | |
| HISTORIAL_PARTIDOS / JUGADORES_PARTIDOS | 10 / 8 | solo existen en B |
| TIPOS_DESCUENTO, JUGADORES_DESCUENTOS, PAGOS, ASISTENCIAS | 0 | `JUGADORES_DESCUENTOS` sin fechas de vigencia |

Todo IDENTITY. Sin triggers. Vista `VISTA_ESTADO_DEUDA`.

### Unificado — `ClubCamioneros_unificada_crear_base.sql`
29 tablas, `SEQ_PAGOS` y `SEQ_JUGADORES`, vista puente `VISTA_ESTADO_DEUDA`, 14 triggers (11 de auditoría + identidad del
operador, género obligatorio ×2, periodos de jugadores), DNI único de 6 a 9 dígitos, una cuenta por persona, correo único y
con formato, `JUGADORES.FK_id_categoria` obligatoria, `JUGADORES.activo` calculado, `JUGADORES_PERIODOS`. Catálogos:
ROLES (7), CATEGORIAS (13), TIPOS_DESCUENTO (Becado = 1, Descuento = 2).

## 5. Matriz de compatibilidad

| Entidad | Base A | Base B | Esquema unificado | Acción |
|---|---|---|---|---|
| Persona | `PERSONA`, IDENTITY, `Dni`, `QR_TOKEN`/`FOTO` | `PERSONAS`, IDENTITY, `dni`, `domicilio` | `PERSONAS`, DNI único válido | **migración requerida** + **dato a transformar** (remapeo por DNI, fusión, género) |
| Usuario | `USUARIO`, PK manual, `password_hash`, identidad materializada, acceso/rol de portal | `USUARIOS`, IDENTITY, `contrasenia`, `email` = usuario | `USUARIOS`, `contrasenia`, sin identidad materializada, una por persona | **migración** + **transformar** (`password_hash`→`contrasenia`, correo inválido, texto plano) |
| Roles | 2 roles | 7 roles | los 7 de B | **transformar** (A 2 → sin rol) |
| Jugadores | PK manual, sin género en la persona, sin `fecha_alta` | IDENTITY, datos físicos | `SEQ_JUGADORES`, género y categoría obligatorios | **migración** + **transformar** (género inferido, fusión A∩B) |
| Categorías | 13 | 13 (mismos) | 13 + `asociacion` | **compatible** (verificado id + nombre) |
| Responsables | PK manual, sin persona | IDENTITY | IDENTITY | **migración** (A conserva ids; B remapeo) |
| Jugador/Responsable | `Jugadore_Responsable` | `JUGADORES_RESPONSABLES` + `parentesco` | `JUGADORES_RESPONSABLES` | **migración** (cambio de nombre) |
| Fichas médicas | sin filas | 6 | IDENTITY | **migración** (remapeo de jugador) |
| Staff | con FK circular a `STAFF_CATEGORIA` | IDENTITY | IDENTITY, sin FK circular | **migración** (la columna circular está vacía) |
| Staff/Categoría | `STAFF_CATEGORIA` | `STAFF_CATEGORIAS` | `STAFF_CATEGORIAS` | **migración** (cambio de nombre) |
| Asistencias | con `FK_id_estado_deuda` | sin persona | + `FK_id_persona`, `observaciones` | **migración** (persona derivada del jugador; 0 filas) |
| Pagos | 1258, `concepto`, registro | 0, sin `concepto` | `SEQ_PAGOS`, índices únicos de pendientes | **migración** (A conserva ids; B → `Cuota`) |
| Beneficios | `TIPO_DESCUENTO` (Descuento = 3), 9 becas con vigencia | `TIPOS_DESCUENTO`, becas sin vigencia | `TIPOS_DESCUENTO` (Descuento = 2) | **transformar** (3 → 2 por nombre); becas de B sin vigencia: **decisión pendiente** si apareciera alguna (hoy 0; el script se frena) |
| Tarifas | `ARANCELES`, `ARANCELES_INSCRIPCION` (0) | — | por género o categoría; inscripción por género | **compatible** (sin datos; el script se frena si aparecen) |
| Auditoría | `AUDITORIA_CAMBIOS` (2) + 11 triggers | — | 14 triggers | **migración** (nombre de tabla unificado en `entidad`) |
| Partidos | — | `HISTORIAL_PARTIDOS`, `JUGADORES_PARTIDOS` | iguales | **migración** |
| Estado de deuda | tabla vacía | vista | vista puente | **cambio de código** (B consulta la API; ya en `ellos-definitivo.patch`) |
| Nuevas sin origen | — | — | `JUGADORES_PERIODOS`, `JUGADORES_CATEGORIAS`, `CREDENCIALES_QR`, `PAGOS_ANULADOS`, `INTENTOS_LOGIN`, `CONFIGURACION_CLUB` | **compatible** (los periodos los crea el trigger) |
| Historial de migraciones | `__CaccMigraciones` (4) | — | el suyo (20 + 3) | no se copia |

## 6. Los seis MAX+1

En el código del Administrativo **no queda ninguno**: los de `PAGOS` y `JUGADORES` ya se habían reemplazado por secuencias
(`V20261011_04`/`_05`; `PlayerDAO.InsertPlayer` usa `NEXT VALUE FOR dbo.SEQ_JUGADORES`). Los seis están en el **Portal
Deportivo**: seis tablas, diez sentencias.

| # | Tabla (PK) | Archivo B | Método | Consulta actual | Riesgo |
|---|---|---|---|---|---|
| 1 | `CATEGORIAS` (`PK_id_categoria`) | `CACC.DAO/CategoriaDao.cs` | `CrearAsync` | rama `ELSE`: `ISNULL(MAX(PK_id_categoria),0)+1` | rama muerta con IDENTITY, viva en cualquier base sin IDENTITY |
| 2 | `STAFF_CATEGORIAS` (`PK_id_staff_categoria`) | `CategoriaDao.cs` | `AsignarStaffAsync` | rama `ELSE`: `MAX+1` | ídem |
|   | | `StaffDao.cs` | `CrearAsync`, `ActualizarAsync` | `SELECT ISNULL(MAX(…),0)` + contador en memoria | ídem, y además calculado en memoria |
| 3 | `RESPONSABLES` (`PK_id_responsable`) | `JugadorDao.cs` | `CrearAsync`, `GuardarTutorAsync` | `SELECT ISNULL(MAX(…),0)+1` + `SET IDENTITY_INSERT ON` | **activo en la base unificada**: dos altas con tutor a la vez toman el mismo id |
| 4 | `JUGADORES_RESPONSABLES` (`PK_id_jugador_responsable`) | `JugadorDao.cs` | `CrearAsync`, `GuardarTutorAsync` | ídem | **activo** |
| 5 | `USUARIOS` (`PK_id_usuario`) | `StaffDao.cs` | `CrearAsync` | rama `ELSE`: `MAX+1` | rama muerta con IDENTITY |
| 6 | `STAFF` (`PK_id_staff`) | `StaffDao.cs` | `CrearAsync` | rama `ELSE`: `MAX+1` | rama muerta con IDENTITY |

**Hallazgo**: `cacc-unificacion/docs/hallazgos-otro-equipo.md` daba el punto 2 por resuelto, pero `ellos-definitivo.patch`
mantiene el `MAX+1` y le agrega `SET IDENTITY_INSERT ON`: deja de fallar con una alta sola, pero no con dos a la vez, y
exige permiso `ALTER` sobre la tabla. `probar.ps1` no lo veía porque prueba de a una alta.

**Resolución**: `cacc-unificacion/parches/ellos-ids-sin-max-mas-uno.patch` (aplica limpio después del definitivo y del de
nombres; `armar.ps1` lo aplica mientras el código de B tenga `ISNULL(MAX(`). Saca los 10 `MAX` y las ramas por
`IsIdentity`: el `INSERT` no manda el id y lo lee con `SCOPE_IDENTITY()`. Se corrigió también el registro de hallazgos.

### Concurrencia (sesiones reales de SQL Server)
| Prueba | Carga | Resultado |
|---|---|---|
| SQL directo, mecanismo acordado (`pruebas/concurrencia_ids.ps1`, fase 1) | 8 sesiones × 50 altas en PERSONAS, JUGADORES, PAGOS y las 6 tablas | **400 altas por tabla, 0 errores, 400 ids distintos en cada una** |
| SQL directo, patrón actual de B (fase 2: `MAX+1` + `IDENTITY_INSERT`) | 8 × 50 en RESPONSABLES | **343 de 400 rechazadas** por PK duplicada |
| DAO reales de B, código actual (consola en el área temporal, 8 tareas × 10) | Categoría, staff con categorías, jugador con tutor, guardar tutor | **64 errores** (PK de RESPONSABLES); 125 de 160 jugadores (35 altas perdidas enteras) |
| DAO reales de B, con el parche | ídem | **0 errores**; 160 responsables, 160 vínculos, 160 jugadores |
| DAO del Administrativo (`UnificacionIdsIntegrationTests`) | 8 altas simultáneas con `PlayerDAO`, transacciones superpuestas | **PASS**: ids distintos de PERSONAS, JUGADORES y PAGOS |

La seguridad sale de primitivas de SQL Server (`IDENTITY`, `SEQUENCE`); no hay reintentos, demoras ni locks de aplicación.
La única pausa (2 ms) está en la fase 2 y representa la ida y vuelta de red entre los dos comandos de `JugadorDao`.

## 7. SEQUENCE

Ya existían (`V20261011_04`/`_05` y el esquema v2): `SEQ_JUGADORES` y `SEQ_PAGOS`, `INT`, `START WITH 1`, `INCREMENT BY 1`,
`NO CACHE`, como DEFAULT de `PK_id_jugador` y `PK_id_pago`. **Se reutilizan; no se creó ninguna.**
Al importar, A conserva sus ids y después cada secuencia se reinicia en `MAX(id) + 1` solo si eso es mayor que su próximo
valor (nunca retrocede). Resultado: `SEQ_JUGADORES` próximo 584 (máximo 583), `SEQ_PAGOS` 1259 (máximo 1258); todas las
IDENTITY por encima del máximo presente (paso 4, grupo IDS).

## 8. V20260929_07

**Problema.** El `UPDATE dbo.USUARIO SET acceso_portal = 1, rol_portal = 1 WHERE FK_id_rol = 1 AND activo = 1 AND
acceso_portal = 0` supone que el rol 1 es el SuperAdmin del Administrativo. En una base que ya trae usuarios del Deportivo,
el rol 1 es **su** "Administrador (Tesorero)": el UPDATE lo convierte en SuperAdmin de este portal. Ni siquiera alcanza con
mirar el nombre del rol: en `CCACUnificada` el rol 1 se sigue llamando "Administrador General" y el usuario 3 viene de B.

**Protección** (`database/V20260929_07__acceso_administrador_rol_portal.sql`; es un script manual, no está en `migrations/`
ni tiene hash registrado):
- En una base sin `dbo.USUARIO` (la unificada) no hace nada: ni columnas, ni datos, ni trigger.
- Solo habilita a quien cumple todo: `FK_id_rol = 1`, activo, sin acceso ni rol de portal, **y con `password_hash`** (la
  credencial que solo escribe el Administrativo desde V20260929_02; las cuentas de B traen la suya en `contrasenia`).
- A lo sumo **una** fila; con más, se detiene sin tocar nada.
- Primero lista y cuenta las filas (SELECT equivalente); si el UPDATE afecta otra cantidad, se revierte.
- Avisa si `ROLES` no es el catálogo original del Administrativo.
- El trigger se crea con SQL dinámico (un `CREATE TRIGGER` sobre una tabla inexistente no compila ni con `NOEXEC`). Su
  cuerpo es el mismo: se comparó el hash del texto creado por la versión original y por la protegida.

**Prueba** (`pruebas/v07_simulacion.ps1`, sobre copias restauradas, con el SuperAdmin vuelto al estado previo a V07):

| Caso | Criterio original contaría | Esperadas | Habilitados después | |
|---|---|---|---|---|
| Original, base mixta A+B (`CCACUnificada`) | 2 | 1 | usuarios 1 **y 3 (de B)** | ❌ el bug |
| Protegido, base A | 1 | 1 | 1 | ✅ |
| Protegido, repetido sobre A | 0 | 0 | sin cambios | ✅ idempotente |
| Protegido, base mixta A+B | 2 | 1 | 1 (el 3 no) | ✅ |
| Protegido, base unificada | n/a | 0 | huella idéntica, salida 0 | ✅ |
| Trigger de la versión protegida = original | | | mismo hash | ✅ |

En la importación (paso 3) esto no depende de V07: `acceso_portal`/`rol_portal` se copian de A tal cual y los usuarios de B
entran sin acceso. Verificado en el paso 4: los usuarios con acceso son exactamente los que ya lo tenían en A (1), y ningún
usuario de B tiene acceso.

## 9. Usuario desde Persona

En el esquema unificado `USUARIOS` **no tiene** nombre, apellido ni DNI: el código los lee de `PERSONAS` por
`FK_id_persona` (AuthDao, AccountAccessDao, UsuariosPortalDao, trigger `TR_AUDITORIA_IDENTIDAD_ACTOR`). No se agregaron
columnas. En A esas columnas existían pero estaban vacías en las 2 cuentas (verificado), así que no se pierde nada.
Cada usuario se vincula a la persona unificada por el mapa de personas (que salió del DNI), nunca por nombre y apellido.
Paso 4: 0 diferencias entre la persona del usuario y la persona traducida del origen.

## 10. Remapeo por DNI

### Calidad del DNI (antes de usarlo como clave)
| | A | B |
|---|---|---|
| Registros | 578 | 29 |
| NULL / vacíos / con espacios, puntos o guiones / no numéricos / largo fuera de 6–9 | 0 / 0 / 0 / 0 / 0 | 0 / 0 / 0 / 0 / 0 |
| DNI repetidos dentro del mismo sistema | 3 | 8 |
| Tipo | `varchar(20)` | `varchar(20)` |

DNI compartidos entre A y B: 9. Personas de B cuyo id choca con un id de A: 23 (por eso el mapa).
El script además marca como conflicto dos DNI que solo difieran en ceros a la izquierda (hoy ninguno).

### Conflictos (`migra.conflicto_dni`) — sin resolución, la importación se detiene (`Msg 53001`)
| DNI | Registros | Motivo |
|---|---|---|
| 49404226 | A:94, A:95 (ANDRADA FABRICIO JAVIER) | repetido en A, los dos con jugador |
| 56893324 | A:490 (2 pagos), A:491 (CARRIZO BENJAMÍN AARON) | repetido en A, los dos con jugador |
| 56893332 | A:496 (2 pagos), A:497 (FUENTES PIZARRO MATEO) | repetido en A, los dos con jugador |
| 47265479 | A:1, B:1, B:11 (ARRASCAETA JULIAN) | repetido en B con datos distintos: tres fechas de nacimiento (2007-09-17, 2007-03-15, 2009-07-16) |

Para probar el mecanismo se cargaron decisiones **de prueba** (`resolucion_dni_PRUEBA.sql`, `es_prueba = 1`; el paso 4 lo
advierte): queda el registro con pagos o el de id más bajo; en ARRASCAETA, el de A. **No son decisiones del club.**

### Unificaciones automáticas
- 7 pares de B idénticos en todo y con un solo registro con dependientes → unidos.
- 9 personas en A y B → unidas con datos de A. En 8 la fecha de nacimiento difería: se quedó la de A y las de B están
  en `migra.transformacion` (14 por precedencia de A + 2 por la resolución de ARRASCAETA).

### Mapeo de ids
`migra.mapa (entidad, origen, id_origen, id_unificado, regla)` con reglas `ID_CONSERVADO` (A), `ID_NUEVO` (B) y
`FUSIONADO`. Cubre PERSONAS, JUGADORES, USUARIOS, STAFF, STAFF_CATEGORIAS, RESPONSABLES, JUGADORES_RESPONSABLES,
FICHAS_MEDICAS, HISTORIAL_PARTIDOS, JUGADORES_PARTIDOS, ASISTENCIAS, TIPOS_DESCUENTO, JUGADORES_DESCUENTOS, PAGOS y
AUDITORIA_CAMBIOS. Ejemplo: la persona B:21 (Zárate) → 582; su usuario B:21 → 3; su staff B:21 → 1.

### Transformaciones registradas (`migra.transformacion`, con valor original)
| Regla | Cantidad |
|---|---|
| Género de jugador inferido por categoría | 570 (565 de A + 5 de B) |
| Género completado desde B en una persona de A | 9 |
| Fecha de nacimiento de B descartada (precedencia de A / resolución) | 14 / 2 |
| Datos de jugador de B que completan al de A (club, posición, ficha de liga, peso, etc.) | 31 |
| Periodo abierto con fecha de la migración (A no tiene `fecha_alta`) | 580 |
| Correo de B descartado por no ser un correo (`mzarate`, `rdelolmo`, `screspo`) | 3 |
| Contraseña en texto plano no copiada (como V20261011_06) | 1 |
| Rol de A sin equivalente (Usuario Común) | 1 |
| Nombre de tabla unificado en la auditoría de A | 2 |

## 11. Scripts y orden

| Archivo | Qué es |
|---|---|
| `database/unificacion/00_ejecutar_prueba.ps1` | arma todo desde cero, respalda orígenes y compara su huella |
| `01_preparar_migra.sql` | esquema `migra` (idempotente; se niega a correr fuera de una base unificada) |
| `02_analizar_identidades.sql` | normaliza DNI, arma grupos, detecta conflictos, aplica decisiones (repetible) |
| `03_importar.sql` | importación en una transacción (**no** repetible: exige base recién creada) |
| `04_validar.sql` | 91 verificaciones; termina con error si alguna es FAIL (repetible) |
| `resolucion_dni_PRUEBA.sql` | decisiones de prueba |
| `pruebas/concurrencia_ids.ps1`, `pruebas/v07_simulacion.ps1` | pruebas complementarias |

Orden de carga (de las FK del esquema): catálogos (verificados) → PERSONAS → JUGADORES → USUARIOS → STAFF →
STAFF_CATEGORIAS → RESPONSABLES → JUGADORES_RESPONSABLES → FICHAS_MEDICAS → HISTORIAL_PARTIDOS → JUGADORES_PARTIDOS →
ASISTENCIAS → JUGADORES_DESCUENTOS → PAGOS → AUDITORIA_CAMBIOS.

**Reejecución**: el proceso completo es reproducible desde un estado conocido (el runner borra y crea la base destino;
dos corridas completas desde cero dieron resultados idénticos). Los pasos 2 y 3 sobre una base ya importada se rechazan (`Msg 53000` y `Msg 53100`, probado: la base no cambia). Datos de
origen sin traducción definida (becas de B, aranceles o invitaciones de A, columnas que el esquema unificado no tiene)
detienen la importación en lugar de perderse.

No va en `database/migrations/`: cruza bases, usa `GO` y controla su transacción, cosas que el ejecutor de la API rechaza.

## 12. Triggers

| | |
|---|---|
| A | 11 de auditoría |
| B | ninguno |
| Unificado (quedan) | 14: `TR_AUDIT_ARANCELES`, `TR_AUDIT_ARANCELES_INSCRIPCION`, `TR_AUDIT_CATEGORIAS`, `TR_AUDIT_JUGADORES`, `TR_AUDIT_JUGADORES_DESCUENTOS`, `TR_AUDIT_PAGOS`, `TR_AUDIT_PERSONAS`, `TR_AUDIT_TIPOS_DESCUENTO`, `TR_AUDIT_TOKEN_ACCESO_CUENTA`, `TR_AUDIT_USUARIOS`, `TR_AUDITORIA_IDENTIDAD_ACTOR`, `TR_JUGADORES_PERIODOS`, `TR_JUGADORES_GENERO_OBLIGATORIO`, `TR_PERSONAS_GENERO_DE_JUGADOR` |

Durante la carga se apagan solo los de auditoría (copiar no es una operación de usuario; queda en `migra.ejecucion`) y se
vuelven a prender antes del COMMIT. Los de reglas quedan activos y validan la carga. Comprobado: la auditoría tiene después
exactamente los 2 eventos de A (la carga no generó ninguno), 580 periodos para 580 jugadores, ningún jugador sin género, y
los 14 triggers habilitados. Todos los `INSERT` en tablas con trigger usan `OUTPUT … INTO` (nunca `OUTPUT` solo).

## 13. Validación (corrida final)

### Conteos
| Entidad | A | B | Esperado (calculado desde los orígenes) | Unificado | |
|---|---|---|---|---|---|
| PERSONAS | 578 | 29 | 578 + 29 − 20 registros con DNI ya presente = **587** | 587 | PASS |
| JUGADORES | 577 | 15 | personas distintas con jugador = **580** | 580 | PASS |
| JUGADORES_PERIODOS | — | — | 580 | 580 | PASS |
| USUARIOS | 2 | 3 | 5 | 5 | PASS |
| STAFF / STAFF_CATEGORIAS | 0 / 0 | 3 / 0 | 3 / 0 | 3 / 0 | PASS |
| RESPONSABLES | 487 | 2 | 489 | 489 | PASS |
| JUGADORES_RESPONSABLES | 0 | 2 | 2 | 2 | PASS |
| FICHAS_MEDICAS | 0 | 6 | 6 | 6 | PASS |
| HISTORIAL_PARTIDOS / JUGADORES_PARTIDOS | — | 10 / 8 | 10 / 8 | 10 / 8 | PASS |
| ASISTENCIAS | 0 | 0 | 0 | 0 | PASS |
| JUGADORES_DESCUENTOS | 9 | 0 | 9 | 9 | PASS |
| PAGOS | 1258 | 0 | 1258 | 1258 | PASS |
| PAGOS: suma de `monto_final` y de `monto_base` | 93.842.928,00 | 0 | 93.842.928,00 | 93.842.928,00 | PASS |
| AUDITORIA_CAMBIOS | 2 | — | 2 | 2 | PASS |

Los 20 "ya presentes" = 9 de B con DNI de A + 7 duplicados idénticos de B + 1 de B y 3 de A unidos por las decisiones de prueba.
Además, cada registro de origen de cada entidad tiene su id unificado y A conserva sus ids (26 verificaciones "CONSERVADO", todas PASS).

### FK y remapeo (0 diferencias en todas)
DNI de destino = DNI normalizado del origen; ninguna persona unificada junta DNI distintos; persona del jugador, categoría,
persona del usuario, usuario del staff, staff y categoría, persona/correo/teléfono del responsable, jugador y responsable
del vínculo, jugador y datos médicos de la ficha, partido y jugador, categoría/fecha/rival del partido, jugador/tipo/valor/
vigencia de la beca, y jugador/montos/fechas/estado/concepto/medio de cada pago. `DBCC CHECKCONSTRAINTS`: 0 filas.
FK y CHECK deshabilitados o no confiables: 0.

### Huérfanos
0 nuevos en: jugadores sin persona, usuarios sin persona, staff sin usuario, staff_categorías incompletas, pagos sin jugador,
asistencias sin jugador ni staff, vínculos jugador-responsable incompletos, fichas sin jugador, jugadores_partidos incompletos.
**Preexistentes** (no los produjo la migración): 487 responsables de A sin persona y sin jugador vinculado.

### Duplicados
0 personas con el mismo DNI, 0 con más de un jugador, 0 con más de un usuario.

### Muestras
| Caso | Persona unificada | Registros de origen | Verificación |
|---|---|---|---|
| Solo en A | 3 BAZÁN NICOLÁS FERNANDO | A:3 (id conservado) | jugador 3, 1 pago, género inferido |
| Solo en B | 582 Zárate Mariano | B:21 (id nuevo) | usuario 3 (rol Tesorero, **sin acceso** al Administrativo), staff 1 |
| En A y en B | 2 BARRETO SANTINO DAVID | A:2 + B:2 + B:12 | un solo jugador (2) con sus 3 pagos de A; fecha de A |
| Conflicto resuelto | 1 ARRASCAETA JULIAN | A:1 + B:1 + B:11 | jugador 1 con 3 pagos de A **y** la ficha médica que tenía en B |

## 14. Pruebas de DAO contra SQL Server real

`dotnet test ApiGestion.Tests` con `CACC_TEST_DB` (cada prueba en una transacción que se revierte):

| Corrida | Base | Resultado |
|---|---|---|
| Previa (sin datos) | esquema unificado vacío | 595 / 607: los 12 que fallan piden datos (una cuenta Administrador habilitada, eventos de auditoría, un usuario para `FK_PAGOS_USUARIO_REGISTRO`). Son precondiciones de las pruebas, no defectos. |
| Previa | ídem + `cacc-unificacion/pruebas/seed_cuentas_prueba.sql` (cuentas ficticias) | **607 / 607** |
| Posterior | **copia** de la base migrada + mismo seed (la migrada no se toca) | **607 / 607** |

Corrieron (no se omitieron) 18 clases contra la base real, entre ellas: `AccountAccessDao` (11), `UsuariosPortalDao` (8),
`UsuarioApiPermisos`, `PagosDaoAltaBajaYAnulacion` (7), `PlayerDao`, `PlayerSearch` (3), `JugadorGeneroCategoria` (13),
`EstadoDeudaDao` (5), `EnrollmentFeeDao` (3), `InscripcionAnual` (12), `InscripcionesProgramadas` (4), `CargosDeAlta` (3),
`CuotasFaltantesDao`, `IntentosLoginDao`, `AuditReferenceResolver` (3) y la nueva **`UnificacionIdsIntegrationTests`** (2:
altas simultáneas con ids distintos; ids por encima de los existentes). Cubren persona, usuario, jugador, pagos, categorías,
autenticación, cuotas, lectura, alta, modificación, FK, ids y transacciones. **STAFF no tiene DAO en el Administrativo**: lo
cubren los DAO de B con la consola de la sección 6 (95/95 pruebas unitarias de B antes y después del parche, y 0 errores
con sus DAO reales contra la base unificada).

## 15. Hallazgos y pendientes

| # | Hallazgo / pendiente | Estado |
|---|---|---|
| 1 | El punto 2 de `hallazgos-otro-equipo.md` no estaba resuelto (MAX+1 + `IDENTITY_INSERT`). | Corregido con `ellos-ids-sin-max-mas-uno.patch`; **falta que el otro equipo lo aplique y lo revise en su repo** |
| 2 | V20260929_07 promovía a SuperAdmin a usuarios de B. | Corregido y probado |
| 3 | 4 conflictos de DNI (3 en A con dos jugadores cada uno; ARRASCAETA con tres fechas). | **Decisión del club pendiente** |
| 4 | 570 géneros inferidos y 16 fechas de nacimiento descartadas. | Revisión del club pendiente (listado en `migra.transformacion`) |
| 5 | `decisiones.md` de `cacc-unificacion` dice "base vacía; solo jugadores del Excel", y esta HU importa A y B. | **Decidir qué datos van a producción** |
| 6 | La documentación decía SQL Server 2016; el código ya requiere 2017. | Corregido; falta confirmar la versión de producción con el otro equipo |
| 7 | `parches/aplicar-parche.ps1` del banco deja `\r\r\n` cuando git tiene `core.autocrlf=true` (`git archive` + su conversión). | Detectado, sin corregir (fuera del alcance) |
| 8 | `B.USUARIOS.email` guarda nombres de usuario; el contrato exige correo. | Se descartan en la importación (anotado); el otro equipo debería pedir un correo real |
| 9 | 487 responsables de A sin persona y sin jugador. | Inconsistencia previa, documentada |
| 10 | Los scripts históricos (incluido V07) tienen `USE ClubCamionerosPruebaCuatro;` fijo (el instructivo pide reemplazarlo). | Sin cambios: riesgo de correrlo sobre la base equivocada |

Sin cambios en reglas de negocio: pagos, aranceles, becas, vencimientos, permisos y política deportiva quedan como estaban.
Lo único que toca permisos es V07, para que no otorgue SuperAdmin a quien no corresponde.

## 16. Respaldo y rollback

- Prueba: respaldo `COPY_ONLY` + `CHECKSUM` de A y B y `RESTORE VERIFYONLY` ("The backup set on file 1 is valid") en
  `%TEMP%\cacc-hu053\`; la base destino se crea desde cero en cada corrida; huella de A y B igual antes y después.
- Producción: procedimiento en [`database/unificacion/README.md`](database/unificacion/README.md#producción-fuera-del-alcance-de-hu-053).
  No se ejecutó en producción.

## 17. Archivos

| Archivo | |
|---|---|
| `database/unificacion/*` (8 archivos) | nuevos: scripts, runner, pruebas, README |
| `database/V20260929_07__acceso_administrador_rol_portal.sql` | modificado: UPDATE protegido, sin efecto en la base unificada |
| `ApiGestion.Tests/Unificacion.Integration.Tests.cs` | nuevo: concurrencia de ids con `PlayerDAO` |
| `database/README.md`, `README.md`, `database/produccion/README.md` | modificados: carpeta `unificacion/`, SQL Server 2017 |
| `QA-HU-053.md` | este documento |
| `cacc-unificacion/parches/ellos-ids-sin-max-mas-uno.patch` | nuevo: los 6 MAX+1 de B |
| `cacc-unificacion/scripts/armar.ps1`, `docs/hallazgos-otro-equipo.md` | modificados: aplica el parche; punto 2 corregido |

## 18. Definición de Listo

| Criterio | Estado | Evidencia |
|---|---|---|
| Roles acordados | PASS | sección 3 |
| Criterio de ids acordado | PASS | sección 3 (IDENTITY + SEQUENCE en PAGOS/JUGADORES) |
| Versión de SQL Server acordada | **BLOCKED** | mínimo 2017 demostrado; versión de producción sin confirmar con el otro equipo |
| Los 6 MAX+1 identificados | PASS | sección 6 (6 tablas, 10 sentencias) |
| Los 6 reemplazados por un mecanismo seguro | PASS en el parche / **BLOCKED** en el repo de B | parche probado con sus DAO reales (0 errores); falta aplicarlo en su repo |
| V20260929_07 protegido | PASS | sección 8 |
| Usuario y persona relacionados | PASS | sección 9 |
| Personas de B remapeadas por DNI | PASS | sección 10 |
| Conflictos de DNI tratados/documentados | PASS (documentados) / **BLOCKED** (decisión real) | sección 10 |
| FK preservadas | PASS | sección 13 |
| Triggers incluidos | PASS | sección 12 |
| DAO críticos con pruebas contra SQL Server real | PASS | sección 14 |
| Script/migración reproducible | PASS | sección 11 |
| Ejecutado en una base de prueba | PASS | `ClubCamionerosUnificadaV2_HU053` |
| Registros de A conservados | PASS | 26 verificaciones CONSERVADO (incluye ids de A sin cambios) |
| Registros de B conservados | PASS | ídem |
| Sin duplicados indebidos | PASS | sección 13 |
| Sin nuevos huérfanos | PASS | sección 13 |
| Conteos reconciliados | PASS | sección 13 |
| Validación conjunta con el otro equipo | **BLOCKED** | no realizada |
| Code review | **BLOCKED** | no realizado |
| Sin escribir en la base de referencia/productiva | PASS | huella de A y B sin cambios; solo bases `_HU053`/`_Prueba` |

### Estado final: **BLOCKED**
Para cerrarla: decisión del club sobre los 4 conflictos (y revisión de géneros/fechas), validación conjunta y aplicación del
parche en el repo del Portal Deportivo, code review, confirmación de la versión de SQL Server de producción y definición de
qué datos se cargan en producción.
