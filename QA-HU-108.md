# QA HU-108 — Vista previa e historial de exportaciones

Fecha de verificación: 31/10/2026. El club utiliza hora Argentina.

## Archivos de esta HU

El workspace ya contenía cambios HU-107. Este inventario identifica aportes HU-108, **no atribuye a HU-108 el diff completo de archivos compartidos**.

| Área | Archivos HU-108 |
|---|---|
| Backend nuevo | `DaoLibrary/IReportesDao.cs`, `DaoLibrary/ReportesDao.cs`, `EntityLibrary/ExportacionReporte.cs` |
| Backend existente | `ApiGestion/Controllers/ReportesController.cs` (conteo, historial, registro tras generación); `ApiGestion/Program.cs` (registro DI); `DaoLibrary/PagosDao.cs`, `DaoLibrary/DiscountDao.cs`, `DaoLibrary/ArancelesDao.cs` (selecciones compartidas); `EntityLibrary/RelojNegocio.cs` (fecha/hora club) |
| Migración nueva | `database/migrations/V20261031_01__historial_exportaciones_reportes.sql` |
| Frontend nuevo | `frontend-cacc/src/app/shared/export-menu/report-export-menu.ts`, `report-export-menu.spec.ts`; `frontend-cacc/src/app/admin-portal/reportes/reportes.spec.ts` |
| Frontend existente | `frontend-cacc/src/app/services/reportes.ts`; `frontend-cacc/src/app/admin-portal/reportes/reportes.ts`, `.html`, `.css`; `frontend-cacc/src/app/admin-portal/deudas-morosidad/deudas-morosidad.ts`, `.html`, `.spec.ts`; `frontend-cacc/src/app/admin-portal/recaudacion-mensual/recaudacion-mensual.ts`, `.html`, `.spec.ts` (integración wrapper y fakes de regresión) |
| Tests backend | `ApiGestion.Tests/FakeReportesDao.cs`, `ReportesHu108.Tests.cs`, `ReportesDao.Integration.Tests.cs`, `ReportesAuthorization.Tests.cs`; ampliación de `ReportesController.Tests.cs` |
| QA/evidencia | `QA-HU-108.md`, `qa/HU-108/resultados/HU108.trx`, `backend-completo.trx`, doce archivos PDF/CSV en `qa/HU-108/evidencia/` |

Compartidos especialmente con HU-107: `DaoLibrary/PagosDao.cs`, `DaoLibrary/RecaudacionDao.cs`, vistas/specs de `recaudacion-mensual` y reloj de negocio. Los cambios previos en `RecaudacionController`, DTO/modelos de recaudación, Services, builders, estilos y evidencia HU-107 se preservaron; no forman por sí solos una modificación nueva atribuible a HU-108.

## Alcance y límites de evidencia

Se revisaron los cinco reportes existentes, sus diez rutas PDF/CSV, Services/DAO, auditoría y componentes de exportación/paginación. Se reutilizaron los exportadores existentes. No se ejecutó build independiente por instrucción del proyecto; `dotnet test` compila sus pruebas de forma inherente.

**PASS automatizado SQL, Controller, archivos y permisos; BLOCKED navegador:** el agente backend configuró la conexión de integración para la base de pruebas HU-107 disponible mediante SQL Server local y aplicó la migración HU-108, sin recrear/borrar la base. Se generaron 12 archivos con consultas DAO, Controller y builders reales. Las inserciones de fixtures/historial se revirtieron con rollback. No se verificó descarga desde navegador autenticado ni guardado físico en el equipo del usuario: no hay servidor UI activo en 4200 ni API en 5118, y no se inició un servidor que disparara build por restricción del proyecto. No se utilizaron datos de producción para afirmar resultados.

## Inventario y fuente única

Todas las rutas de exportación son `GET /api/reportes/{tipo}/{formato}`; formatos `pdf` y `csv`. Cada tipo dispone de `GET /api/reportes/{tipo}/conteo`. El conteo responde `{ tipo, cantidadFilas }` y no recibe identidad desde el navegador.

