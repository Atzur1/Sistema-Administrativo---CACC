# Unificación de las bases A y B (HU-053)

Importa la base del **Portal Administrativo (A)** y la del **Portal Deportivo (B)** a una base con el esquema unificado
(`../esquema/ClubCamioneros_unificada_crear_base.sql`), sin duplicar personas y sin perder registros.
Los resultados de la prueba y las decisiones están en [`QA-HU-053.md`](../../QA-HU-053.md).

**Las bases de origen solo se leen.** Todo se escribe en la base destino, que el script crea desde cero y que tiene que
tener un nombre de prueba (`Prueba` o `_HU053`).

## Cómo se prueba

Desde la raíz del repo, en PowerShell (no Git Bash: rompe las rutas que recibe `sqlcmd`):

```powershell
# Sin decisiones sobre los conflictos de DNI: se detiene en el paso 2 (BLOQUEADA) y lista los conflictos.
.\database\unificacion\00_ejecutar_prueba.ps1

# Con las decisiones de PRUEBA (no son del club) y respaldo verificado de los orígenes:
.\database\unificacion\00_ejecutar_prueba.ps1 -ConResolucionDePrueba -RespaldarOrigenes

# Pruebas complementarias
.\database\unificacion\pruebas\concurrencia_ids.ps1     # ids bajo concurrencia (IDENTITY/SEQUENCE vs MAX+1)
.\database\unificacion\pruebas\v07_simulacion.ps1       # V20260929_07 original vs protegido, sobre copias
```

Parámetros: `-Servidor` (por defecto `(localdb)\MSSQLLocalDB`), `-BaseA` (`ClubCamionerosPRUEBA`), `-BaseB`
(`CACC_B_Origen`), `-BaseDestino` (`ClubCamionerosUnificadaV2_HU053`), `-Salida` (log y respaldos, `%TEMP%\cacc-hu053`).
`-SoloEsquema` crea la base vacía con el esquema y las migraciones, sin importar (sirve para las pruebas de DAO "previas").

## Qué hace cada paso

| Paso | Archivo | Escribe en | Repetible |
|---|---|---|---|
| 0 | `00_ejecutar_prueba.ps1` | crea la base destino (borra la anterior si existe) | sí: siempre parte del mismo estado |
| 1 | `01_preparar_migra.sql` | esquema `migra` (mapeos, conflictos, decisiones, conteos) | sí (idempotente); se niega a correr fuera de una base unificada |
| 2 | `02_analizar_identidades.sql` | solo `migra` | sí: recalcula todo |
| 3 | `03_importar.sql` | `dbo` (una sola transacción) | **no**: exige una base recién creada |
| 4 | `04_validar.sql` | solo `migra.validacion` | sí |

El runner, además: aplica las migraciones de `../migrations/` que el esquema todavía no trae y las registra con el mismo
hash que calcula la API; toma una huella (filas y checksum por tabla) de A y de B antes y después, y falla si cambiaron.

### Orden de carga (derivado de las FK del esquema unificado)

Catálogos (ya vienen en el esquema y se verifican: CATEGORIAS y ROLES por id **y** nombre; TIPOS_DESCUENTO por nombre)
→ PERSONAS → JUGADORES → USUARIOS → STAFF → STAFF_CATEGORIAS → RESPONSABLES → JUGADORES_RESPONSABLES → FICHAS_MEDICAS →
HISTORIAL_PARTIDOS → JUGADORES_PARTIDOS → ASISTENCIAS → JUGADORES_DESCUENTOS → PAGOS → AUDITORIA_CAMBIOS de A.

### Identificadores

- **A primero y conserva sus ids** (comprobantes y auditoría siguen valiendo). `IDENTITY_INSERT` en las tablas IDENTITY;
  valor explícito en JUGADORES y PAGOS.
- Después se adelantan `SEQ_JUGADORES` y `SEQ_PAGOS` por encima del mayor id presente (nunca hacia atrás).
- **B recibe ids nuevos** y cada id viejo queda en `migra.mapa (entidad, origen, id_origen, id_unificado, regla)`.
  Ninguna FK de B se copia tal cual: todas se traducen por el mapa.

### Personas: regla del DNI

DNI normalizado (sin espacios, puntos ni guiones; válido con 6 a 9 dígitos; comparado como texto).

| Caso | Qué pasa |
|---|---|
| Mismo DNI en A y en B | misma persona. Quedan los datos de A; B completa lo vacío. Lo descartado queda en `migra.transformacion`. |
| DNI repetido dentro de un sistema, registros idénticos y a lo sumo uno con dependientes | se unen solos. |
| Cualquier otro DNI repetido (datos distintos, dos con jugador, etc.) | **conflicto**: la importación se detiene hasta que haya una decisión humana. |
| Sin DNI válido | se importa sola, sin unirse a nadie. |
| Dos usuarios para la misma persona, o DNI que solo difieren en ceros a la izquierda | conflicto que **no** se resuelve con una decisión: hay que corregir el origen. |

### Cómo se resuelve un conflicto (antes de producción)

1. Correr el runner sin `-ConResolucionDePrueba` y revisar `migra.conflicto_dni` en la base destino.
2. Para cada grupo, una persona del club decide qué registro queda (`CANONICA`, exactamente uno) y cuáles se le unen
   (`FUSIONAR`). Sus dependientes (jugador, pagos, fichas…) pasan al registro que queda; nada se borra.
3. Cargar esas decisiones en un script propio con la forma de `resolucion_dni_PRUEBA.sql`, con `es_prueba = 0` y
   `decidido_por` real. Si los datos de origen cambian, el paso 2 rechaza las decisiones que ya no corresponden.

### Triggers

Durante el paso 3 se deshabilitan solo los de auditoría (`TR_AUDIT_*`, `TR_AUDITORIA_IDENTIDAD_ACTOR`): copiar datos no es
una operación de un usuario, y la carga queda registrada en `migra.ejecucion`. Los de reglas (`TR_JUGADORES_GENERO_OBLIGATORIO`,
`TR_PERSONAS_GENERO_DE_JUGADOR`, `TR_JUGADORES_PERIODOS`) quedan activos y validan los datos que entran. Antes del COMMIT
se vuelven a habilitar y se verifica que no quede ninguno deshabilitado; el paso 4 comprueba los 14 del esquema.

## Producción (fuera del alcance de HU-053)

No se ejecutó en producción. Procedimiento previsto:

1. **Antes**: decisiones reales cargadas para todos los conflictos; ventana sin uso de los dos portales; respaldo
   `COPY_ONLY` con `CHECKSUM` de A y de B y `RESTORE VERIFYONLY`.
2. **Ejecución**: base destino nueva con el esquema unificado; pasos 1 a 4 con un usuario administrador de la base (el paso 3
   necesita `ALTER` para `IDENTITY_INSERT` y los triggers). Las APIs siguen apuntando a A y B hasta terminar.
3. **Validación**: el paso 4 sin ningún FAIL, huella de A y B sin cambios, y revisión de `migra.transformacion` con el club.
4. **Rollback**: si algo falla, no hay nada que deshacer en A ni en B (no se tocan): se borra la base destino y las APIs
   siguen con A y B. Si el problema aparece después de cambiar las APIs a la base unificada, se vuelven a apuntar a A y B y
   se restaura su respaldo solo si alguien escribió en ellas por error.
