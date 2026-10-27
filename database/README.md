# Base de datos

Los cambios de estructura se entregan como migraciones SQL versionadas que la API aplica
automáticamente al iniciar en `Development`, registrándolas en `dbo.__CaccMigraciones`.

**Guía completa (equipo e IA): [InstructivoEquipo.md](InstructivoEquipo.md).**

## Contenido de esta carpeta

| Ruta | Qué es | ¿Lo aplica la API? |
|---|---|---|
| `migrations/` | Migraciones automáticas. Todo cambio nuevo va acá. | Sí |
| `V2026*.sql` (raíz) | Scripts manuales históricos del equipo, hasta `V20260929_07`. Forman la base común que verifica `migrations/V20260930_01__verificar_base_equipo.sql`. | No |
| `esquema/ClubCamioneros_unificada_crear_base.sql` | **Base única** (Portal Administrativo + Portal Deportivo): estructura + catálogos, sin datos de personas. Es la base que usa el código de esta rama; su contrato (nombres de tablas, ids por secuencia, bajas, auditoría) está en el encabezado del script. | No |
| `esquema/ClubCamioneros_crear_base.sql` | **Obsoleto** (base anterior, con `PERSONA`/`USUARIO`/`TIPO_DESCUENTO`): el código de esta rama ya no funciona con ella. | No |
| `upgrades/` | Diagnóstico y actualización puntual de `ClubCamionerosPRUEBA` del 23/09/2026. | No |

## Base para las pruebas de integración

Las pruebas marcadas como de integración (`CACC_TEST_DB`) corren contra una base real y necesitan **todas las migraciones
aplicadas**. Una base nueva creada con `esquema/ClubCamioneros_unificada_crear_base.sql` solo trae las anteriores a
`V20261018_02`: levantá la API una vez en `Development` apuntando a esa base (las aplica sola) o corré las pendientes a mano:

```
sqlcmd -S localhost -E -C -I -b -d <base> -i database\migrations\V20261021_01__inscripcion_anual.sql
```

Si falta `V20261021_01`, fallan las pruebas de `InscripcionAnual` con `Cannot insert duplicate key row ... UX_PAGOS_InscripcionPendiente_Jugador`.

No agregar scripts nuevos en la raíz ni copiar los históricos a `migrations/`: usan `GO` y
`USE`, que el ejecutor rechaza. El código del ejecutor está en `ApiGestion/Database/`.