| Reporte / tipo estable | Filtros exportación y conteo | Rutas actuales (sufijo PDF/CSV) | Fuente / inclusión / orden |
|---|---|---|---|
| Deudores / `deudores` | `idCategoria` opcional | `deudores/pdf`, `deudores/csv` | `PagosDao.DeudoresSeleccion`, reutilizado por `ObtenerPendientesAgrupados`; cuotas pendientes, saldo ajustado > 0, agrupación por jugador; cuotas y monto descendentes |
| Cuotas y pagos / `pagos-recientes` | `top`, defecto 500; rango 1..1000 | `pagos-recientes/pdf`, `pagos-recientes/csv` | `PagosDao.PagosRegistradosCtes`, reutilizado por pagos registrados; cobros agrupados según regla existente jugador/período/concepto; mismo límite `top` |
| Becados y descuentos / `becados` | Sin filtros adicionales | `becados/pdf`, `becados/csv` | `DiscountDao.FromJoins` y `GetAllDiscounts`; incluye historial de beneficios y sus estados según exportador existente; orden estado y fecha de inicio descendente |
| Actualización de aranceles / `aranceles` | Sin filtros adicionales | `aranceles/pdf`, `aranceles/csv` | `ArancelesDao.ArancelesSeleccion` y `ObtenerHistorial`; aranceles históricos, categorías y responsable de auditoría; conserva orden existente del Service |
| Recaudación mensual / `recaudacion` | `anio`, `mes`, `idCategoria` opcional | `recaudacion/pdf`, `recaudacion/csv` | `RecaudacionDao.PagosDelPeriodo`; pagos cobrados, `fecha_pago >= @desde AND fecha_pago < @hasta`, categoría opcional; detalle por fecha descendente con desempate existente |

No hay exportador Excel en este alcance. El número de filas de recaudación refiere al detalle de cobros, no a los resúmenes/totales decorativos del documento. El de deudores refiere a jugadores agrupados, no a cada cuota.

## SQL utilizado por el conteo

`DaoLibrary/ReportesDao.cs` compone consultas parametrizadas a partir de los mismos fragmentos de selección que los DAO de exportación:

```sql
-- deudores
SELECT COUNT(*) FROM (<PagosDao.DeudoresSeleccion>) filas;
-- pagos-recientes, después de PagosRegistradosCtes
SELECT CASE WHEN COUNT(*) > @top THEN @top ELSE COUNT(*) END
FROM Registrados WHERE cumple_filtros = 1;
-- becados
SELECT COUNT(*) <DiscountDao.FromJoins>;
-- aranceles
SELECT COUNT(*) <ArancelesDao.ArancelesSeleccion>;
-- recaudacion
SELECT COUNT(*) <RecaudacionDao.PagosDelPeriodo>;
```

Los elementos entre `<...>` son referencias a fragmentos de código, no SQL literal ejecutable. Parámetros categoría/top/período se enlazan como parámetros SQL; las fechas mensuales usan intervalo cerrado-abierto. No hay SELECT de todo el reporte para calcular su cantidad; no se invocan los builders PDF/CSV ni `Registrar` desde el conteo. La igualdad efectiva entre COUNT y SELECT sobre dataset SQL estable pasó para los cinco tipos.

## Historial y criterio de éxito

La auditoría general `AUDITORIA_CAMBIOS` se utiliza para otras acciones y responsables. Se agregó una tabla específica porque el historial requiere semántica propia y paginación legible.

Migración: `database/migrations/V20261031_01__historial_exportaciones_reportes.sql`.

Modelo `ExportacionReporte`, tabla `dbo.EXPORTACIONES_REPORTES`:

| Campo | Origen |
|---|---|
| `id` | IDENTITY en SQL |
| `fecha_hora` | `RelojNegocio.Ahora`, reloj del club en backend |
| `usuario_id` | Claim autenticado `idUsuario`; no body/query enviado libremente |
| `persona` | Resolución SQL desde `USUARIOS` y `PERSONAS` para ese id |
| `tipo` | Código estable de los cinco reportes |
| `formato` | `pdf` / `csv` (CHECK SQL) |
| `filtros` | Descripción legible de categoría/período/límite o sin filtros adicionales |
| `cantidad_filas` | Colección real usada por el exportador, nunca cantidad del preview; CHECK >= 0 |

**No existen columnas para archivo, bytes, base64, PDF/CSV, contenido ni filas exportadas.** El nombre de la persona es el del actor, no el listado de personas del reporte.

El Controller consulta datos nuevamente, termina de generar bytes con el exportador actual, registra metadatos y sólo después devuelve `File(...)`. Si la consulta, generación o registro falla, no responde éxito con el archivo. No hay almacenamiento del archivo dentro de transacción SQL. El servidor confirma generación y persistencia de metadatos; no puede confirmar que el navegador guardó físicamente el archivo. Un fallo posterior de red/guardado local no revierte el historial.

