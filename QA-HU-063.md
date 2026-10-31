# QA — HU-063: los pagos se registran en la base compartida y se actualizan en el sistema deportivo

Evidencia de las pruebas de HU-063 en la rama `feat/HU-063-pruebas-pagos`, ejecutadas junto con Claude Code el
2026-10-28. Las pruebas corrieron contra una **copia descartable** de la base unificada de prueba
(`ClubCamioneros_HU063_Descartable`, restaurada desde un respaldo `COPY_ONLY` de `ClubCamionerosUnificadaV2_Prueba`).
La base de prueba original no se modificó. La copia quedó en el SQL Server local, sin uso.

Esta HU es solo de pruebas: **no se modificó código del sistema**. Lo único nuevo es el archivo de tests
`ApiGestion.Tests/PagosBaseCompartida.Integration.Tests.cs` y este informe.

---

## 1. Cómo se probó

- **Cobro real, de punta a punta.** Las pruebas llaman al mismo servicio que usa la API (`PagosService` con
  `PaymentTransactionDao`), que guarda con commit. El operador se fija igual que en un pedido HTTP
  (`SqlAuditActorContext`), así la auditoría registra el usuario como en producción.
- **Lectura como otro sistema.** Después de cada pago, todo se vuelve a leer con **conexiones nuevas**: la tabla
  `PAGOS`, `AUDITORIA_CAMBIOS`, el historial, Actividad y el estado de deuda. El estado de deuda se lee con
  `EstadoDeudaDao`, que es exactamente lo que responde `GET api/integracion/v1/estado-deuda/dni/{dni}`, el endpoint
  que consulta el Portal Deportivo. Ese endpoint no tiene caché: lee la base en cada pedido.
- **Datos propios.** Cada prueba crea su propio jugador (DNI y apellido al azar) con cuotas de 2025, que ya están
  vencidas cualquier día que se corra.
- **Protección.** Estas pruebas escriben datos, así que solo corren si se define la variable
  `CACC_TEST_DB_DESCARTABLE`, y además se niegan a correr si el nombre de la base no contiene "Descartable". Sin la
  variable quedan salteadas, igual que el resto de las pruebas de integración.

## 2. Resultado por caso

| Caso de la HU | Prueba | Resultado |
|---|---|---|
| **Pago completo** (HU-025, cobro desde la ficha) | `PagoCompleto_ElCobroDejaLaCuotaAbonadaConMetodoFechaYUsuario` | ✅ La cuota queda `estado = 1`, con método `Transferencia` (guardado con su nombre canónico aunque llegue en minúsculas), `fecha_pago` = hoy, `fecha_hora_registro` y `FK_id_usuario_registro` = operador. La otra cuota no se toca. |
| **Pago completo** (HU-015 / HU-016, registro por el total) | `PagoCompleto_ElRegistroPorElTotalDejaElAbonoYCierraLaCuota` | ✅ Queda el abono en `Efectivo` con fecha, hora y operador, y la fila pendiente se borra. |
| **Varias cuotas** | `VariasCuotas_TodasQuedanAbonadasEnLaMismaOperacion` | ✅ Las 3 quedan abonadas, con el mismo método, operador y la **misma** hora de registro. |
| **Varias cuotas, falla a mitad** | `VariasCuotas_SiLaOperacionFallaNoQuedaNingunaAMedias` | ✅ La 1.ª cuota ya se había ajustado con su beca cuando la 3.ª resultó cubierta al 100 % y el cobro se rechazó. No queda nada a medias: ninguna abonada, montos originales, sin beneficio aplicado y **sin eventos nuevos en la auditoría**. |
| **Varias cuotas, una ya cobrada por otro** | `VariasCuotas_SiUnaYaFueCobradaPorOtroSeRechazaTodoElLote` | ✅ Se rechaza todo el lote ("ya fueron abonadas"). Las otras siguen pendientes y la ya cobrada no cambia. |
| **Con beneficio** | `ConBeneficio_SeCobraElMontoAjustadoYLosDosSistemasVenElMismoSaldo` | ✅ Con una beca del 50 % se cobran $10.000 de una cuota de $20.000 y la fila guarda el beneficio aplicado. El saldo del estado de cuenta (Administrativo) y el del estado de deuda (Deportivo) son **iguales antes y después** del pago. |
| **Historial** | `Historial_ElPagoApareceEnElHistorialDelJugadorYEnActividad` | ✅ El pago aparece en el historial del jugador (HU-017) como "Junio 2025", con método y responsable, y en Actividad y Movimientos como "Pagado". |
| **Lectura del otro sistema** | `LecturaDelDeportivo_VeElEstadoNuevoDespuesDeCadaPago` | ✅ El Deportivo ve el estado nuevo después de cada pago, sin datos viejos: Inhabilitado (2 vencidas) → Solo entrenamientos (1) → Habilitado (0), con la deuda correcta en cada paso. |
| **Sin escritura cruzada** | `ElDeportivoNoPuedeRegistrarModificarNiAnularPagos` y `CadaEscrituraDePagosEsSoloDelAdministrativo` | ✅ Ningún rol del Deportivo puede registrar, cobrar ni anular pagos. Se revisan **todos** los endpoints de escritura de la API. Ver la observación 1. |
| **Auditoría** | `Auditoria_ElCobroQuedaRegistradoConSuUsuario` y `Auditoria_ElRegistroDejaElAltaDelAbonoYElCierreDeLaCuotaConSuUsuario` | ✅ El cobro queda en `AUDITORIA_CAMBIOS` con el `id_usuario` y el email del operador, y con el método y el estado en los datos nuevos. En el registro por el total quedan con el mismo usuario el alta del abono (INSERT) y el cierre de la cuota (DELETE). |

