"""HU-007 - Destino despues del login y restricciones del perfil exclusivamente deportivo.

Corre contra la API real (ApiGestion) levantada sobre CCACUnificada_QA_HU007, recien cargada con 01_datos_prueba.sql.
Reutiliza la infraestructura y la matriz de 64 endpoints de qa/HU-061/run_hu061.py.

Uso:
  python qa/HU-007/run_hu007.py [--base http://localhost:5161] [--base-datos CCACUnificada_QA_HU007] [--salida evidencia.json]
"""
import argparse
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "HU-061"))
import run_hu061 as base  # noqa: E402

CONTRASENIA = "Deporte#QA2026"
DNI = {"SA": "90070001", "ADM": "90070002", "DT2": "90070003", "PF1": "90070004", "DT0": "90070005", "MED": "90070006",
       "NADA": "90070007"}
JUGADORES = {"J2": "90070102", "J3": "90070103", "J4": "90070104", "J5": "90070105"}

# Portales que cada cuenta tiene que recibir en el login (ApiGestion/Services/PortalesDeLaCuenta.cs).
PORTALES_ESPERADOS = {
    "SA": ["administrativo", "deportivo", "personas"],
    "ADM": ["administrativo"],
    "DT2": ["deportivo"], "PF1": ["deportivo"], "DT0": ["deportivo"], "MED": ["deportivo"],
}
# Jugadores que cada cuenta ve en la API de integracion (alcance por STAFF_CATEGORIAS).
VISIBLES = {
    "SA": {"J2", "J3", "J4", "J5"},
    "MED": {"J2", "J3", "J4", "J5"},
    "DT2": {"J2", "J3", "J5"},  # categorias 2 y 3; J5 pertenece a la 3 aunque se le cobre la 4
    "PF1": {"J2"},
    "DT0": set(),               # sin fila en STAFF: no se amplia nada
}


def main():
    argumentos = argparse.ArgumentParser()
    argumentos.add_argument("--base", default="http://localhost:5161")
    argumentos.add_argument("--base-datos", default="CCACUnificada_QA_HU007")
    argumentos.add_argument("--salida", default="evidencia_hu007.json")
    opciones = argumentos.parse_args()
    base.api = opciones.base.rstrip("/")
    base.BASE_DATOS = opciones.base_datos
    registrar = base.registrar

    jug = int(base.escalar("SELECT j.PK_id_jugador FROM dbo.JUGADORES j JOIN dbo.PERSONAS p ON p.PK_id_persona = j.FK_id_persona "
                           "WHERE p.dni = '%s'" % JUGADORES["J2"]))

    tokens = {}
    for clave, esperados in PORTALES_ESPERADOS.items():
        estado, cuerpo = base.login(DNI[clave], CONTRASENIA)
        cuerpo = cuerpo if isinstance(cuerpo, dict) else {}
        token = cuerpo.get("token")
        tokens[clave] = token
        categorias = sorted(base.claims(token)[1].get("id_categoria", []) if token else [])
        if isinstance(categorias, str):
            categorias = [categorias]
        registrar("LOG-%s" % clave, "cuenta %s" % clave, clave, "POST /api/auth/login",
                  "200; portales=%s" % esperados,
                  "%d; role=%s; portales=%s; id_categoria=%s" % (estado, cuerpo.get("role"), cuerpo.get("portales"), categorias),
                  estado == 200 and cuerpo.get("portales") == esperados)

    _, cuerpo_dt2 = base.login(DNI["DT2"], CONTRASENIA)
    categorias = base.claims(cuerpo_dt2["token"])[1].get("id_categoria")
    categorias = sorted([categorias] if isinstance(categorias, str) else categorias or [])
    registrar("CAT-01", "DT2 con STAFF_CATEGORIAS 2 y 3", "DT2", "claims id_categoria del token",
              "las dos categorias: ['2', '3']", str(categorias), categorias == ["2", "3"])
    tokens["DT2"] = cuerpo_dt2["token"]

    estado, cuerpo = base.login(DNI["NADA"], CONTRASENIA)
    registrar("SIN-01", "cuenta activa sin acceso Administrativo ni rol deportivo", "NADA", "POST /api/auth/login",
              "sin token y sin destino (nunca el Administrativo por defecto)", "%d %s" % (estado, cuerpo),
              estado in (401, 403) and not (isinstance(cuerpo, dict) and cuerpo.get("token")))

    # Restriccion administrativa por API: el perfil deportivo recibe 403 en los 64 endpoints del Administrativo.
    for clave in ("DT2", "PF1", "DT0", "MED"):
        base.recorrer_matriz("API-%s" % clave, clave, tokens[clave], jug, lambda alcance: False)
    base.recorrer_matriz("API-ADM", "ADM (multientorno no aplica: solo Administrativo)", tokens["ADM"], jug, lambda alcance: alcance == "A")
    base.recorrer_matriz("API-SA", "SA (multientorno)", tokens["SA"], jug, lambda alcance: True)

    # Carga de datos autorizada: el lote de estado de deuda solo devuelve lo que cae en las categorias del staff.
    todos = list(JUGADORES.values())
    por_dni = {v: k for k, v in JUGADORES.items()}
    for clave, esperados in VISIBLES.items():
        estado, cuerpo = base.llamar("POST", "/api/integracion/v1/estado-deuda/lote", tokens[clave], {"dnis": todos})
        vistos = {por_dni[i["dni"]] for i in (cuerpo or {}).get("items", [])} if estado == 200 else set()
        con_montos = any(k in json.dumps(cuerpo) for k in ("monto", "importe", "saldo")) if estado == 200 else False
        registrar("DAT-%s" % clave, "jugadores J2 (cat 2), J3 (cat 3), J4 (cat 4), J5 (cat 4 + 3)", clave,
                  "POST /api/integracion/v1/estado-deuda/lote con los 4 DNI",
                  "200; ve %s; sin montos" % sorted(esperados), "%d; ve %s; montos=%s" % (estado, sorted(vistos), con_montos),
                  estado == 200 and vistos == esperados and not con_montos)
    estado, _ = base.llamar("GET", "/api/integracion/v1/estado-deuda/dni/%s" % JUGADORES["J4"], tokens["DT2"])
    registrar("DAT-02", "J4 fuera de las categorias de DT2", "DT2", "GET estado-deuda/dni/{J4}", "404 (no revela que existe)",
              "%d" % estado, estado == 404)
    estado, _ = base.llamar("POST", "/api/integracion/v1/estado-deuda/lote", tokens["ADM"], {"dnis": todos})
    registrar("DAT-03", "Administrador del Administrativo", "ADM", "POST estado-deuda/lote", "403", "%d" % estado, estado == 403)

    with open(opciones.salida, "w", encoding="utf-8") as archivo:
        json.dump(base.resultados, archivo, ensure_ascii=False, indent=2)
    resumen = {e: sum(1 for r in base.resultados if r["estado"] == e) for e in ("PASS", "FAIL", "BLOCKED")}
    print("\nResumen:", resumen, "->", opciones.salida)
    return 0 if resumen["FAIL"] == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