Historial: `GET /api/reportes/historial?pagina=1&tamanioPagina=20`, contrato `{ items, total, pagina, tamanioPagina }`. Valida página 1..1000000 y tamaño 1..100. SQL `ORDER BY fecha_hora DESC, id DESC OFFSET ... FETCH NEXT ...`; índice acorde. La UI muestra fecha/hora del club sin reconvertirla según zona del navegador.

## Autorización y regresión de permisos

Roles actuales: `1` SuperAdmin, `2` Administrador. Backend mantiene autenticación global y permisos de exportación previos:

| Operación | SuperAdmin | Administrador | Anónimo |
|---|---|---|---|
| Exportar deudores/pagos-recientes/becados | Permitido | Permitido | 401 |
| Exportar aranceles/recaudación | Permitido | 403 | 401 |
| Conteo deudores/pagos-recientes/becados | Permitido | Permitido | 401 |
| Conteo aranceles/recaudación | Permitido | 403 en acción | 401 |
| Consultar historial directo | Permitido | 403 | 401 |

`ReportesAuthorization.Tests.cs` levanta Kestrel local con descubrimiento de rutas MVC del Controller de producción y middleware reales de autenticación/autorización. Sólo la identidad se simula. Un terminal sintético responde 204 **después de autorizar y antes de ejecutar acción/SQL**. Por ello prueba efectivamente HTTP 401/403 y aceptación del permiso, pero **no** prueba respuesta 200/cuerpo de historial, validación JWT real, ejecución del DAO ni descargas. La restricción por tipo dentro de la acción `Conteo` debe probarse además como prueba de Controller.

## Pruebas y fixtures

| Comprobación | Datos / esperado | Evidencia y alcance |
|---|---|---|
| Estado inicial/cargando | No emitir exportación | Test unitario componente `ReportExportMenu` |
| Varias filas | Respuesta simulada 5 habilita exportación | Test unitario componente; no dataset SQL |
| Una fila | Respuesta simulada 1 habilita PDF | Test unitario componente |
| Sin filas | Respuesta simulada 0 bloquea exportación | Test unitario componente; plantilla muestra advertencia comprensible |
| Cambio filtro | Categoría 8 invalida inmediatamente, anterior 99 no reemplaza actual | Test unitario con Subjects y timers simulados |
| Error de conteo | Bloquea exportar, muestra estado error | Test unitario componente |
| Límite pagos | Conteo solicita `top:500` | Test unitario componente |
| Historial Administrador | No consulta service | Test unitario `Reportes` |
| Historial paginado/hora club | Página 2, tamaño 20, total simulado 41; `2026-10-31T23:58:07` → `31/10/2026 23:58:07` | Test unitario `Reportes`; timestamp fixture, no persistido |
| Exportaciones CSV/PDF con datos | Builders y Controller existentes | Suite backend reportada debajo; no descarga navegador |
| Coincidencia COUNT/SELECT, cinco tipos | Misma selección SQL que exportación | PASS integración SQL, valores en tabla siguiente |
| Fixtures 0/1/N reales | Recaudación junio2098: 0 → insertar1 → 1 → insertar2 → 3; julio2098 y categoría inexistente:0 | PASS SQL; rollback |
| Conteo no crea historial | Total historial antes/después fixture permanece igual | PASS SQL |
| Historial persistido, persona y orden real | INSERT + SELECT paginado, 2 formatos/fecha igual2099; páginas1/2 tamaño1; id descendente | PASS SQL; nombre viene de usuario DB aunque objeto diga `suplantado`; rollback |
| Schema sin contenido | Nombres exactos ocho columnas | PASS SQL `sys.columns` |
| PDF/CSV generados desde Controller | 12 archivos reales con datos SQL + 12 registros metadatos en transacción | PASS integración; archivos locales en `qa/HU-108/evidencia` |
| Descarga en navegador | Descarga física + sesión con ambos roles | BLOCKED: no se ejecutó navegador |

Resultados frontend iniciales: `vitest run src/app/shared/export-menu/report-export-menu.spec.ts src/app/admin-portal/reportes/reportes.spec.ts`: **4 PASS, 2 archivos**, lógica de componentes. Verificación de tipos `tsc --noEmit` para app y specs: **PASS**, informado por agente frontend.

