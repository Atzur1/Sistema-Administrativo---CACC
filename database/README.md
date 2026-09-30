# Base de datos

Los cambios de estructura se entregan como migraciones SQL versionadas que la API aplica
automáticamente al iniciar en `Development`, registrándolas en `dbo.__CaccMigraciones`.

**Guía completa (equipo e IA): [InstructivoEquipo.md](InstructivoEquipo.md).**

## Contenido de esta carpeta

| Ruta | Qué es | ¿Lo aplica la API? |
|---|---|---|
| `migrations/` | Migraciones automáticas. Todo cambio nuevo va acá. | Sí |
| `V2026*.sql` (raíz) | Scripts manuales históricos del equipo, hasta `V20260929_07`. Forman la base común que verifica `migrations/V20260930_01__verificar_base_equipo.sql`. | No |
| `upgrades/` | Diagnóstico y actualización puntual de `ClubCamionerosPRUEBA` del 23/09/2026. | No |

No agregar scripts nuevos en la raíz ni copiar los históricos a `migrations/`: usan `GO` y
`USE`, que el ejecutor rechaza. El código del ejecutor está en `ApiGestion/Database/`.
