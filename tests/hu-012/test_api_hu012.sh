#!/usr/bin/env bash
# Pruebas de integración de HU-012 contra la API real y SQL Server.
# Cubren la vigencia obligatoria, el estado calculado por el servidor y la
# validación de superposición de rangos.

API="http://localhost:5118/api/Players"
TOKEN=$(cat /tmp/hu011_token.txt)
AUTH="Authorization: Bearer $TOKEN"
JSON="Content-Type: application/json"

PASS=0
FAIL=0

# Jugadores de prueba
P1=20   # estados: programada / activa / expirada
P2=21   # superposicion
P3=22   # validaciones de rango
P4=23   # caducidad libera el periodo
P5=24   # edicion

limpiar() {
  for p in $P1 $P2 $P3 $P4 $P5; do
    for id in $(curl -s "$API/$p/discounts" | node -e "let d='';process.stdin.on('data',c=>d+=c).on('end',()=>{try{JSON.parse(d).forEach(x=>console.log(x.id))}catch(e){}})"); do
      curl -s -o /dev/null -X DELETE "$API/$p/discount?discountId=$id" -H "$AUTH"
    done
  done
}

chequear() {
  local nombre="$1" esperado="$2" obtenido="$3"
  if [ "$esperado" = "$obtenido" ]; then
    PASS=$((PASS+1)); printf '  OK    %-50s esperado %-22s obtuvo %s\n' "$nombre" "$esperado" "$obtenido"
  else
    FAIL=$((FAIL+1)); printf '  FALLA %-50s esperado %-22s obtuvo %s\n' "$nombre" "$esperado" "$obtenido"
  fi
}

code() { curl -s -o /tmp/hu012_body.txt -w "%{http_code}" "$@"; }
body() { cat /tmp/hu012_body.txt; }
campo() { body | node -e "let d='';process.stdin.on('data',c=>d+=c).on('end',()=>{try{const j=JSON.parse(d);console.log(j['$1']===undefined?'(sin campo)':j['$1'])}catch(e){console.log('(no json)')}})"; }

HOY=$(date +%Y-%m-%d)
AYER=$(date -d "yesterday" +%Y-%m-%d)
MANANA=$(date -d "tomorrow" +%Y-%m-%d)

echo "==================================================="
echo " HU-012 - Vigencia, estados y superposicion"
echo " Hoy segun el sistema: $HOY"
echo "==================================================="
echo

limpiar

echo "--- ESTADO CALCULADO POR EL SERVIDOR ---"

# Expirada: rango enteramente pasado
C=$(code -X POST "$API/$P1/discount" -H "$AUTH" -H "$JSON" \
    -d "{\"reason\":\"Media Beca\",\"valueType\":\"%\",\"percentage\":50,\"startDate\":\"2020-01-01\",\"endDate\":\"2020-12-31\"}")
chequear "alta con rango pasado" 201 "$C"
chequear "  -> estado Expired" "Expired" "$(campo status)"
chequear "  -> isActive derivado en false" "false" "$(campo isActive)"

# Activa: rango que contiene hoy
C=$(code -X POST "$API/$P1/discount" -H "$AUTH" -H "$JSON" \
    -d "{\"reason\":\"Beca Completa\",\"valueType\":\"%\",\"percentage\":100,\"startDate\":\"$AYER\",\"endDate\":\"$MANANA\"}")
chequear "alta con rango que contiene hoy" 201 "$C"
chequear "  -> estado Active" "Active" "$(campo status)"
chequear "  -> isActive derivado en true" "true" "$(campo isActive)"

# Programada: rango enteramente futuro
C=$(code -X POST "$API/$P1/discount" -H "$AUTH" -H "$JSON" \
    -d "{\"reason\":\"Descuento por Hermanos\",\"valueType\":\"\$\",\"fixedAmount\":15000,\"startDate\":\"2030-01-01\",\"endDate\":\"2030-12-31\"}")
chequear "alta con rango futuro" 201 "$C"
chequear "  -> estado Scheduled" "Scheduled" "$(campo status)"
chequear "  -> isActive derivado en false" "false" "$(campo isActive)"

echo
echo "         los tres beneficios del jugador $P1, con su estado:"
curl -s "$API/$P1/discounts" | node -e "let d='';process.stdin.on('data',c=>d+=c).on('end',()=>{JSON.parse(d).forEach(x=>console.log('           '+x.startDate+' a '+x.endDate+'  '+x.type.padEnd(24)+x.status))})"
echo

