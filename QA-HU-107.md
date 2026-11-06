# QA HU-107: Recaudación Mensual con fecha de cada pago y paginación

**Rama:** `feat/HU-107-recaudacion-paginada`, que sale de `main` en `342bd86`.
**Fecha:** 31/10/2026.
**Revalidación actual:** **37 PASS / 0 FAIL** contra API real y SQL Server, ejecutados el 31/10/2026 a las 19:08:53 (Argentina). Evidencia: `qa/HU-107/revalidacion/api/evidencia.json`. El recorrido manual/en navegador de esta versión está **BLOCKED**: no había una instancia actual servida y no se ejecutó `ng serve` porque compila, prohibido por las instrucciones vigentes.

El documento recibido de Claude declaraba 50 PASS (38 API + 12 Chrome), 682 pruebas backend y 522 frontend, además de builds. Esos son **antecedentes de otra corrida**, no resultados nuevos: el script actual registra realmente 37 comprobaciones API. Las capturas y logs de `qa/HU-107/evidencia/` se conservan como antecedentes y no prueban el estado final actual. No se ejecutó build independiente en esta revisión.

## 1. Estado encontrado al retomar y antecedentes

Al retomar, el árbol de trabajo ya tenía implementadas la fecha del club, la paginación SQL, los cuatro órdenes, los totales independientes y las exportaciones completas. Esta revisión completó los bordes de orden por timestamp, desborde de paginación, fallback horario, estados de carga/error y legibilidad del total PDF; se detallan al final. Se preservaron los cambios de HU-108.

La tabla siguiente describe el sistema antes de la implementación original de HU-107, no los pendientes encontrados al retomar.

| Tema | Cómo estaba |
|---|---|
| Período | `RecaudacionDao` filtra `estado = 1` con `fecha_pago` en el rango `[1.º del mes, 1.º del mes siguiente)`. El rango es de HU-070 y permite usar índices. |
| Fecha real del pago | `PAGOS.fecha_pago` (DATE) es el día del club, igual a `RelojNegocio.Hoy`. `PAGOS.fecha_hora_registro` (DATETIME2) es el instante en que se grabó, en hora del club (`SqlReloj.Ahora`). Los pagos anteriores a esa columna no tienen hora (NULL). |
| Datos de la pantalla | `GET api/recaudacion` devolvía los totales y **todo** el detalle del mes en una sola respuesta. |
| Orden | El servidor ordenaba fijo (`fecha_pago DESC, PK_id_pago DESC`). El navegador no ordenaba. |
| Scroll | La tabla tenía `max-height: 340px` con scroll interno, sin paginador ni total real. |
| Fecha en la tabla, el PDF y el CSV | No aparecía. |
| Exportaciones | Las arma el servidor con el mes completo (`ReportesController` + `RecaudacionReportBuilder`). |
| HU-088 | `GET api/pagos/registrados?page&pageSize`: 10 por página, tope de 100, respuesta `{ items, total, pagina, tamanioPagina }`, paginador "← Anterior / Página X de Y / Siguiente →". No ordena por columnas. |

## 2. Diseño

- **Fecha del pago:** se muestra `fecha_hora_registro` y, si es NULL, solo `fecha_pago`. No se inventó ninguna fecha nueva. El período se sigue filtrando por `fecha_pago`, igual que en el resto del sistema.
- **Zona horaria:** hay una sola interpretación, y la conversión ocurre **en el backend**.
  - La base ya guarda la hora del club.
  - El DTO la envía como `DateTimeOffset` con el corrimiento que calcula `RelojNegocio.EnHoraDelClub`, a partir de `TimeZoneInfo` `America/Argentina/Buenos_Aires` (sin un "-3" fijo).
  - La pantalla muestra esos dígitos tal cual, sin pasarlos por `Date`, así que no depende de la zona del navegador.
