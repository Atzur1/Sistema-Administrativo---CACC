# Contexto funcional — Portal Administrativo CACC

Última actualización: 2026-09-28 (incluye auditoría de responsables y alcance de cuentas del Portal Administrativo).

Este documento comparte el contexto de negocio vigente del equipo que desarrolla el **Portal Administrativo** del Club Atlético Social y Deportivo Camioneros (CACC). Complementa `README.md`, `estructura-tecnica.md` y `database/README.md`; para los cambios de base de datos, ver `database/InstructivoEquipo.md`; para el estado real del código prevalece siempre el repositorio. Las reglas marcadas como pendientes no deben asumirse como decisiones cerradas.

## 1. Alcance del equipo

El equipo trabaja **únicamente en el Portal Administrativo**. El Portal Deportivo pertenece a otro equipo. La administración de altas de usuarios ya no es responsabilidad funcional confirmada de este equipo; el mecanismo de aprovisionamiento inicial de administradores sigue pendiente de decisión.

El portal administrativo cubre operaciones financieras y de gestión administrativa relacionadas con jugadores: aranceles, cuotas, pagos, descuentos/beneficios, consulta de deuda, historial, reportes y auditoría. Este alcance no asigna al equipo la implementación de portales deportivos, de socios o de sus flujos.

## 2. Reglas funcionales conocidas

### Aranceles

- El arancel debe poder configurarse por **división/categoría** del club, no solo por género. Una actualización puede aplicar a una división específica y permitir montos distintos entre divisiones femeninas y masculinas.
- Género/ rama puede servir para organizar o filtrar divisiones, pero no reemplaza la selección de la división a la que corresponde el arancel.
- Los cambios deben tener vigencia e historial. Las cuotas ya emitidas conservan el importe que les correspondía al emitirse; cambiar un arancel no reescribe deudas históricas.
- La necesidad de configurar aumentos porcentuales o por monto fijo no está confirmada en esta actualización. El código actual programa un nuevo monto y una fecha de vigencia.
- Los importes concretos citados en documentos anteriores (por ejemplo, inscripción masculina o tarifas por rama) son referencias históricas y requieren confirmación antes de tratarlos como valores vigentes.

### Pagos y trazabilidad

- Cada acción que registra o modifica el estado de un pago debe dejar constancia de **quién la realizó y cuándo**.
- Deben distinguirse la fecha efectiva del pago (`fecha_pago`) y la fecha/hora en que la operación quedó registrada (`fecha_hora_registro`). Las cargas retroactivas no deben falsear la hora de registro.
- La identidad del operador debe derivarse de la identidad autenticada en el servidor (por ejemplo, el `idUsuario` del JWT), no confiarse a un identificador enviado por el cliente.
- Las operaciones masivas deben conservar la atribución de la persona que realizó la acción para todos los pagos afectados. Si se necesita historial de cambios posteriores, se debe definir una bitácora append-only que preserve cada evento, en vez de sobrescribir la auditoría anterior.

### Cuentas administrativas y primer acceso del superadministrador

Las altas de administradores, usuarios normales, staff y profesores corresponden al otro equipo, que administra las cuentas en la base compartida. Este equipo no crea ni invita esas cuentas desde el Portal Administrativo.

Necesidades funcionales que deben cubrirse al definir ese flujo:

- El único flujo de creación/restablecimiento de contraseña dentro del alcance de este equipo es el primer acceso del superadministrador del Portal Administrativo, cuya cuenta e identidad se aprovisionan previamente en la base de datos.
- “¿Olvidaste tu contraseña?” solo debe emitir un enlace para el correo configurado como `Security:SuperAdminEmail`; para cualquier otro correo debe responder de forma genérica sin crear ni enviar un token.
- Los enlaces de recuperación deben ser aleatorios, de un solo uso, con vencimiento, almacenados como hash y revocados al completar el flujo.
- Las respuestas de recuperación no deben revelar si una dirección está registrada. Aplicar límites de solicitudes y registrar eventos de seguridad.
- Nunca enviar contraseñas temporales por correo ni guardar contraseñas en texto plano. El superadministrador define una contraseña propia mediante el enlace seguro de recuperación.
- El envío requiere configuración de correo institucional/SMTP o proveedor transaccional, manejo de errores y configuración de secretos fuera del repositorio.

El Portal Administrativo no incluye altas ni invitaciones. La API limita el restablecimiento al superadministrador indicado por configuración y requiere que su registro activo, rol e identidad existan previamente en la base de datos. La identidad no se completa desde la UI; el aprovisionamiento inicial debe incluir nombre, apellido y DNI reales.

## 3. Estado contrastado con el código (2026-09-28)

Observaciones sobre el checkout inspeccionado; no equivalen a una promesa de disponibilidad en producción:

| Área | Estado observado |
|---|---|
| Autenticación y acceso | Login genera JWT con `idUsuario`, correo, rol y versión de credencial. El flujo de restablecimiento por correo está restringido al `Security:SuperAdminEmail` configurado, usa token de un solo uso almacenado como hash y limita solicitudes por IP; el cambio invalida JWT anteriores. Requiere aplicar `V20260929_03` a `V20260929_06` y configurar SMTP, URL pública y correo de superadministrador. |
| Usuarios/permisos | La vista conserva datos de muestra y no crea ni invita cuentas. Las altas de administradores, usuarios, staff y profesores pertenecen al otro equipo y al sistema de cuentas compartido. |
| Aranceles | Cada arancel es por género **o** por categoría (uno solo; migraciones `V20260930_05` a `V20260930_07`). A cada jugador le corresponde el arancel de su categoría si tiene uno vigente (el género no importa) y, si no, el de su género. Los aranceles previos quedaron como "por género". Se permite **un solo arancel por mes y destino** (género o categoría): para cambiar el de un mes hay que cancelarlo (`DELETE api/aranceles/{id}`) y programar el nuevo. Cancelar quita el arancel y las cuotas pendientes sin pagos emitidas con él, y se rechaza si alguna ya tiene pagos o si el arancel es de un mes anterior (historial). |
| Cuotas | `GeneradorCuotasMensuales` (servicio en segundo plano de la API) crea al iniciar y cada 6 horas la cuota del mes en curso de quien aún no la tiene, con el arancel vigente según la jerarquía de arriba; si nadie cambió el arancel, se repite el anterior. Solo genera el mes en curso (no recupera meses atrasados). Al programar un arancel para el **mes en curso**, las cuotas pendientes sin pagos de los jugadores alcanzados se vuelven a emitir con el monto nuevo y las que ya tienen pagos se conservan; si el arancel es de un **mes futuro** no se emite nada (lo hace el generador cuando ese mes empieza); si es de un mes pasado solo se completan las cuotas que falten. Se puede apagar con `GeneracionCuotas:Habilitada=false`. |
| Pagos y trazabilidad | El DAO propaga el usuario autenticado a SQL Server y las migraciones `V20260929_03` y `V20260929_06` registran acciones e identidad histórica del operador. Auditoría presenta nombre, apellido, DNI y fecha/hora; pagos, aranceles y beneficios muestran solo nombre y apellido del responsable. Los historiales anteriores a la auditoría quedan sin responsable cuando el dato no puede recuperarse con certeza. |
| Descuentos/beneficios | Hay soporte de descuentos porcentuales y fijos. El listado incluye la persona que asignó el beneficio (nombre y apellido); Auditoría conserva el detalle completo de cada cambio. |
| Medios de pago | El código inspeccionado ofrece Transferencia y Efectivo. La mención histórica de Débito no refleja la UI/API actual; confirmar antes de reintroducirlo. |

Las descripciones de estado de `project_2.md` y de documentos anteriores incluyen afirmaciones antiguas (por ejemplo, pagos simulados, ausencia de endpoints y ausencia de descuentos). Para el estado funcional actual, verificar código, migraciones y pruebas, no copiar esas afirmaciones.

## 4. Próximas líneas de trabajo conocidas

1. Aplicar las migraciones pendientes `V20260929_03` a `V20260929_06` en orden, después de respaldo, en cada base compartida.
2. Aprovisionar en la base compartida la identidad real del superadministrador y configurar `Security:SuperAdminEmail`, SMTP y `Email:PublicBaseUrl` mediante configuración segura por entorno; validar el enlace de recuperación, expiración, revocación y rechazo de correos de otros usuarios.
3. Extender aranceles para elegir una división real del catálogo y mantener vigencias independientes por división. Revisar la generación de cuotas para tomar la tarifa correcta según la división del jugador y preservar importes históricos.
4. Coordinar el consumo del padrón compartido con el otro equipo; este proyecto no debe duplicar el alta ni la administración de cuentas.

Configuración de correo para probar la rama: establecer `Email__SmtpHost`, `Email__SmtpPort`, `Email__EnableSsl`, `Email__Username`, `Email__Password`, `Email__From`, `Email__PublicBaseUrl` y `Security__SuperAdminEmail` como variables de entorno de la API. No completar credenciales ni DNI personales en archivos versionados. En entornos compartidos, `Email__PublicBaseUrl` debe ser HTTPS y apuntar al frontend alcanzable por quienes reciben el correo; HTTP solo se admite para loopback local.

Los cambios de seguridad o de esquema de base de datos requieren migraciones compatibles, revisables e idempotentes según las convenciones del repositorio. No guardar secretos en el código ni en archivos versionados.

## 5. Preguntas de negocio que siguen abiertas

- ¿Cuál es el correo e identidad real (nombre, apellido y DNI) del superadministrador que se aprovisionará previamente?
- ¿Qué proveedor/cuenta institucional enviará el restablecimiento del superadministrador?
- ¿Cómo se integrará la vista de Usuarios y Permisos con el padrón que mantiene el otro equipo?
- ¿Cuáles son las divisiones vigentes del club y cómo se relacionan con género/categoría? ¿Qué arancel aplica a cada una y desde qué fecha?
- ¿La cuota masculina es uniforme entre divisiones o también tendrá importes diferenciados?
- ¿Se deben admitir aumentos porcentuales además de fijar un nuevo importe?
- ¿Hay fecha de vencimiento, recargos por mora o reglas de reinscripción que deban reflejarse en cuotas?
- ¿Débito sigue siendo un medio requerido, o los medios autorizados son Transferencia y Efectivo?