Resultado frontend FINAL ampliado: Angular `ng test --watch=false` con inclusión de cuatro specs (Reportes, DeudasMorosidad, RecaudacionMensual y report-export-menu): **55 PASS, 0 FAIL, 4 archivos**. Se compilaron las plantillas reales dentro del runner de pruebas y se verificó DOM. No se ejecutó un build independiente ni un navegador manual contra servidores de la aplicación.

Comando exacto desde `frontend-cacc`:

```powershell
./node_modules/.bin/ng.cmd test --watch=false --include='**/report-export-menu.spec.ts' --include='**/reportes.spec.ts' --include='**/deudas-morosidad.spec.ts' --include='**/recaudacion-mensual.spec.ts'
```

Evidencia frontend: `qa/HU-108/resultados/frontend.txt`. Checks posteriores `tsc --noEmit -p tsconfig.app.json`, `tsc --noEmit -p tsconfig.spec.json` y `git diff --check`: exit0; advertencias de normalización LF/CRLF, sin errores.

El DOM del wrapper verificó carga bloqueada → texto `0 registros` y explicación de filtros con botón deshabilitado → cambio de filtro → `1 registro` y botón habilitado. El DOM de Reportes verificó Administrador con tres tarjetas y estadísticas presentes, sin consultar aranceles/historial; SuperAdmin con cuatro tarjetas e historial con persona, formato CSV, nombre amigable y hora club. Los55 incluyen regresión de DeudasMorosidad y RecaudacionMensual por la incorporación del wrapper. No sumar los4 iniciales a los55 finales como casos distintos.

Autorización HTTP: `dotnet test ApiGestion.Tests --filter FullyQualifiedName~ReportesAuthorizationTests --no-restore --verbosity quiet`: **14 PASS, 0 FAIL, 0 omitidos**, 29 segundos. Incluye historial rol 1 permitido/rol 2 prohibido/anónimo 401, conteo autenticado y conservación de permisos de exportación.

Suite HU-108 backend/HTTP/SQL inicial: **46 PASS, 0 FAIL, 0 omitidos**, evidencia `qa/HU-108/resultados/HU108.trx` (contadores XML verificados). Incluye los 14 casos HTTP anteriores; no sumarlos de nuevo. Se agregó posteriormente un caso de fallo de generación con builder CSV real: el grupo HU-108 final tiene 47 casos.

Suite backend completa FINAL: **722 PASS, 11 omitidos, 0 FAIL; 733 total**, evidencia `qa/HU-108/resultados/backend-completo.trx`, contadores XML verificados. Incluye 47 casos HU-108 y regresión existente; los 11 omitidos siguen siendo pruebas no ejecutadas, no PASS.

### Exportaciones reales verificadas

| Reporte | Conteo obtenido y esperado | Filas reales registradas | Archivos generados |
|---|---:|---:|---|
| Deudores | 34 | 34 | `deudores-2026-9.pdf`, `.csv` |
| Pagos recientes | 60 | 60 | `pagos-recientes-2026-9.pdf`, `.csv` |
| Becados | 0 | 0 | `becados-2026-9.pdf`, `.csv` |
| Aranceles | 1 | 1 | `aranceles-2026-9.pdf`, `.csv` |
| Recaudación septiembre2026 | 126 | 126 | `recaudacion-2026-9.pdf`, `.csv` |
| Recaudación julio2098 | 0 | 0 | `recaudacion-2098-7.pdf`, `.csv` |

Los doce archivos están en `qa/HU-108/evidencia`. Sufijo 2026-9 de los reportes sin filtro mensual identifica la ejecución de evidencia, no un filtro inexistente. Actor fixture: usuario autenticado id1, persona resuelta desde la base (sin reproducir datos personales aquí). Fechas reales de esta ejecución: `2026-10-31T18:55:38`, fracciones entre `.17` y `.88`, hora club. Cada formato creó exactamente un registro (12 en total), con cantidad igual al preview en dataset estable; finalmente rollback. Los archivos con 0 filas se generaron desde Controller para probar robustez del exportador existente; la UI impide iniciarlos cuando el preview es 0. No se afirma que estos archivos hayan sido descargados por navegador.

## Criterios de aceptación

`PASS código` indica inspección de implementación; `PASS test` identifica evidencia ejecutada. Ninguna de las dos sustituye los casos integrales pendientes.