echo "--- FECHAS OBLIGATORIAS ---"
C=$(code -X POST "$API/$P3/discount" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Media Beca","valueType":"%","percentage":50,"endDate":"2026-12-31"}')
chequear "sin fecha desde" 400 "$C"

C=$(code -X POST "$API/$P3/discount" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Media Beca","valueType":"%","percentage":50,"startDate":"2026-10-01"}')
chequear "sin fecha hasta" 400 "$C"

C=$(code -X POST "$API/$P3/discount" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Media Beca","valueType":"%","percentage":50,"startDate":"","endDate":""}')
chequear "ambas vacias" 400 "$C"

echo
echo "--- RANGO ESTRICTO (los ejemplos de la historia) ---"
C=$(code -X POST "$API/$P3/discount" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Media Beca","valueType":"%","percentage":50,"startDate":"2026-10-10","endDate":"2026-10-10"}')
chequear "fechas iguales 10/10 - 10/10" 400 "$C"
echo "         mensaje: $(body | head -c 160)"

C=$(code -X POST "$API/$P3/discount" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Media Beca","valueType":"%","percentage":50,"startDate":"2026-10-10","endDate":"2026-10-09"}')
chequear "fin anterior al inicio 10/10 - 09/10" 400 "$C"

C=$(code -X POST "$API/$P3/discount" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Media Beca","valueType":"%","percentage":50,"startDate":"2026-10-10","endDate":"2026-10-11"}')
chequear "un dia de diferencia (valido)" 201 "$C"

C=$(code -X POST "$API/$P3/discount" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Media Beca","valueType":"%","percentage":50,"startDate":"2026-13-01","endDate":"2026-12-31"}')
chequear "formato invalido" 400 "$C"

echo
echo "--- SUPERPOSICION DE RANGOS ---"
C=$(code -X POST "$API/$P2/discount" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Media Beca","valueType":"%","percentage":50,"startDate":"2026-01-01","endDate":"2026-06-30"}')
chequear "primer beneficio ene-jun" 201 "$C"

C=$(code -X POST "$API/$P2/discount" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Beca Completa","valueType":"%","percentage":100,"startDate":"2026-07-01","endDate":"2026-12-31"}')
chequear "segundo jul-dic (NO se pisa)" 201 "$C"

C=$(code -X POST "$API/$P2/discount" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Beca Completa","valueType":"%","percentage":100,"startDate":"2026-06-01","endDate":"2026-08-31"}')
chequear "tercero jun-ago (SE PISA con ambos)" 409 "$C"
echo "         mensaje: $(body | head -c 190)"

C=$(code -X POST "$API/$P2/discount" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Beca Completa","valueType":"%","percentage":100,"startDate":"2026-06-30","endDate":"2026-07-15"}')
chequear "solapa por un solo dia (borde)" 409 "$C"

C=$(code -X POST "$API/$P2/discount" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Beca Completa","valueType":"%","percentage":100,"startDate":"2027-01-01","endDate":"2027-12-31"}')
chequear "cuarto 2027 (contiguo, no se pisa)" 201 "$C"

echo
echo "         beneficios del jugador $P2 (ninguno se pisa):"
curl -s "$API/$P2/discounts" | node -e "let d='';process.stdin.on('data',c=>d+=c).on('end',()=>{JSON.parse(d).forEach(x=>console.log('           '+x.startDate+' a '+x.endDate+'  '+x.status))})"
echo

echo "--- CADUCIDAD AUTOMATICA LIBERA EL PERIODO ---"
echo "         (esto es lo que la regla vieja impedia)"
C=$(code -X POST "$API/$P4/discount" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Media Beca","valueType":"%","percentage":50,"startDate":"2024-01-01","endDate":"2024-12-31"}')
chequear "beneficio ya expirado (2024)" 201 "$C"

C=$(code -X POST "$API/$P4/discount" -H "$AUTH" -H "$JSON" \
    -d "{\"reason\":\"Beca Completa\",\"valueType\":\"%\",\"percentage\":100,\"startDate\":\"$AYER\",\"endDate\":\"$MANANA\"}")
chequear "nuevo beneficio vigente SIN cancelar el viejo" 201 "$C"
chequear "  -> el nuevo esta Activo" "Active" "$(campo status)"

echo
echo "--- EDICION RESPETA LA SUPERPOSICION ---"
C=$(code -X POST "$API/$P5/discount" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Media Beca","valueType":"%","percentage":50,"startDate":"2026-01-01","endDate":"2026-06-30"}')
chequear "beneficio A ene-jun" 201 "$C"
ID_A=$(campo id)

C=$(code -X POST "$API/$P5/discount" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Beca Completa","valueType":"%","percentage":100,"startDate":"2026-07-01","endDate":"2026-12-31"}')
chequear "beneficio B jul-dic" 201 "$C"

C=$(code -X PUT "$API/$P5/discount?discountId=$ID_A" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Media Beca","valueType":"%","percentage":50,"startDate":"2026-01-01","endDate":"2026-09-30"}')
chequear "estirar A hasta septiembre (pisa a B)" 409 "$C"

C=$(code -X PUT "$API/$P5/discount?discountId=$ID_A" -H "$AUTH" -H "$JSON" \
    -d '{"reason":"Media Beca","valueType":"%","percentage":75,"startDate":"2026-01-01","endDate":"2026-06-30"}')
chequear "editar A sin tocar el rango (no choca consigo mismo)" 200 "$C"
chequear "  -> porcentaje persistido" "75" "$(campo percentage)"

echo
echo "--- REGRESION HU-014 / HU-011 ---"
printf '  GET /discounts      -> HTTP %s\n' "$(curl -s -o /dev/null -w '%{http_code}' $API/discounts)"
printf '  GET /discounts/all  -> HTTP %s\n' "$(curl -s -o /dev/null -w '%{http_code}' $API/discounts/all)"
C=$(code "$API/5/discount"); chequear "jugador 5 (expirada) sigue dando 404" 404 "$C"
C=$(code "$API/6/discount"); chequear "jugador 6 (cancelada) sigue dando 404" 404 "$C"
C=$(code "$API/1/discount"); chequear "jugador 1 (vigente) sigue dando 200" 200 "$C"
chequear "  -> con status Active" "Active" "$(campo status)"
C=$(code "$API/0/discount"); chequear "id invalido sigue dando 400" 400 "$C"
C=$(code -X POST "$API/$P1/discount" -H "$JSON" -d '{}'); chequear "sin token sigue dando 401" 401 "$C"

echo
echo "==================================================="
echo " RESULTADO:  $PASS pasaron,  $FAIL fallaron"
echo "==================================================="

limpiar
exit $FAIL
