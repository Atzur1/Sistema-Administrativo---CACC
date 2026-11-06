# Borrador Jira — NO registrado

Título: [HU064] El flujo de becas impide rehabilitar a un jugador restringido por cuotas vencidas

Severidad propuesta: Alta / bloqueo de aceptación HU064. Tipo: contradicción funcional entre requisito de habilitación y política de beneficios; no se propone cambiarla sin decisión de producto.

## Descripción

HU064 exige observar jugador restringido por deuda vencida → asignación de beca vigente → Al día en la siguiente consulta deportiva. La API de beneficios rechaza el inicio en cuotas anteriores a la última emitible sin pagos. Son precisamente las vencidas las que restringen según HU083. Una beca preventiva no demuestra la transición exigida. El supuesto criterio acordado de QA-HU-064.md no se toma como autorización para sustituir el requisito.

## Entorno y trazabilidad

Revalidación basada en entorno HU115; aislamiento comunicado por responsable: BD `HU064_Revalidacion_20261104`, Administrativo `http://localhost:5328`, Deportivo `http://localhost:5329`. El responsable confirmó APIs listas; la correspondencia de binarios y fixture debe leerse en `entorno.json` antes de registrar resultado real. Administrativo fuente: rama FramD, f9d4514 + cambios locales; fix de migración separado en b3ee28e. La copia de trabajo está en `C:/Users/FramD/Documents/Tesis/HU-064-entorno/cacc-unificacion/trabajo/nuestro`; no es un clone independiente. Deportivo: rama fix/SCRUM-805-credencial-integracion, a9e690f5 (git verificado). Unificación: misma rama, 61e1030 (git verificado). No existe clone Administrativo dentro de HU-064-entorno; no atribuir la ejecución a un clone inexistente.

## Fixture previsto D y pasos reproducibles

Pendiente de ejecutar. Crear/identificar fixture D activo, categoría permitida al DT, sin pagos ni beneficios superpuestos; una cuota de septiembre de 2026 pendiente, con saldo positivo y vencida al 04/11/2026. Anotar DNI e ID reales en el manifiesto; no reutilizar ciegamente DNI del QA histórico. La preparación SQL es fixture, no evidencia de asignación por API.

1. Con token DT válido y credencial de sistema en backend, consultar `GET http://localhost:5329/api/jugadores/{idDeportivoD}` y listado `GET /api/jugadores`. Confirmar estado inicial SOLO ENTRENAMIENTOS. IDs de ambos portales pueden diferir.
2. Con bearer Administrativo rol 1 o 2, consultar `GET http://localhost:5328/api/players/discounts/types` y verificar que existe motivo `Becado`; consultar `GET /api/players/{idAdminD}/discount/first-period` y conservar respuesta.
3. Enviar `POST http://localhost:5328/api/players/{idAdminD}/discount`, Content-Type application/json, bearer autorizado. Payload conforme a DiscountRequestDTO:

```json
{
  "reason": "Becado",
  "valueType": "%",
  "percentage": 100,
  "fixedAmount": null,
  "startDate": "2026-09-01",
  "endDate": "2026-12-31"
}
```

4. Repetir ficha/listado deportivo inmediatamente después de la respuesta. Consultar integración directa `GET http://localhost:5328/api/integracion/v1/estado-deuda/dni/{dniD}` con bearer DT y X-Integration-Key sin registrar secretos.

## Esperado por requisito

La asignación permite cumplir el caso de beca vigente y el jugador pasa a AL DÍA / habilitado sin job ni reinicio en la siguiente consulta. Si la política de beneficios prohíbe esto, se necesita decisión explícita de producto que reconcilie ambas reglas; no falsear el PASS con un fixture preventivo.

## Respuesta prevista — PENDIENTE DE EJECUCIÓN

Por revisión estática: HTTP 422, cuerpo `{ "exito": false, "mensaje": "...primera que se puede becar...Las anteriores...condonar deuda..." }`; la cuota de septiembre continúa restringiendo. El texto exacto depende de primer período y pagos existentes: no se presenta como respuesta capturada. Evidencia: BeneficiosService.cs:74-95 y 249-254, PlayersController.cs:198-205, ResultadoHttp.cs:19-25. Conservar request/response y estado previo/posterior reales al ejecutar.

Vínculos sugeridos: HU064 (aceptación bloqueada), HU011/HU012 (asignación/vigencia de beneficios), HU083 (vencimiento). No inventar IDs Jira ni afirmar ticket creado.