- **Endpoints (opción B, cambio mínimo):**
  - `GET api/recaudacion?anio&mes&idCategoria`: solo los totales del período completo, para tarjetas, encabezado y donut. Ya no trae el detalle.
  - `GET api/recaudacion/pagos?anio&mes&idCategoria&page=1&pageSize=10&sortBy=fecha|monto&sortDirection=asc|desc`: la tabla.
  - Usa los mismos nombres que HU-088. La respuesta es `{ items, total, pagina, tamanioPagina, totalPaginas }`, y `total` sale de un `COUNT(*)`, no de `items.length`.
  - `GET api/reportes/recaudacion/pdf|csv`: sin cambios en la firma. Exportan todo el período.
- **Validación:**
  - Devuelven 400 los casos `page < 1`, `pageSize` fuera de 1..100, `sortBy` distinto de fecha o monto, `sortDirection` distinto de asc o desc, y un período inválido.
  - El texto de `sortBy` se traduce a un enum (`OrdenPagosDelPeriodo`). El `ORDER BY` sale de un `switch` cerrado y nunca del texto del cliente.
- **Consulta:** un único fragmento `PagosDelPeriodo` (FROM + JOIN + WHERE) que comparten tarjetas, conteo, página y exportación. Por construcción no pueden contar conjuntos distintos.

  ```sql
  SELECT COUNT(*) AS total <PagosDelPeriodo>;
  SELECT <columnas> <PagosDelPeriodo>
  ORDER BY COALESCE(p.fecha_hora_registro, CAST(p.fecha_pago AS datetime2)) DESC, p.PK_id_pago DESC   -- o p.monto_final, p.PK_id_pago
  OFFSET @saltar ROWS FETCH NEXT @tomar ROWS ONLY;
  -- PagosDelPeriodo: FROM PAGOS p JOIN JUGADORES j JOIN PERSONAS pe LEFT JOIN CATEGORIAS c
  --                  WHERE p.estado = 1 AND p.fecha_pago >= @desde AND p.fecha_pago < @hasta
  --                    AND (@idCategoria IS NULL OR j.FK_id_categoria = @idCategoria)
  ```

  El WHERE se aplica, después el ORDER BY y al final OFFSET/FETCH. El desempate es siempre `PK_id_pago`. Por fecha ordena por el timestamp real. Solo cuando es NULL utiliza fecha_pago a medianoche como respaldo. Esto también resuelve el caso excepcional en que día imputado y día del timestamp difieren. Cada uno de los cuatro órdenes se cotejó con IDs ordenados por SQL directo independiente del DAO.
- **Frontend:**
  - La tabla tiene su propio estado (`cargandoPagos`, `LatestRequest` propio).
  - Cambiar de página u orden pide solo `/pagos`: tarjetas y donut no se recalculan ni parpadean, y la página anterior queda atenuada mientras llega la nueva.
  - Cambiar de orden vuelve a la página 1. Cambiar de mes, año o categoría vuelve a la página 1, conserva el orden y recarga los totales.
  - Encabezados Fecha y Monto: son botones con `aria-sort` y `aria-label`.
  - Formato `dd/MM/yyyy HH:mm`, o `dd/MM/yyyy` en los pagos sin hora.
- **PDF:** columna Fecha (`dd/MM/yyyy HH:mm`) primera, anchos de columna reajustados y la zona aclarada en el encabezado.
- **CSV:** columna Fecha primera, en ISO `yyyy-MM-dd HH:mm`, que no depende de la configuración regional. Separador `;`, BOM UTF-8 y montos numéricos sin cambios. Se agregó la fila `Fechas;Hora del club (America/Argentina/Buenos_Aires)`.
- **Permisos:** sin cambios (`[Authorize(Roles = "1")]` + `superAdminGuard`).
- **Índices:** no se agregaron índices: lo medido no lo justifica (ver tiempos).

## 3. Entorno y fixtures

- Base de prueba **existente** `ClubCamionerosUnificadaV2_HU107_Prueba` en LocalDB; esta revisión usó `-SinPreparar`, sin recrearla ni recargar fixtures.
  - Sus fixtures previos se conservaron. `-SinPreparar` renovó únicamente las claves de las dos cuentas QA y limpió intentos de login de esta base de prueba. Las exportaciones agregaron metadatos reales al historial HU-108; no se borraron.
