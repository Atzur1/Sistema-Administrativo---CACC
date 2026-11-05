# HU064 — Auditoría estática, 04/11/2026

Estado: BLOCKED para aceptación integral. Revisión de código, sin tests, builds ni solicitudes API. Las pruebas históricas del QA no se reejecutaron por esta auditoría.

## Alcance y trazabilidad

Administrativo auditado: repositorio de trabajo `Sistema-Administrativo---CACC`, rama `FramD`, HEAD `f9d4514`, con modificaciones locales; no equivale a un binario puro de ese commit. Deportivo auditado: `C:/Users/FramD/Documents/Tesis/HU-064-entorno/App-Club-Atletico-Camioneros`, HEAD `a9e690f5`, rama `fix/SCRUM-805-credencial-integracion`. Unificación en ese entorno: HEAD `61e1030`, rama `fix/SCRUM-805-credencial-integracion`. Estos HEAD se consultaron con git; no acreditan por sí solos qué binario está ejecutándose. El manifiesto del responsable del entorno debe resolver esa correspondencia. No se certifica la rama literal `feat/base-unificada` que figura en el informe histórico.

## Regla única y contradicción de beca

Contraste adicional de copias para ejecución: `git diff --no-index --ignore-space-at-eol` no detectó diferencias textuales en EstadoDeudaDao.cs ni BeneficiosService.cs entre workspace y `cacc-unificacion/trabajo/nuestro`. En EstadoDeudaClient.cs de `trabajo/ellos` sí hay diferencias: la copia usa propiedad string no nullable, constante del header y validación de clave en opciones; el fork actual delega configuración/validación a ClienteAdministrativo. El método de consultas HTTP y traducción de estado no cambió en ese diff. No equiparar hashes de fuente con identidad de DLL.

- `DaoLibrary/EstadoDeudaDao.cs:61-71`: lee pendientes, calcula saldo ajustado por beneficio y cuenta exclusivamente concepto Cuota, saldo positivo y vencimiento cumplido. `EntityLibrary/EstadoDeuda.cs:12-18`: 0 habilitado, 1 solo entrenamientos, 2+ inhabilitado; baja tiene prioridad.
- `DaoLibrary/SqlCalendarioCuotas.cs:9`: vencimiento inclusive desde el día 10 del mes siguiente al período. El campo fecha_vencimiento representa período mensual, no ese día de corte.
- `DaoLibrary/DescuentosSql.cs:28-46`: beneficio por período de cuota y fecha de cancelación; saldo base menos beneficio. Un beneficio cancelado puede conservar aplicación histórica: no debe confundirse con beneficio vigente hoy.
- `ServiceLibrary/BeneficiosService.cs:74-95`: el primer período admisible es la última cuota emitible sin pagos, o uno futuro si tiene pagos; rechaza inicio anterior. `:249-254` explica que becar anteriores sería condonar deuda.
- Por ello el requisito jugador restringido por deuda vencida → asignación de beca vigente → Al día está BLOCKED con el flujo actual. No corresponde cambiar la regla ni sustituir el requisito por prevención. `QA-HU-064.md:69,97` presenta prevención como criterio acordado y PASS: esa afirmación no es autorización vigente ni prueba del criterio requerido.

## Pago, rollback y anulación

`ServiceLibrary/PagosService.cs:29-31,91-106` ejecuta registro y actualización/eliminación de pendiente en una transacción. `:132-178` registra anulación y devuelve monto a pendiente o lo reabre. `DaoLibrary/PaymentTransactionDao.cs:18-21` delega a `SqlTransactionRunner`; `DaoLibrary/SqlTransactionRunner.cs:24-35` confirma después de la operación y revierte ante excepción. Esto verifica diseño; no sustituye prueba de fallo con comparación de filas.

El caso histórico lote con cuota válida + ID inexistente verifica rechazo previo, NO rollback de escrituras: `PagosService.cs:239-240` carga todas y valida; `:482-486` lanza por faltantes antes del bucle y escrituras. El cobro final usa UPDATE en lote (`DaoLibrary/PagosDao.cs:321-356`), no INSERT. Una prueba efectiva puede inyectar un fallo mediante trigger AFTER UPDATE temporal, sólo sobre fixture F en BD aislada, después de detectar la transición pendiente→pagado. Para probar además reversión de una escritura anterior al statement fallido, una primera cuota con beneficio parcial ocasiona `ActualizarMontoCobroConDescuento` (`PagosService.cs:255-256`) antes del UPDATE final; el snapshot debe demostrar restauración de monto y FK de beneficio. Es propuesta coordinable, no ejecutada por esta auditoría.

