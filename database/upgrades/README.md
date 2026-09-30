# Actualizacion de ClubCamionerosPRUEBA

## Resultado local del 23/09/2026

Se aplicaron ambas migraciones sobre `(localdb)\MSSQLLocalDB`, despues de un
respaldo COPY_ONLY con CHECKSUM verificado mediante RESTORE VERIFYONLY.
Se conservaron cantidades y huellas de verificacion de 577 jugadores, 1258 pagos
y 9 asignaciones de descuentos. DBCC CHECKCONSTRAINTS no reporto infracciones.
La segunda ejecucion encontro cero migraciones pendientes. ARANCELES quedo vacia.

La aplicacion puntual se realizo con `02_aplicar_migraciones_PRUEBA.ps1`, que
registra los mismos nombres y hashes que la API. Es una herramienta limitada
a estas dos migraciones y a esta instancia; no reemplaza el flujo habitual.
El 29/09/2026 se valido el inicio de la API con el ejecutor: verifico estas dos
migraciones sin reaplicarlas (ver `../InstructivoEquipo.md`, seccion 1).

## Diagnostico y alcance

El archivo `bbdd.txt` describe una version anterior a la base local inspeccionada
el 23/09/2026 en `(localdb)\MSSQLLocalDB`. No debe ejecutarse sobre esa base:
contiene instrucciones para crear otra base y tablas que ya existen.

El diagnostico real mostro que `TIPO_DESCUENTO` y `JUGADORES_DESCUENTOS` ya tienen
las claves autoincrementales, columnas, validaciones e indice de vigencias del
modelo del equipo. No hace falta reconstruir esas tablas ni copiar datos de prueba.

Faltaban dos objetos, incorporados mediante migraciones en `../migrations/`:

- `V20260923_01__crear_aranceles.sql`: crea `ARANCELES`, con unicidad por genero
  y fecha, generos admitidos y monto positivo.
- `V20260923_02__index_pagos_pendientes.sql`: agrega el indice de pagos pendientes
  que ya estaba en un script historico del repositorio.

Las migraciones no borran ni modifican jugadores, usuarios, pagos o descuentos.
No cargan montos de arancel: deben configurarse con los valores acordados por
el club antes de emitir cuotas. No importan los jugadores ni el administrador
del archivo de ejemplo del equipo.

## Aplicacion

1. Conservar un respaldo verificable de `ClubCamionerosPRUEBA` antes de aplicar.
2. Confirmar que la conexion de la API apunta a la instancia local correcta y
   a `ClubCamionerosPRUEBA`.
3. Usar el ejecutor de migraciones del proyecto, que registra scripts aplicados.
   No ejecutar los TXT completos ni el script historico que apunta a otra base.
4. Verificar que existan `dbo.ARANCELES`, `IX_PAGOS_Estado_Pendientes` y las dos
   entradas nuevas del historial, conservando las cantidades de registros previas.

`01_diagnostico_PRUEBA.sql` es una consulta de solo lectura para revisar esquema,
relaciones, indices y cantidades. Se ejecuta manualmente en SSMS o sqlcmd;
no corresponde copiarla a `migrations/`.

La comprobacion de esta maquina no garantiza que otra base del equipo tenga el
mismo esquema. Si conserva exactamente el esquema viejo del TXT (sin IDENTITY
en descuentos), necesita una migracion especifica adicional: no hay que borrar
sus tablas para hacer coincidir el modelo.