- Cuentas propias del QA: SuperAdmin `95900001` y Administrador `95900002`. La clave se genera al azar en cada corrida, va solo en memoria y en el entorno de los procesos hijos, y no se guarda.
- Fixtures (DNI `96xxxxxx`, 30 jugadores en 2 categorías):

| Mes | Cobros | Qué ejercita |
|---|---|---|
| Septiembre 2026 | **126** ($ 7.749.000) | Más de 100 pagos. Varios por día a distintas horas; dos con la misma hora exacta (desempate por id); 6 sin hora; montos $9.000/$20.000/$100.000, que como texto se ordenan mal; 30/09 23:59:30 en el borde. Además, 30 cuotas pendientes que no deben contar. |
| Octubre 2026 | **7** ($ 525.000) | Mes con pocos pagos; 01/10 00:01 en el borde. |
| Agosto 2026 | **0** | Mes vacío; el 31/07 23:59 debe quedar en julio. |
| Julio 2026 | **5.001** | Volumen para medir tiempos. |

**Comando ejecutado:** `pwsh -NoProfile -File qa/HU-107/qa_hu107.ps1 -SinPreparar -Evidencia qa/HU-107/revalidacion/api`. Requiere API ya compilada en `bin/Debug`, SQL Server y herramientas PDF. Se omitió expresamente `-Navegador`: inicia `ng serve` y compila. Salida 0, 37 PASS / 0 FAIL. No se imprimieron ni persistieron claves o tokens.

## 4. Resultados contra la API (lo esperado sale de SQL directo, no del DAO probado)

| Id | Caso | Resultado |
|---|---|---|
| PAG-1 | Primera página: 10 filas, total 126, 13 páginas | PASS |
| PAG-2 | Página intermedia (7) | PASS |
| PAG-3 | Última página: 6 filas | PASS |
| PAG-4 | Después de la última página: 0 filas y total 126 | PASS |
| PAG-5 ×4 | Recorrer todas las páginas en los 4 órdenes: 126 filas, 126 distintas, mismos ids que SQL | PASS |
| ORD ×4 | fecha asc/desc y monto asc/desc sobre todo el período, cruzando páginas, con desempate por id | PASS |
| ORD-NUM | Monto numérico: 9000 < 20000 < … < 100000 | PASS |
| ORD-DIA | 01/09: 08:37, 09:07, 09:07, 12:07, 13:37; la misma hora exacta se ordena por id (6, 8) | PASS |
| FEC-1 | `fechaHoraRegistro` = `PAGOS.fecha_hora_registro`, con `-03:00` | PASS |
| FEC-2 | 6 pagos sin hora: `null`, solo el día | PASS |
| FEC-3 | 30/09 23:59:30 está en septiembre y es el primero por fecha desc | PASS |
| TOT-1 | Tarjetas = SQL: $7.749.000 / efectivo $5.460.000 (84) / transferencia $2.289.000 (42) | PASS |
| TOT-2 | Pedir otras páginas u otro orden no cambia los totales (respuesta idéntica) | PASS |
| TOT-3 | Los totales ya no traen el detalle (124 bytes) | PASS |
| TOT-4 | La suma de todas las páginas da el total; la página 1 sola ($536.000) no | PASS |
| CAT-1 | Con categoría: tabla 63 = tarjetas 63 = SQL 63 | PASS |
| CHI-1/2 | Octubre: 7 pagos, 1 página, tarjetas = SQL; 01/10 00:01 incluido | PASS |
| VAC-1 | Agosto: 0 pagos, 1 página, tarjetas en 0 | PASS |
| VAL-1 | Devuelven 400: `page=0`, `page=-3`, `pageSize=0`, `pageSize=101`, `sortBy=PK_id_pago`, `sortBy=fecha_pago; DROP TABLE PAGOS--`, `sortDirection=arriba`, `sortDirection=desc, PK_id_pago`, `mes=13`, `anio=1999`. PAGOS queda intacta. | PASS |
| VAL-2 | `MONTO`/`ASC` sin distinguir mayúsculas | PASS |
| VAL-3 | `sortBy=` vacío equivale a no enviado: 200 con el orden por defecto (ver OBS-107-2) | PASS |
| AUT-1 | Administrador: 403 en tabla, tarjetas y CSV; sin sesión: 401 | PASS |
| CSV-1/2/3 | BOM, `;`, columna Fecha primera, zona indicada; 126 filas en ISO cuya suma da el total; mismo orden que la tabla | PASS |
| PDF-1/2 | 126 filas con fecha `dd/MM/yyyy`; zona y "Total recaudado (126 cobros)" (texto extraído con `pdftotext`) | PASS |
| VOL-1 | Julio: 5.001 cobros, 501 páginas, la última con 1 | PASS |
| PERF-1/2 | Tiempos (abajo); 2.077 bytes por página | PASS |

