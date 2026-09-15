#!/usr/bin/env bash
# Pruebas de integración de HU-011 contra la API real y SQL Server.
# Cada caso imprime lo esperado y lo obtenido; al final hay un resumen.

API="http://localhost:5118/api/Players"
TOKEN=$(cat /tmp/hu011_token.txt)
AUTH="Authorization: Bearer $TOKEN"
JSON="Content-Type: application/json"

PASS=0
FAIL=0

# Jugadores de prueba, distintos entre sí para no pisarse
P1=10   # Beca Completa
P2=11   # Media Beca porcentual
P3=12   # Descuento por Hermanos monto fijo
P4=13   # duplicidad
P5=14   # editar
P6=15   # cancelar y reasignar

limpiar() {
  for p in $P1 $P2 $P3 $P4 $P5 $P6; do
    curl -s -o /dev/null -X DELETE "$API/$p/discount" -H "$AUTH"
  done
}

# chequear <nombre> <http_esperado> <http_obtenido> [detalle]
chequear() {
  local nombre="$1" esperado="$2" obtenido="$3" detalle="$4"
  if [ "$esperado" = "$obtenido" ]; then
    PASS=$((PASS+1))
    printf '  OK   %-52s esperado %s, obtuvo %s %s\n' "$nombre" "$esperado" "$obtenido" "$detalle"
  else
    FAIL=$((FAIL+1))
    printf '  FALLA %-51s esperado %s, obtuvo %s %s\n' "$nombre" "$esperado" "$obtenido" "$detalle"
  fi
}

code() { curl -s -o /tmp/hu011_body.txt -w "%{http_code}" "$@"; }
body() { cat /tmp/hu011_body.txt; }

echo "=============================================="
echo " HU-011 - Pruebas de integracion contra la API"
echo "=============================================="
echo

limpiar