**Errores encontrados: ninguno.** No hay Bugs para cargar en Jira de la parte del Administrativo.

## 3. Pruebas automáticas

| Suite | Resultado |
|---|---|
| HU-063 (14 pruebas nuevas), corridas 3 veces seguidas | **14/14** cada vez |
| Backend completo contra la copia descartable | **629/629**. Compilado fuera de la carpeta del repo, `UsuarioApiPermisos` falla solo porque busca un archivo del repo; compilado dentro del repo, pasa. |
| Backend completo sin base (como en la compu de cualquiera) | **541** correctas y **83** salteadas, que son las de integración |

Para correrlas (con una copia descartable de la base):

```powershell
$env:CACC_TEST_DB = "Server=localhost;Database=<copia>_Descartable;Trusted_Connection=True;TrustServerCertificate=True;"
$env:CACC_TEST_DB_DESCARTABLE = $env:CACC_TEST_DB
dotnet test ApiGestion.Tests --filter "FullyQualifiedName~PagosBaseCompartida|FullyQualifiedName~PagosSinEscrituraCruzada"
```

Nota del entorno: el `global.json` del repo pide el SDK de .NET 10, y en la compu donde se probó está el 9. Las
pruebas se corrieron con el SDK 9 (los proyectos son `net9.0`), sin cambiar el `global.json`, porque ese tema lo
está viendo el equipo.

## 4. Observaciones (no son errores)

1. **El Deportivo puede generar cargos pendientes, pero no pagos.** La única escritura que la API le permite a un rol
   del Deportivo es `POST api/integracion/v1/jugadores/{id}/cargos-alta`, y solo con el rol "Administrador
   (Tesorero)". Cuando el Deportivo da de alta un jugador, ese endpoint genera su inscripción y su cuota del mes
   **pendientes**. No cobra ni modifica pagos existentes, y es parte del contrato de integración v1. El test deja
   esta excepción escrita: si alguien abre otra escritura al Deportivo, el test falla.
   `POST api/estado-deuda/lote` también admite al Deportivo, pero es una consulta.
2. **Permisos en la base, del lado del Deportivo.** Lo anterior cubre la API. Si el Portal Deportivo se conecta
   directo a la base compartida con su propio usuario de SQL, sus permisos sobre `PAGOS` no se pudieron revisar
   desde acá. Se verifican con el otro equipo (ver la guía, paso 4).
3. **El rol "Administrativo" del Deportivo** (`ROLES.PK_id_rol = 2`) no figura en `RolesDelDeportivo` de la API. Hoy
   eso significa que no puede entrar a ningún endpoint, ni siquiera al estado de deuda. Se incluyó en la prueba de
   escritura cruzada para que quede cubierto.

## 5. Guía para el equipo del Portal Deportivo

Para cumplir la Definición de Listo, estos pasos se ejecutan **juntos**, sobre el entorno de prueba compartido
(HU-059). Las consultas son de solo lectura.

1. **Elegir un jugador con cuotas vencidas** y anotar su DNI. Antes del pago, consultar desde el Deportivo:
   `GET /api/integracion/v1/estado-deuda/dni/{dni}`. Anotar el estado y la deuda.
2. **Registrar el pago en el Administrativo** (Cuotas y Pagos o la ficha del jugador), en Efectivo o Transferencia.
3. **Volver a consultar desde el Deportivo** el mismo endpoint, sin reiniciar nada. Tiene que verse el estado nuevo
   (por ejemplo, de Inhabilitado a Solo entrenamientos) y la deuda menor. Si el Deportivo guarda algo en caché de su
   lado, este es el paso que lo muestra.
4. **Lectura directa de la base** (solo si el Deportivo lee las tablas):

   ```sql
   SELECT pg.PK_id_pago, pg.fecha_vencimiento, pg.estado, pg.monto_final, pg.metodo_pago,
          pg.fecha_pago, pg.fecha_hora_registro, pg.FK_id_usuario_registro
   FROM PAGOS pg
   JOIN JUGADORES j ON j.PK_id_jugador = pg.FK_id_jugador
   JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona
   WHERE p.Dni = '<dni>' ORDER BY pg.fecha_vencimiento, pg.PK_id_pago;
   ```

   Y con el **usuario de SQL que usa el Deportivo**, confirmar que no puede escribir en `PAGOS`:

   ```sql
   SELECT HAS_PERMS_BY_NAME('dbo.PAGOS', 'OBJECT', 'INSERT') AS puede_insertar,
          HAS_PERMS_BY_NAME('dbo.PAGOS', 'OBJECT', 'UPDATE') AS puede_modificar,
          HAS_PERMS_BY_NAME('dbo.PAGOS', 'OBJECT', 'DELETE') AS puede_borrar;
   ```

   Lo esperado es `0, 0, 0`. Si alguno da 1, se carga como Bug vinculado a HU-063.
5. **Intentar escribir desde el Deportivo:** con el token de un usuario del Deportivo, llamar a
   `POST /api/pagos/cobro`. Lo esperado es **403**.

Si algún paso no da lo esperado, se carga como **Bug** en Jira, vinculado a HU-063.

## 6. Pendiente para cerrar la HU

- Ejecutar la guía de la sección 5 con el otro equipo sobre el entorno compartido (HU-059) y anotar el resultado.
- Confirmar con ellos la observación 2: con qué usuario de SQL se conecta el Deportivo y qué permisos tiene.