### Tiempos (HTTP local, 10 pedidos por caso)

| Caso | Mediana | Máximo |
|---|---|---|
| Página 1, septiembre (126 cobros) | 4,1 ms | 5,7 ms |
| Página 1 por fecha, julio (5.001) | 25,8 ms | 31,5 ms |
| Página 250 por monto, julio | 25,8 ms | 27,2 ms |
| Totales, julio | 17,5 ms | 20,6 ms |
| CSV completo, julio | 49,9 ms | 54,3 ms |

## 5. Antecedentes del navegador (corrida anterior, NO reejecutada)

La tabla siguiente conserva lo informado previamente por Claude. Todos los NAV están **BLOCKED para revalidación actual**; no se suman como PASS nuevos. La suite de componentes actual se informa por separado.

| Id | Caso | Resultado |
|---|---|---|
| NAV-1 | Septiembre: 10 filas, Fecha como primera columna en `dd/MM/yyyy HH:mm`, "126 pagos en Septiembre 2026 · Página 1 de 13" | PASS |
| NAV-1b | El donut, terminada la animación, muestra 126, igual que el encabezado | PASS |
| NAV-2 | Siguiente: página 2 con otras filas; tarjetas, encabezado, leyenda y arco del donut idénticos; la **única** petición fue `/pagos?page=2` | PASS |
| NAV-3 | Recorrer las 13 páginas: 126 filas, 126 distintas; "Siguiente" se deshabilita en la última | PASS |
| NAV-4 | Clic en Fecha: ascendente, vuelve a la página 1 y empieza en 01/09 | PASS |
| NAV-5 | Clic en Monto: primero $100.000; otro clic: primero $9.000; `aria-sort` correcto | PASS |
| NAV-6 | Teclado: Enter sobre el encabezado ordena y el foco queda en el botón | PASS |
| NAV-7 | Navegador en Asia/Tokyo (+12 h), pantalla recargada: fechas idénticas a las del club ("30/09/2026 23:59") | PASS |
| NAV-8 | Exportar CSV y PDF desde la página 3: piden `anio`/`mes` sin `page`/`pageSize` | PASS |
| NAV-9 | Octubre: página 1, "7 pagos", sin paginador, tarjetas del mes nuevo | PASS |
| NAV-10 | Agosto: "0 pagos", estado vacío, sin tabla ni paginador, $0 | PASS |
| NAV-11/12 | Tablet 820 px y celular 390 px: sin scroll horizontal de página y con el paginador entero en pantalla | PASS |

Las capturas están en `qa/HU-107/evidencia/capturas/`.

## 6. Antecedentes de pruebas automáticas (corrida anterior)

Los conteos y builds de la tabla siguiente pertenecen al QA recibido. Los resultados actuales se detallan al final y reemplazan estos conteos para evaluar esta revisión.