| Criterio | Resultado / evidencia |
|---|---|
| Cada reporte tiene cantidad antes de exportar | PASS código: cinco tipos, wrapper en cajas y exportaciones relacionadas |
| Conteo en backend | PASS código + pruebas de Controller |
| Mismos filtros/selección de exportación | PASS integración SQL de los cinco tipos y paridad preview/filas exportadas |
| Conteo no genera archivo | PASS test Controller sin llamada exportador; DAO sólo SELECT COUNT |
| Conteo no guarda archivo | PASS código, sin API de archivo/persistencia en `Contar` |
| Conteo no registra exportación | PASS test Controller con colección historial vacía |
| 0 filas advierte claramente | PASS test DOM `0 registros`, advertencia y botón deshabilitado; browser manual pendiente |
| Reportes con filas continúan exportando | PASS Controller/builders/DAO reales y archivos evidencia; descarga navegador pendiente |
| Exportación exitosa registra historial | PASS integración 12 exportaciones con 12 registros reales (rollback) |
| Historial fecha/hora | PASS rango reloj backend, persistencia SQL y formato UI |
| Historial persona | PASS integración SQL actor id 1 + intento `suplantado` ignorado |
| Historial reporte | PASS test código estable + etiquetas UI |
| Historial filtros | PASS test categoría/período y código sin filtros adicionales |
| Historial cantidad real | PASS test preview999 vs colección real1 + integración cinco reportes |
| Historial formato | PASS test CSV y PDF de recaudación + restricciones SQL |
| Historial no contiene archivo | PASS esquema SQL exacto ocho columnas/modelo |
| Historial no contiene filas exportadas | PASS esquema SQL exacto ocho columnas/modelo |
| SuperAdmin puede consultar historial | PASS HTTP autorización 204 terminal + DAO SQL real; navegador pendiente |
| Administrador no consulta API historial | PASS HTTP directo 403 |
| Permisos exportación conservados | PASS HTTP roles existentes, separado del historial |
| Tests verdes | Frontend final 55 PASS / 0 FAIL, 4 archivos; backend completo 722 PASS / 11 omitidos / 0 FAIL, incluye 47 HU-108 y 14 HTTP |
| QA con y sin filas | PASS fixtures 0/1/5 frontend, 0/1/57 Controller, 0/1/3 SQL y archivos 0/1/34/60/126 |
| Cambiar filtros invalida conteo | PASS test inmediato y descarte respuesta vieja 99 |
| Usuario autenticado, no payload | PASS código + test actor 7 y rechazo sin identidad |
| Fallo consulta/generación/historial no responde archivo | PASS tests consulta fallida, builder CSV real con entrada inválida y fallo persistencia; no historial exitoso |
| Historial orden estable y paginado | PASS SQL fecha/id descendentes con empate/páginas 1 y 2; test UI página 2/tamaño 20 |

Pendiente en entorno integral: navegador con ambos roles, revisión visual de estados/conteos y descarga física. Las pruebas no inducen un fallo real del motor PDF ni desconexión SQL de mitad de generación; sí prueban fallo de builder CSV real con entrada inválida, fallo de consulta, ausencia de actor y fallo al persistir historial, sin responder archivo exitoso. No declarar la validación manual de navegador como realizada.

## Bugs y regresión

- Detectada durante QA una presentación de códigos internos en historial: corregida con mapping a etiquetas amigables y verificación unitaria; revisado uso de `nombreReporte(item.tipo)` en plantilla.
- Corregido texto de cero filas para mostrar explícitamente `0 registros`, además de explicación y bloqueo; validado en DOM.
- Corregido acceso de Administrador a tarjeta/consulta de aranceles exclusiva de SuperAdmin: se oculta tarjeta y no se consulta resumen. Evita que un403 de aranceles rompa el `forkJoin` y deje sin estadísticas los otros reportes. Validado con DOM rol2: tres tarjetas y estadísticas visibles, sin consultas aranceles/historial.
- Dos casos iniciales del harness esperaban 403 por tipo de conteo pero ese control vive en la acción, que el terminal sintético no ejecuta. Se retiraron del harness y se cubren en Controller. No constituye bug de producción.
- Hubo contención de DLL por ejecución simultánea de pruebas backend; se coordinó ejecución serial y se repitió. No constituye resultado funcional.
- HU-107 y sus artefactos previos se preservaron. Recaudación conserva endpoints/exportadores y selección mensual; paridad de 126 cobros septiembre 2026 validada contra SQL.
