# QA — HU-065: cambios de categoría, aranceles y bajas entre sistemas impactan bien en las cuotas

Evidencia de las pruebas de HU-065 en la rama `feat/HU-065-pruebas-cambios-jugador`, ejecutadas junto con Claude Code el
2026-10-28, contra una copia de la base unificada de prueba (`ClubCamioneros_HU063_Descartable`). La base de prueba
original no se modificó.

Esta HU es solo de pruebas: **no se modificó código del sistema**, y tampoco se tocó el Portal Deportivo. Lo nuevo es el
archivo de tests `ApiGestion.Tests/CambiosDelJugadorEntreSistemas.Integration.Tests.cs` y este informe.

---

## 1. Cómo se probó

- **Los cambios del Deportivo se imitan como él los hace:** el Portal Deportivo escribe directo en la base compartida, así
  que el test hace lo mismo con un `UPDATE` sobre `JUGADORES` (categoría, `fecha_baja`, `fecha_alta`). No se usa su código.
- **Las cuotas se emiten con el SQL real:** el mismo del generador mensual y de la programación de aranceles
  (`SqlCuotaMensual`).
- **Los casos no dejan datos:** cada caso corre en una transacción que se revierte, con categorías y aranceles propios en
  2097, así no depende de los aranceles que tenga cargados cada base.
- **La única excepción es la prueba de Deudas y Morosidad:** esa pantalla lee con su propia conexión y no ve datos sin
  confirmar, así que esta prueba graba datos. Por eso solo corre contra una base con "Descartable" en el nombre
  (variable `CACC_TEST_DB_DESCARTABLE`).

## 2. Resultado por caso

| Caso de la HU | Prueba | Resultado |
|---|---|---|
| **Cambio de categoría** (HU-010) | `CambioDeCategoria_LaProximaCuotaTomaElArancelNuevoYLasEmitidasNoCambian` | ✅ Un jugador pasa de una categoría con arancel de $10.000 a otra con arancel de $15.000. La siguiente cuota que se emite sale con $15.000. Las ya emitidas, pendientes o pagadas, siguen en $10.000 y no se vuelven a emitir. |
| **Arancel nuevo de categoría** (HU-049) | `ArancelNuevoDeCategoria_SoloAfectaASuCategoriaDesdeSuMes` | ✅ Al programar un arancel nuevo para una categoría desde mayo, solo sus jugadores reciben el monto nuevo, y desde mayo. El mes anterior no cambia, y los de otra categoría siguen con el suyo. |
| **Arancel nuevo de género** (HU-049) | `ArancelNuevoDeGenero_SoloAfectaAEseGeneroSinArancelDeCategoria` | ✅ Alcanza solo a ese género. Quien tiene arancel de su categoría sigue con el de la categoría, y antes del mes de vigencia no se emite nada. |
| **Baja** (HU-057) | `Baja_NoRecibeCuotasDeLosMesesSiguientesYLaDeudaSigue` | ✅ El mes de la baja se cobra, porque estuvo en el club. Desde el mes siguiente no se emiten cuotas, ni con el generador ni a mano, y tampoco figuran como faltantes. Lo que debía sigue pendiente. |
| **Baja con deuda en Deudas y Morosidad** (HU-057) | `UnJugadorDadoDeBajaConDeuda_SigueEnDeudasYMorosidadYElDeportivoLoVeDeBaja` | ✅ Sigue en Deudas y Morosidad con toda su deuda ($30.000, 3 cuotas vencidas) y su fecha de baja. |
| **Reincorporación** | `Reincorporacion_VuelveARecibirCuotaDesdeEseMes` | ✅ Si se le saca la baja **y** se le pone como alta el día en que vuelve, recibe cuota desde ese mes. Los meses en que estuvo afuera no se cobran ni figuran como faltantes. Ver el hallazgo 1. |
| **Estado tras la baja** | `EstadoTrasLaBaja_ElDeportivoLoVeDeBajaYSinAcceso` y la prueba de Deudas y Morosidad | ✅ Lo que lee el Deportivo (`estado-deuda`) lo muestra como `baja` y sin acceso aunque deba. La deuda queda para cobranzas. Que no aparezca en sus listas activas depende de cómo use ese estado el Deportivo: se valida con ellos. |

## 3. Pruebas automáticas

| Suite | Resultado |
|---|---|
| HU-065 (7 pruebas nuevas), corridas 3 veces seguidas | **7/7** cada vez |
| Backend completo, contra la copia | **621/622**. El que falla es `UsuarioApiPermisos`, solo porque se compiló fuera de la carpeta del repo: ese test busca un archivo del repo. Compilado dentro del repo, pasa. |
| Backend completo sin base (como en la compu de cualquiera) | **537** correctas y **80** salteadas, que son las de integración |

```powershell
$env:CACC_TEST_DB = "Server=localhost;Database=<base de prueba>;Trusted_Connection=True;TrustServerCertificate=True;"
# Solo para la prueba que graba datos, y solo con una copia descartable:
$env:CACC_TEST_DB_DESCARTABLE = "Server=localhost;Database=<copia>_Descartable;Trusted_Connection=True;TrustServerCertificate=True;"
dotnet test ApiGestion.Tests --filter "FullyQualifiedName~CambiosDelJugadorEntreSistemas|FullyQualifiedName~BajaConDeudaEnPantallas"
```

## 4. Hallazgos para revisar con el equipo del Portal Deportivo

1. **Reincorporación sin actualizar la fecha de alta (posible Bug).** Si el Deportivo reincorpora a un jugador **sacándole
   solo la `fecha_baja`**, la `fecha_alta` queda la original. Para nuestras reglas, el jugador queda "en el club" también en
   los meses en que estuvo afuera: se le pueden generar esas cuotas como faltantes, y `JUGADORES_PERIODOS` abre un período
   nuevo con la fecha de alta vieja. Se comprobó en la copia, dentro de una transacción revertida. Cuando el reingreso lo
   hace el Administrativo, la fecha de alta pasa a ser el día del reingreso y este problema no ocurre. **Hay que confirmar con
   el otro equipo cómo reincorporan.** Si solo sacan la baja, se carga como Bug vinculado a HU-065.
2. **Cambio de categoría antes del día 10.** La cuota del mes se emite el día 10 con la categoría que el jugador tiene ese
   día. Si el Deportivo cambia la categoría entre el 1 y el 9, la cuota de **ese mismo mes** ya sale con el arancel nuevo; si
   la cambia después del 10, el cambio se ve en la del mes siguiente. Hay que confirmar si eso coincide con lo que espera la
   HU-010 ("desde la cuota del mes siguiente").
3. **Baja hecha directo en la base.** Cuando la baja la da el Administrativo, además se cancela la beca o el descuento
   vigente y se quitan las cuotas pendientes sin pagos de meses posteriores (por ejemplo, las emitidas por adelantado). Si el
   Deportivo solo escribe la `fecha_baja`, esos dos pasos no ocurren, porque ningún trigger de la base los hace. No genera
   cuotas nuevas, pero conviene confirmar con ellos si dan la baja así.

**Errores confirmados del lado del Administrativo: ninguno.** El hallazgo 1 se convierte en Bug solo si el Deportivo
reincorpora de esa forma.

## 5. Pendiente para cerrar la HU

- Ejecutar los casos junto con el equipo del Portal Deportivo sobre el entorno compartido (HU-059): cambiar la categoría, dar
  de baja y reincorporar a un jugador desde su sistema, y revisar las cuotas y Deudas y Morosidad en el nuestro.
- Revisar con ellos los tres hallazgos de la sección 4 y cargar como Bug lo que corresponda.