| Suite | Resultado |
|---|---|
| Backend (`dotnet test`, `CACC_TEST_DB` = base de HU-107) | **682 ok, 0 fallos, 11 omitidos.** Los omitidos piden otras variables de entorno. |
| Frontend (`ng test`) | **522/522** |
| `dotnet build` | Correcto, 0 advertencias. |
| `ng build` | Correcto. Advierte que el bundle inicial pesa 1,21 MB contra un presupuesto de 1,00 MB; la advertencia ya existía antes de HU-107. |

**Pruebas nuevas:**
- `RecaudacionDao.Integration.Tests.cs` (6): páginas sin repetir ni perder filas en 4 órdenes, totales iguales a la tabla, orden dentro del día, orden numérico, fecha incluida, mes chico y mes vacío.
- `RecaudacionController.Tests.cs`: defaults, mapeo del orden, 400 con parámetros inválidos, totales sin detalle, JSON de la fecha con `-03:00`, `ORDER BY` de lista cerrada.
- Exportaciones: CSV con fecha ISO, zona y todas las filas.
- `recaudacion-mensual.spec.ts`: 14 casos nuevos de HU-107.

**Regresión:** contra la base de HU-106 (3.000 jugadores) fallan 2 pruebas ajenas a HU-107: el timeout de `PagosRegistrados_FiltraYPaginaEnLaBase` (HU-088) y `ArancelNuevoDeGenero_SoloAfecta…`. **Fallan igual en `main` limpio contra esa base**, así que dependen de ese volumen y no de este cambio. Con la base de HU-107 pasan.

## 7. Bugs

| Id | Descripción | Estado |
|---|---|---|
| BUG-107-1 | PDF: con la columna nueva, la hora y el chip "Transferencia" se partían en dos líneas (visto al renderizar el PDF). | Corregido (anchos de columna) |
| BUG-107-2 | Pantalla: sin el alto máximo de la tabla, el panel del desglose quedaba con un hueco blanco arriba del donut. | Corregido (contenido alineado arriba) |

Durante el armado del QA aparecieron varios errores **del script de QA**, no del sistema, y se corrigieron. Los listo para que se puedan auditar:
- DNI de las cuentas dentro del patrón de los jugadores sintéticos.
- `sqlcmd` cortaba `STRING_AGG` a 256 caracteres.
- `ConvertFrom-Json` pasaba las fechas a hora local.
- Variables pisadas por la insensibilidad a mayúsculas de PowerShell (`$clave`, `$Base`, `$Evidencia`).
- `-SinPreparar` sin `QUOTED_IDENTIFIER`.
- Salida de Node leída antes de que terminara de escribirse.

## 8. Observaciones

- **OBS-107-1:** si un cobro se registra justo a medianoche, `fecha_pago` (reloj de la API) y `fecha_hora_registro` (reloj de SQL Server) podrían caer en días distintos por diferencia de milisegundos. El cobro queda en el mes de `fecha_pago` y la tabla muestra la hora de registro. No se reprodujo; no se tocó porque implica cambiar cómo se registran los pagos, que la HU excluye.
- **OBS-107-2:** `sortBy=` o `sortDirection=` vacíos se tratan como no enviados (orden por defecto). Así los entrega el binder de ASP.NET, igual que `page`/`pageSize` en HU-088. El valor nunca llega al SQL.
- **OBS-107-3:** en el PDF el porcentaje de Efectivo/Transferencia es **por monto**, y en la pantalla es **por cantidad de pagos**. Ya era así antes de HU-107 (está documentado en el código) y no se cambió.
- **OBS-107-4:** el CSV exporta en el orden por defecto (fecha desc), no en el orden elegido en la pantalla. Es el criterio que ya existía y la HU lo permite.

## 9. Criterios de aceptación automatizados

Las referencias API (PAG/ORD/FEC/TOT/CSV/PDF) corresponden a la nueva evidencia. Las referencias NAV son antecedentes y no sirven por sí solas para marcar PASS actual. Formato, interacciones y gráficos requieren la suite frontend actual; queda pendiente su observación en navegador real.