## Revisión del harness inicial

En `hu064-revalidacion.ps1` revisado antes de la corrida completa: el caso beca-exacta esperaba 400 aunque fuente actual mapea NoProcesable a 422; registrar discrepancia fuente/binario si ocurre, sin convertirla en PASS. `Save` contenía `database=$SportsUrlase`, variable equivocada. Los asserts iniciales de anulación no verificaban PAGOS_ANULADOS ni identidad del abono eliminado; pago sólo comprobaba incremento de pagados, sin cantidad/IDs exactos. No había assert automático de allowlist privada ni consulta directa con alcance DT (usaba SA). Estos son hallazgos de una versión en evolución; el resultado final debe indicar cuáles se corrigieron. Los snapshots idénticos ante ID inexistente acreditan ausencia de efectos por rechazo, no escritura revertida.

## Caché y actualización visible

`ApiGestion/Controllers/EstadoDeudaController.cs:56,108` consulta DAO por request. Deportivo `BackEnd/CACC.API/Services/EstadoDeudaClient.cs:94-156` realiza HTTP individual/lote; `Controllers/JugadoresController.cs:66,117` lo invoca en los GET. No se identificó caché de estado en ese recorrido.

Se buscó en todo `FrontEnd/src/**/*.ts`: BehaviorSubject, ReplaySubject, shareReplay, localStorage, sessionStorage, store, cache e interceptores. No aparecieron mecanismos RxJS/store de persistencia del estado de deuda. El almacenamiento hallado es autenticación: `shared/services/auth.service.ts:18,34,51-60,109-110,149` guarda token/rol en localStorage. `app.config.ts:11` registra `core/interceptors/auth.interceptor.ts`, que agrega bearer y trata errores 401/403 (`:7-42`), sin reutilizar respuestas. Existe otro interceptor en shared, sin registro en app.config.

El front conserva arrays en memoria de cada pantalla: `features/admin-home/admin-home.component.ts:124` hace GET al cargar; no se verificó push, polling ni refresco automático de una pantalla abierta. Afirmación válida: siguiente GET vuelve a consultar; no afirmar que una vista abierta cambia sola. Riesgo: fallback optimista `estadoCuota || 'AL DÍA'` en ese archivo `:140` y `features/admin-lista-jugadores/admin-lista-jugadores.ts:160`.

## Autenticación y privacidad

`ApiGestion/Controllers/EstadoDeudaController.cs:17-19,122-134` exige rol, rate limit y acota DT/PF por claims de categoría; DAO incluye categoría principal y membresías (`DaoLibrary/EstadoDeudaDao.cs:48-50`). `ApiGestion/Program.cs:384-385` añade manejo de errores y autenticación de cliente para integración. El fork agrega X-Integration-Key en `BackEnd/CACC.API/Services/ClienteAdministrativo.cs:48-53`; `EstadoDeudaClient.cs` reenvía bearer. No se leyeron valores de secretos.

`ApiGestion/Models/EstadoDeudaResponseDTO.cs:7-28` sólo expone DNI, ID, estado, acceso y fecha de baja. No expone deuda total, cantidad de cuotas, períodos ni medios de pago. Privacidad verificada en contrato estático; falta confirmar payload real con sesión y alcance previstos.

## Medición honesta de latencia

`DaoLibrary/PagosDao.cs:254-259` asigna fecha_hora_registro durante INSERT; NO es timestamp de commit. Commit ocurre después (`SqlTransactionRunner.cs:29`). `hu064-e2e.ps1:84-106` del entorno mide solicitud/respuesta administrativa y siguiente consulta deportiva. Se puede reportar latencia de ese GET y diferencia recepción respuesta administrativa → recepción deportiva. No se puede afirmar latencia exacta commit → reflejo; `QA-HU-064.md:152` debe corregirse.

Esta limitación aplica a la evidencia histórica. La nueva revalidación prepara instrumentación independiente SQL Extended Events: si captura y correlaciona commit real con cada operación, sus resultados podrán medir ese intervalo. Esta auditoría no anticipa éxito ni identidad causal de eventos todavía pendientes.

## Migraciones

El repositorio actual conserva dos `V20261031_01`: aranceles_impacto_auditado e historial_exportaciones_reportes. Worktree SCRUM804, rama `fix/SCRUM-804-migracion-duplicada`, HEAD `b3ee28e`, limpio en git status; renombra historial a `V20261031_02`. No se aplicó ese cambio al repositorio principal por esta auditoría.

Engram/mem_save no disponible. Estándares compactos recibidos: sin registry actual aplicable. No se modificó QA-HU-064.md.