echo "--- CASO 1: jugador sin beneficio -> Beca Completa ---"
C=$(code -X POST "$API/$P1/discount" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Beca Completa","valueType":"%","percentage":100,"startDate":"2026-09-15"}')
chequear "CASO 1 alta Beca Completa" 201 "$C"
echo "         respuesta: $(body | head -c 190)"
echo

echo "--- CASO 2: Media Beca porcentual 50% ---"
C=$(code -X POST "$API/$P2/discount" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Media Beca","valueType":"%","percentage":50,"startDate":"2026-09-15"}')
chequear "CASO 2 alta Media Beca 50%" 201 "$C"
PERS=$(code "$API/$P2/discount"; body | node -e "let d='';process.stdin.on('data',c=>d+=c).on('end',()=>{const j=JSON.parse(d);console.log(j.percentage+' '+j.valueType)})" 2>/dev/null)
echo "         persistido (GET): $PERS"
echo

echo "--- CASO 3: Descuento por Hermanos monto fijo \$15000 ---"
C=$(code -X POST "$API/$P3/discount" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Descuento por Hermanos","valueType":"$","fixedAmount":15000,"startDate":"2026-09-15"}')
chequear "CASO 3 alta monto fijo" 201 "$C"
echo "         respuesta: $(body | head -c 190)"
echo

echo "--- CASO 4: sin motivo -> rechazado ---"
C=$(code -X POST "$API/20/discount" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"","valueType":"%","percentage":50}')
chequear "CASO 4 motivo vacio" 400 "$C"

C=$(code -X POST "$API/20/discount" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Beca Inventada","valueType":"%","percentage":50}')
chequear "CASO 4b motivo fuera del catalogo" 400 "$C"
echo "         mensaje: $(body | head -c 150)"
echo

echo "--- CASO 5: porcentaje 0 -> rechazado ---"
C=$(code -X POST "$API/20/discount" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Media Beca","valueType":"%","percentage":0}')
chequear "CASO 5 porcentaje 0" 400 "$C"
echo

echo "--- CASO 6: porcentaje 101 -> rechazado ---"
C=$(code -X POST "$API/20/discount" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Media Beca","valueType":"%","percentage":101}')
chequear "CASO 6 porcentaje 101" 400 "$C"
echo

echo "--- CASO 7: monto fijo 0 y negativo -> rechazados ---"
C=$(code -X POST "$API/20/discount" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Descuento por Hermanos","valueType":"$","fixedAmount":0}')
chequear "CASO 7a monto fijo 0" 400 "$C"

C=$(code -X POST "$API/20/discount" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Descuento por Hermanos","valueType":"$","fixedAmount":-500}')
chequear "CASO 7b monto fijo negativo" 400 "$C"

C=$(code -X POST "$API/20/discount" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Media Beca","valueType":"%","percentage":50,"fixedAmount":15000}')
chequear "CASO 7c porcentaje + monto juntos" 400 "$C"
echo

echo "--- CASO 9: duplicado por API directa -> rechazado ---"
C=$(code -X POST "$API/$P4/discount" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Media Beca","valueType":"%","percentage":50,"startDate":"2026-09-15"}')
chequear "CASO 9a primera asignacion" 201 "$C"

C=$(code -X POST "$API/$P4/discount" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Beca Completa","valueType":"%","percentage":100,"startDate":"2026-09-15"}')
chequear "CASO 9b SEGUNDA asignacion (debe dar 409)" 409 "$C"
echo "         mensaje: $(body | head -c 160)"
echo

echo "--- CASO 10: editar -> cambios persistidos ---"
C=$(code -X POST "$API/$P5/discount" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Media Beca","valueType":"%","percentage":50,"startDate":"2026-09-15"}')
chequear "CASO 10a alta previa" 201 "$C"

C=$(code -X PUT "$API/$P5/discount" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Descuento por Hermanos","valueType":"$","fixedAmount":22500,"startDate":"2026-09-15"}')
chequear "CASO 10b editar a monto fijo" 200 "$C"

code "$API/$P5/discount" > /dev/null
LEIDO=$(body | node -e "let d='';process.stdin.on('data',c=>d+=c).on('end',()=>{const j=JSON.parse(d);console.log(j.type+' | '+j.valueType+' | pct='+j.percentage+' | fijo='+j.fixedAmount)})" 2>/dev/null)
echo "         releido de la base: $LEIDO"
if echo "$LEIDO" | grep -q "Descuento por Hermanos" && echo "$LEIDO" | grep -q "fijo=22500"; then
  PASS=$((PASS+1)); printf '  OK   %-52s edicion realmente persistida\n' "CASO 10c verificacion"
else
  FAIL=$((FAIL+1)); printf '  FALLA %-51s la edicion no persistio\n' "CASO 10c verificacion"
fi
echo

echo "--- CASO 11: cancelar -> libera y permite reasignar ---"
C=$(code -X POST "$API/$P6/discount" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Beca Completa","valueType":"%","percentage":100,"startDate":"2026-09-15"}')
chequear "CASO 11a alta previa" 201 "$C"

C=$(code -X DELETE "$API/$P6/discount" -H "$AUTH")
chequear "CASO 11b cancelar" 204 "$C"

C=$(code "$API/$P6/discount")
chequear "CASO 11c ya no hay activa" 404 "$C"

C=$(code -X POST "$API/$P6/discount" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Media Beca","valueType":"%","percentage":50,"startDate":"2026-09-15"}')
chequear "CASO 11d reasignar tras cancelar" 201 "$C"
echo

echo "--- CASO 12: los datos vienen de la base, no del front ---"
code "$API/$P1/discount" > /dev/null
echo "         GET jugador $P1: $(body | head -c 190)"
C=$(code "$API/$P1/discount")
chequear "CASO 12 relectura tras alta" 200 "$C"
echo

echo "--- EXTRA: editar jugador sin bonificacion -> 404 ---"
C=$(code -X PUT "$API/500/discount" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Media Beca","valueType":"%","percentage":50}')
chequear "EXTRA editar inexistente" 404 "$C"

echo "--- EXTRA: cancelar jugador sin bonificacion -> 404 ---"
C=$(code -X DELETE "$API/500/discount" -H "$AUTH")
chequear "EXTRA cancelar inexistente" 404 "$C"

echo "--- EXTRA: jugador inexistente -> 400 ---"
C=$(code -X POST "$API/999999/discount" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Media Beca","valueType":"%","percentage":50}')
chequear "EXTRA jugador inexistente" 400 "$C"

echo "--- EXTRA: sin token -> 401 ---"
C=$(code -X POST "$API/$P1/discount" -H "$JSON" \
    -d '{"reason":"Media Beca","valueType":"%","percentage":50}')
chequear "EXTRA POST sin token" 401 "$C"

echo
echo "=============================================="
echo " RESULTADO:  $PASS pasaron,  $FAIL fallaron"
echo "=============================================="

limpiar
exit $FAIL