| Criterio | Resultado |
|---|---|
| Cada pago muestra su fecha real | PASS (FEC-1/2, NAV-1) |
| Formato visible `dd/MM/yyyy` | PASS (suite Angular actual y fecha API); visual navegador BLOCKED |
| La hora usa la zona configurada del club | PASS (FEC-1, NAV-7) |
| Orden por fecha en el backend | PASS (ORD-fecha, ORD-DIA, NAV-4) |
| Orden por monto en el backend | PASS (ORD-monto, ORD-NUM, NAV-5) |
| Paginación en el servidor | PASS (PAG-1..5, NAV-2, PERF-2) |
| Default coherente con HU-088 (10 por página, tope 100) | PASS |
| `totalItems` de todo el período | PASS (PAG-1/4, NAV-1) |
| No se pierden filas entre páginas | PASS (PAG-5, NAV-3) |
| No se duplican filas entre páginas | PASS (PAG-5, NAV-3) |
| Orden estable | PASS (desempate por id, ORD-DIA) |
| PDF incluye la fecha | PASS (PDF-1) |
| CSV incluye la fecha | PASS (CSV-1/2) |
| PDF exporta el período completo | PASS (PDF-1, NAV-8) |
| CSV exporta el período completo | PASS (CSV-2, NAV-8) |
| Tarjetas del período completo | PASS (TOT-1/2/4) |
| Donut del período completo | PASS automático (métricas del período y tests componente); visual navegador BLOCKED |
| Cambiar de página no cambia las métricas | PASS (TOT-2 y suite frontend actual) |
| Mes vacío | PASS (VAC-1, NAV-10) |
| Mes con pocos pagos | PASS (CHI-1, NAV-9) |
| Mes con más de 100 pagos | PASS (septiembre: 126; julio: 5.001) |
| Tests en verde | PASS actual: backend 726 aprobadas / 11 omitidas / 0 fallos; frontend 531/531 |
| QA documentado | PASS (este documento) |


## 10. Correcciones y validación de esta revisión

- **BUG-107-3 (corregido):** el ORDER BY anterior priorizaba fecha_pago antes del timestamp. Un pago registrado al día siguiente podía quedar fuera de orden cronológico. Ahora ordena por `COALESCE(fecha_hora_registro, CAST(fecha_pago AS datetime2))` y PK estable. La prueba de integración específica crea el cruce de días dentro de una transacción y revierte sus fixtures.
- **BUG-107-4 (corregido):** se retiró el fallback temporal de offset fijo. La zona se resuelve por IANA o Windows; si no existe, falla explícitamente, sin inventar UTC-3.
- **BUG-107-6 (corregido):** al cambiar de período se mostraban temporalmente filas y conteo anteriores bajo el filtro nuevo. Ahora se descartan mientras se carga el nuevo conjunto.
- **BUG-107-7 (corregido):** un fallo de consulta podía anunciar 0 pagos. El estado ahora informa error y permite reintentar solo la tabla.
- **BUG-107-8 (corregido):** la inspección del PDF actual encontró que el importe total final se partía en tres líneas. Se amplió el ancho del total a dos columnas; el PDF final muestra `$ 7.749.000` completo en una línea y conserva las 126 filas. Se agregaron dos pruebas de render SVG (montos 7.749.000 y 999.999.999; rojo/verde). Evidencia anterior en `revalidacion/pdf-antes/`; render final en `revalidacion/api/pdf-pagina-6.png`.
- **BUG-107-5 (corregido):** páginas cuyo desplazamiento desborda el rango permitido se rechazan con 400 antes de consultar SQL.
- El script QA ahora verifica timestamp/fallback como criterio primario y compara la secuencia completa de IDs con cuatro ORDER BY SQL independientes.
- Evidencia actual PDF: `qa/HU-107/revalidacion/api/reporte-recaudacion-septiembre.pdf`, 6 páginas, 136.935 bytes, 126 filas con fecha. Texto y CSV completos en la misma carpeta; render `pdf-pagina-1.png` para inspección visual.
- No se alteraron pagos históricos ni la lógica de registro de pagos. No se crearon índices, commits ni builds independientes.

