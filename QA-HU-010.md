# QA — HU-010: Inmutabilidad del arancel histórico en cuotas adeudadas

Evidencia de las pruebas de integración (API) y de blindaje de código de
HU-010. Pruebas ejecutadas junto con Claude Code contra `ClubCamionerosPRUEBA`
(SQL Server local) el 2026-09-13, sobre la rama
`feature/HU-010-fee-historical-amounts`.

A diferencia de HU-014, esta HU no tiene colección Postman: las pruebas de
API se corrieron manualmente con `curl` (`http://localhost:5118`) y se
verificaron directamente contra la base con `sqlcmd`, comparando el estado
antes/después de cada llamada.

---

## Pruebas de integración (API — manual vía curl + sqlcmd)

| # | Caso | Llamada | Resultado esperado | Resultado obtenido | Estado |
|---|---|---|---|---|---|
| 1 | Historial completo | `GET /api/tariffs` | 200, array con las tarifas sembradas | 200 OK — `[{branch:"F",amount:58000,...},{branch:"M",amount:85000,...}]` | ✅ |
| 2 | Tarifa vigente por rama | `GET /api/tariffs/current` | 200, una entrada por rama con `validFrom <= hoy` | 200 OK — devuelve M ($85.000) y F ($58.000), ambas `isActive: true` | ✅ |
| 3 | Programación válida | `POST /api/tariffs` `{branch:"M", amount:95000, validFrom:"2026-11-01"}` | 200, cierra la tarifa abierta anterior de esa rama y crea la nueva | 200 OK — tarifa anterior quedó con `validTo` fijado, la nueva con `isActive:true` | ✅ |
| 4 | Rechazo — rama inválida | `POST /api/tariffs` `{branch:"X", ...}` | 400 | 400 — `"Branch must be either 'M' or 'F'."` | ✅ |
| 5 | Rechazo — monto ≤ 0 | `POST /api/tariffs` `{amount:-10, ...}` | 400 | 400 — `"Amount must be greater than zero."` | ✅ |
| 6 | Rechazo — fecha en el pasado | `POST /api/tariffs` `{validFrom:"2026-08-15", ...}` | 400 | 400 — `"A tariff cannot be scheduled to start in the past."` | ✅ |
| 7 (**T-1**) | Rechazo — solapamiento con la tarifa vigente de esa rama | Programar `validFrom` futura pero anterior/igual a la tarifa ya vigente de la misma rama | 400, mensaje explícito de solapamiento | 400 — `"The new tariff must start after the branch's current tariff (2026-11-01)."` | ✅ |
| 8 | Independencia entre ramas | Tras el caso 3 (cambio en M), consultar la tarifa F | La tarifa F no debe reflejar ningún cambio | F siguió en $58.000, `validFrom` sin modificar | ✅ |
| 9 (**T-3**) | Inmutabilidad de la cuota | Snapshot de `PAGOS.PK_id_pago = 1261` (`monto_final = 85000.00`) antes y después de programar un aumento en M | El monto persistido no debe cambiar | Sin cambios: `monto_final = 85000.00` antes y después | ✅ |

**Resultado: 9/9 casos verificados.**

### Nota sobre el caso 9 (T-3) — alcance real de la prueba