### Archivos del alcance HU-107

`DaoLibrary/RecaudacionDao.cs`, `DaoLibrary/IRecaudacionDao.cs`, `EntityLibrary/Recaudacion.cs`, `EntityLibrary/RelojNegocio.cs`, `ApiGestion/Controllers/RecaudacionController.cs`, `ApiGestion/Models/RecaudacionResponseDTO.cs`, `ApiGestion/Reports/RecaudacionReportBuilder.cs`, `ApiGestion/Reports/PdfReportBuilder.cs`, `frontend-cacc/src/app/models/CollectionPeriodModel.ts`, `frontend-cacc/src/app/services/recaudacion.ts`, los tres archivos de `frontend-cacc/src/app/admin-portal/recaudacion-mensual/`, `ApiGestion.Tests/RecaudacionController.Tests.cs`, `ApiGestion.Tests/RecaudacionDao.Integration.Tests.cs`, pruebas existentes de exportación y `qa/HU-107/qa_hu107.ps1`. El árbol tenía cambios HU-108 y otros cambios previos: esta lista no atribuye toda la diferencia de Git a esta revisión.

### Pruebas actuales verificadas

| Comprobación | Resultado | Evidencia |
|---|---|---|
| API real + SQL independiente, paginación/4 órdenes/totales/permisos/exportaciones | 37 PASS / 0 FAIL | `revalidacion/api/evidencia.json` |
| Backend focal HU-107 (con SQL real y rollback) | 39 PASS / 0 FAIL | `revalidacion/backend-hu107-verde.trx` |
| Backend completo | 726 PASS / 11 omitidas / 0 FAIL | `revalidacion/backend-completo.trx` |
| Frontend completo (`ng test --watch=false`) | 39 archivos / 531 pruebas PASS | `revalidacion/frontend.txt` |
| Regresiones frontend nuevas | Rojo: 2 fallos / 34 PASS; verde: 36 PASS HU-107 incluidos en suite completa | `revalidacion/frontend-red.txt`, `revalidacion/frontend-green.txt` |
| PDF visual (render Poppler de páginas 1 y 6, inspección actual) | PASS: fechas y medio legibles; total completo, sin superposición | `revalidacion/api/pdf-pagina-1.png`, `pdf-pagina-6.png` |
| Navegador real actual, responsive y descarga interactiva | BLOCKED: sin frontend actual servido; no se ejecuta ng serve/build | No hay evidencia nueva NAV |

Las dos regresiones DOM nuevas verifican que un cambio de filtro no muestre filas/conteos del período anterior mientras carga, y que un error no se anuncie como “0 pagos”: ofrece Reintentar y reconsulta solo la tabla. Tarjetas y métricas no se recargan al reintentar. JSDOM advierte que no implementa canvas; los tests verifican datos/estado, no el trazado visual del donut. Los 11 tests backend omitidos requieren configuraciones adicionales y no se cuentan como aprobados.


La suite backend final incluye las dos pruebas nuevas PDF: **737 totales, 726 aprobadas, 11 omitidas y ninguna fallida**. Un intento intermedio tuvo un fallo por colisión de DNI aleatorio del fixture ajeno `ActivePlayersPreviousMonthIntegrationTests`; se conserva `revalidacion/backend-colision-fixture.trx`. La repetición completa final pasó. Las pruebas del total PDF tienen evidencia rojo/verde propia en `revalidacion/pdf-total-rojo.trx` y `revalidacion/pdf-total-verde.trx`.


El constructor compartido PDF permite un ancho opcional para la celda de total mediante span, con **default 1** para conservar el comportamiento de los otros reportes. Recaudación solicita span 2. Las dos pruebas nuevas renderizan SVG y comprueban el importe completo en un solo elemento de texto para 7.749.000 y 999.999.999.