El proceso de **emisión de cuotas** (que resolvería la tarifa vigente y la
copiaría a `PAGOS` al generar una cuota nueva) no está implementado todavía
— quedó identificado como pendiente (`BE-4`) desde el diagnóstico inicial de
esta HU. Por lo tanto, la inmutabilidad hoy se prueba y garantiza a nivel
**estructural**: `TariffDao` no ejecuta ninguna sentencia sobre `PAGOS`
(verificado por lectura de código) y, en la prueba de arriba, un pago real
ya emitido no se vio afectado por un cambio de tarifa. La prueba end-to-end
completa ("emitir cuota → subir tarifa → la cuota vieja no cambia, la nueva
sí") solo podrá automatizarse una vez que `BE-4` exista.

### Nota sobre el caso 7 (T-1) — por qué no se probó con una fecha pasada

La tarifa vigente actual de cada rama arranca el 2026-09-01 (pasado respecto
a hoy, 2026-09-13). Cualquier fecha anterior o igual a esa cae también en el
pasado, así que dispara primero la validación genérica de "no fechas
pasadas" (caso 6) y nunca llega a la validación específica de solapamiento.
Para ejercitar el mensaje de solapamiento real, se programó primero una
tarifa futura válida (2026-11-01) y luego se intentó solapar otra sobre ese
rango (2026-10-15) — ver caso 7.

### Blindaje a nivel de base de datos (defensa en profundidad)

Además de la validación en `TariffDao.ScheduleTariff`, el índice único
filtrado `UX_TARIFAS_RAMA_VIGENTE` (`WHERE fecha_fin IS NULL`) impide a
nivel de motor que existan dos tarifas abiertas para la misma rama, incluso
ante un `INSERT` directo que se salte la capa de aplicación. Verificado en
la Fase 1 de esta HU con un `INSERT` de prueba (con `ROLLBACK`) que fue
rechazado con `Msg 2601: Cannot insert duplicate key row ... 'UX_TARIFAS_RAMA_VIGENTE'`.

---

## Blindaje de código — saldo adeudado sin cruce a tarifas (BE-5 / T-2)

Revisión de `DaoLibrary/PaymentDao.cs` (sin cambios respecto a `main`,
confirmado con `git diff`):

| Método | Verificación |
|---|---|
| `PaymentBaseQuery` | El único `JOIN` a `CATEGORIAS` trae `nombre_categoria` (texto), nunca un monto. No hay `JOIN` a `TARIFAS`. |
| `GetPendingFees()` | Lee `monto_final` directo de `PAGOS`. |
| `GetTreasuryMetrics()` | `SUM(monto_final)` directo sobre `PAGOS`, sin cruce a tarifas. |

Y en sentido inverso: `TariffDao.cs` no contiene ninguna sentencia SQL que
referencie `PAGOS`. Las dos tablas están desacopladas en ambas direcciones,
no solo en el esquema.

---

## Automatización pendiente (deuda técnica reconocida)

El proyecto no tiene ningún proyecto de tests (`.csproj` de xUnit/NUnit/
MSTest) en la solución `SistemaCamionerosBackend.slnx` — se decidió
explícitamente, junto con el equipo, no introducir uno en el marco de esta
HU y dejar los casos T-1 y T-3 cubiertos por la evidencia manual de arriba.
Si en el futuro se agrega una suite de integración, estos son los primeros
casos candidatos a automatizar.

---

## Criterios de aceptación — estado final

| Criterio | Estado |
|---|---|
| Inmutabilidad del arancel histórico (monto fijado al emitir) | ✅ Garantizado estructuralmente (ver nota del caso 9) — pendiente de prueba end-to-end cuando exista `BE-4` |
| Independencia ante aumentos (un aumento solo afecta períodos futuros) | ✅ Cumplido — `ScheduleTariff` nunca modifica cuotas, solo cierra/abre vigencias en `TARIFAS` |
| Saldo adeudado = sumatoria simple de `monto`, sin cruce a tarifas vigentes | ✅ Cumplido (`PaymentDao.cs`, sin cambios) |
| Independencia tarifaria por rama (Masculino/Femenino desacoplados) | ✅ Cumplido — sin FK entre `TARIFAS` y `CATEGORIAS`; verificado que un cambio en M no afecta F |
| Nomenclatura técnica en inglés (modelos, entidades, DAOs, variables) | ✅ Cumplido en `Tariff.cs`, `ITariffDao.cs`, `TariffDao.cs`, `TariffsController.cs`, `tariffs.ts`, `TariffModel.ts` |

## Bugs / ajustes de compatibilidad corregidos durante esta HU

| # | Hallazgo | Fix |
|---|---|---|
| 1 | `ALTER TABLE ... ADD rama` seguido de su uso en el mismo lote fallaba con `Invalid column name 'rama'` (SQL Server resuelve nombres de columna al compilar el lote completo) | Se separó la migración en lotes con `GO` entre cada paso — `database/V20260913_01__crear_tarifas_por_rama.sql` |
| 2 | `CREATE UNIQUE INDEX ... WHERE fecha_fin IS NULL` (índice filtrado) fallaba con `Msg 1934` por `QUOTED_IDENTIFIER` apagado | Se agregó `SET QUOTED_IDENTIFIER ON;` antes de crear el índice y antes de cualquier `INSERT` sobre `TARIFAS` — mismo fix aplicado en el seed |
| 3 | La cadena de conexión de `appsettings.Development.json` (`localhost\SQLEXPRESS`) no resuelve en esta máquina (la instancia real corriendo es la default, `localhost`) | No se tocó el archivo compartido en git — se sobrescribió localmente con `dotnet user-secrets set "ConnectionStrings:ConexionSQL" "Server=localhost;..."`, mismo mecanismo ya usado para `Jwt:Key` |
| 4 | Seed inicial de tarifas con `ValidFrom = 2026-09-24` (futuro respecto a la fecha del sistema, 2026-09-13) hacía que `/api/tariffs/current` devolviera vacío | Ajustado a `ValidFrom = 2026-09-01` en el script de seed y en la base real |

---

## Pendiente para cerrar la Definición de Terminado

- [x] Migraciones de base ejecutadas y verificadas contra `ClubCamionerosPRUEBA`.
- [x] Backend (`Tariff`, `ITariffDao`/`TariffDao`, `TariffsController`) compilando con 0 warnings/0 errores.
- [x] Frontend (`actualizacion-aranceles`) conectado al backend real, `ng build` en verde.
- [x] Blindaje de `PaymentDao` confirmado (sin JOIN a `TARIFAS`).
- [x] T-1 y T-3 verificados manualmente con evidencia documentada.
- [ ] `BE-4` — proceso de emisión mensual de cuotas (fuera del alcance original de HU-010, identificado en el diagnóstico inicial; sin esto, `PAGOS` sigue sin nuevas filas pendientes reales).
- [ ] Suite de tests automatizados (xUnit) — deuda técnica reconocida, no bloqueante para esta HU.
- [ ] Code review / aprobación del equipo y merge de `feature/HU-010-fee-historical-amounts` a `main`.
