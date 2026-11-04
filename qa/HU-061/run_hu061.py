"""HU-061 - Casos de habilitacion y permisos de Administrador / SuperAdmin con usuarios del otro portal.

Corre contra la API real (ApiGestion) levantada sobre la base descartable CCACUnificada_QA_HU061, recien preparada
con 00_preparar_entorno.ps1 (o el esquema unificado + 01_datos_prueba.sql). No usa localStorage ni el navegador:
pide el token al login real y lo manda como lo hace el frontend (Authorization: Bearer).

Uso:
  python qa/HU-061/run_hu061.py --log <ruta al log de la API> [--base http://localhost:5161] [--salida evidencia.json]

El log de la API hace falta porque, en Development con Email:LogResetLink=true, el enlace de activacion se escribe en
el log en lugar de enviarse por correo: es la unica forma de completar la activacion sin un buzon real.
Solo se usa la libreria estandar de Python. Requiere sqlcmd en el PATH para las verificaciones de base.
"""
import argparse
import base64
import json
import os
import re
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

SERVIDOR = r"(localdb)\MSSQLLocalDB"
BASE_DATOS = "CCACUnificada_QA_HU061"
CONTRASENIA_DEPORTIVA = "Deporte#QA2026"
CONTRASENIA_NUEVA = "Ingreso#Seguro2026"

DNI = {"SA1": "90610001", "CAND": "90610003", "CAND2": "90610004", "DEP": "90610005", "CAND3": "90610006", "JUG": "90610099"}

resultados = []
api = ""
log_api = ""
# HU-055: /api/integracion exige, además del JWT, la credencial del sistema Deportivo (X-Integration-Key). Se manda en esas
# rutas como lo hace el backend del Deportivo, para que los casos sigan probando la autorización por rol y no la credencial.
clave_integracion = ""


# ----------------------------------------------------------------------------------------------- infraestructura
def llamar(metodo, ruta, token=None, cuerpo=None, reintentar_429=True):
    datos = None if cuerpo is None else json.dumps(cuerpo).encode("utf-8")
    pedido = urllib.request.Request(api + ruta, data=datos, method=metodo)
    if datos is not None:
        pedido.add_header("Content-Type", "application/json")
    if token:
        pedido.add_header("Authorization", "Bearer " + token)
    if clave_integracion and ruta.startswith("/api/integracion"):
        pedido.add_header("X-Integration-Key", clave_integracion)
    try:
        with urllib.request.urlopen(pedido, timeout=60) as respuesta:
            estado, texto = respuesta.status, respuesta.read()
    except urllib.error.HTTPError as error:
        estado, texto = error.code, error.read()
    if estado == 429 and reintentar_429:
        # Politicas de la API: "login" (10 por minuto por IP) y "account-email" (4 cada 15 minutos por IP, compartida
        # por la activacion y la recuperacion). Se espera la ventana que corresponde y se reintenta una vez.
        espera = 905 if "/account-access/" in ruta or "/password-reset/" in ruta else 62
        print("    (429 en %s: espero %d s la ventana del limite)" % (ruta, espera))
        time.sleep(espera)
        return llamar(metodo, ruta, token, cuerpo, reintentar_429=False)
    try:
        return estado, json.loads(texto.decode("utf-8")) if texto else None
    except (ValueError, UnicodeDecodeError):
        return estado, "<%d bytes>" % len(texto)


def sql(consulta):
    salida = subprocess.run(
        ["sqlcmd", "-S", SERVIDOR, "-d", BASE_DATOS, "-E", "-b", "-I", "-W", "-h", "-1", "-s", "|",
         "-Q", "SET NOCOUNT ON; " + consulta],
        capture_output=True, text=True, encoding="utf-8", errors="replace")
    if salida.returncode != 0:
        raise RuntimeError("sqlcmd: " + salida.stdout + salida.stderr)
    return [linea.split("|") for linea in salida.stdout.splitlines() if linea.strip()]


def escalar(consulta):
    filas = sql(consulta)
    return filas[0][0] if filas else None


def claims(token):
    partes = token.split(".")
    def decodificar(parte):
        return json.loads(base64.urlsafe_b64decode(parte + "=" * (-len(parte) % 4)))
    return decodificar(partes[0]), decodificar(partes[1])


def roles_del_token(token):
    _, cuerpo = claims(token)
    rol = cuerpo.get("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", [])
    return rol if isinstance(rol, list) else [rol]


def login(dni, contrasenia):
    return llamar("POST", "/api/auth/login", cuerpo={"username": dni, "password": contrasenia})


def registrar(caso, precondicion, actor, accion, esperado, real, ok, evidencia=None, estado=None):
    estado = estado or ("PASS" if ok else "FAIL")
    resultados.append({"id": caso, "precondicion": precondicion, "actor": actor, "accion": accion,
                       "esperado": esperado, "real": real, "estado": estado, "evidencia": evidencia})
    print("[%s] %-9s %s -> %s" % (estado, caso, accion, real))


def enlace_activacion(email):
    """Ultimo enlace que la API escribio en su log para ese correo (generado, NO enviado)."""
    time.sleep(1)
    with open(log_api, encoding="utf-8", errors="replace") as archivo:
        texto = archivo.read()
    encontrados = re.findall(r"Enlace temporal de acceso para (\S+) \(([^)]*)\): (\S+)", texto)
    propios = [e for e in encontrados if e[0].lower() == email.lower()]
    if not propios:
        return None, None
    _, tipo, url = propios[-1]
    token = urllib.parse.parse_qs(urllib.parse.urlparse(url).query).get("token", [None])[0]
    return url, token


def usuario(id_usuario):
    fila = sql("SELECT FK_id_persona, acceso_portal, ISNULL(CONVERT(varchar, rol_portal), 'NULL'), activacion_pendiente, "
               "token_version, activo FROM dbo.USUARIOS WHERE PK_id_usuario = %d" % id_usuario)[0]
    return {"persona": int(fila[0]), "acceso": int(fila[1]), "rol": fila[2], "pendiente": int(fila[3]),
            "version": int(fila[4]), "activo": int(fila[5])}


def ultimo_evento():
    return int(escalar("SELECT ISNULL(MAX(PK_id_evento), 0) FROM dbo.AUDITORIA_CAMBIOS"))


def autorizado(estado):
    return estado not in (401, 403)


# ------------------------------------------------------------------------------- matriz de endpoints (HU-043)
# (metodo, ruta, cuerpo, puede_el_Administrador). Las escrituras usan ids inexistentes o cuerpos invalidos: prueban la
# autorizacion (que corre antes que la accion) sin cambiar datos. "S" = solo SuperAdmin, "A" = SuperAdmin y Administrador.
def matriz(jug):
    return [
        ("GET", "/api/usuarios/candidatos", None, "S"),
        ("GET", "/api/usuarios/habilitados", None, "S"),
        ("GET", "/api/usuarios/deshabilitados", None, "S"),
        ("PUT", "/api/usuarios/999999", {"nombre": "x", "apellido": "x", "dni": "1"}, "S"),
        ("POST", "/api/usuarios/999999/habilitar", {"email": "x@cacc.test", "rolPortal": 2}, "S"),
        ("POST", "/api/usuarios/999999/resetear-contrasena", None, "S"),
        ("PUT", "/api/usuarios/999999/rol", {"rolPortal": 2}, "S"),
        ("POST", "/api/usuarios/999999/deshabilitar", None, "S"),
        ("POST", "/api/usuarios/999999/reactivar", {"rolPortal": 2}, "S"),
        ("GET", "/api/auditoria/cambios", None, "S"),
        ("GET", "/api/estadisticas/resumen-general", None, "S"),
        ("GET", "/api/recaudacion", None, "S"),
        ("GET", "/api/cuotas-faltantes", None, "S"),
        ("POST", "/api/cuotas-faltantes/2026/13/generar", None, "S"),
        ("GET", "/api/aranceles/historial", None, "S"),
        ("POST", "/api/aranceles/programar", {}, "S"),
        ("DELETE", "/api/aranceles/999999", None, "S"),
        ("GET", "/api/enrollmentfees/current", None, "S"),
        ("POST", "/api/enrollmentfees", {}, "S"),
        ("POST", "/api/players", {}, "S"),
        ("PUT", "/api/players/%d/discount" % jug, {}, "S"),
        ("POST", "/api/jugadores/999999/baja", {}, "S"),
        ("POST", "/api/jugadores/999999/reactivar", {}, "S"),
        ("GET", "/api/jugadores/%d/historial-pagos" % jug, None, "S"),
        ("POST", "/api/pagos/999999/anular", {}, "S"),
        ("GET", "/api/reportes/aranceles/csv", None, "S"),
        ("GET", "/api/reportes/aranceles/pdf", None, "S"),
        ("GET", "/api/reportes/recaudacion/csv", None, "S"),
        ("GET", "/api/reportes/recaudacion/pdf", None, "S"),
        # Cobranzas: Cuotas y Pagos, Deudas y Morosidad, Becados y Descuentos (y los catalogos que esas pantallas leen).
        ("GET", "/api/pagos/resumen", None, "A"),
        ("GET", "/api/pagos/pendientes", None, "A"),
        ("GET", "/api/pagos/player-accounts", None, "A"),
        ("GET", "/api/pagos/deuda-por-categoria", None, "A"),
        ("GET", "/api/pagos/recientes", None, "A"),
        ("GET", "/api/pagos/resumen-hoy", None, "A"),
        ("GET", "/api/pagos/registrados", None, "A"),
        ("GET", "/api/pagos/deuda/%d" % jug, None, "A"),
        ("GET", "/api/pagos/cuotas/%d" % jug, None, "A"),
        ("GET", "/api/pagos/jugador/%d/estado-de-cuenta" % jug, None, "A"),
        ("POST", "/api/pagos/registrar", {}, "A"),
        ("POST", "/api/pagos/cobro", {}, "A"),
        ("GET", "/api/players/%d/enrollment" % jug, None, "A"),
        ("POST", "/api/players/%d/enrollment/payments" % jug, {}, "A"),
        ("GET", "/api/players/search?q=Qa", None, "A"),
        ("GET", "/api/players/discounts", None, "A"),
        ("GET", "/api/players/discounts/all", None, "A"),
        ("GET", "/api/players/discounts/types", None, "A"),
        ("GET", "/api/players/%d/discount" % jug, None, "A"),
        ("GET", "/api/players/%d/discounts" % jug, None, "A"),
        ("GET", "/api/players/%d/discount/first-period" % jug, None, "A"),
        ("POST", "/api/players/999999/discount", {}, "A"),
        ("DELETE", "/api/players/999999/discount", None, "A"),
        ("POST", "/api/players/999999/discount/1/void", {}, "A"),
        ("GET", "/api/jugadores", None, "A"),
        ("GET", "/api/jugadores/%d" % jug, None, "A"),
        ("GET", "/api/categorias", None, "A"),
        ("GET", "/api/aranceles/resumen", None, "A"),
        ("GET", "/api/enrollmentfees", None, "A"),
        ("GET", "/api/reportes/deudores/csv", None, "A"),
        ("GET", "/api/reportes/deudores/pdf", None, "A"),
        ("GET", "/api/reportes/pagos-recientes/csv", None, "A"),
        ("GET", "/api/reportes/pagos-recientes/pdf", None, "A"),
        ("GET", "/api/reportes/becados/csv", None, "A"),
        ("GET", "/api/reportes/becados/pdf", None, "A"),
    ]


def recorrer_matriz(caso, actor, token, jug, permitido):
    """permitido(alcance) -> True si ese actor deberia pasar la autorizacion."""
    fallas, detalle = [], []
    for metodo, ruta, cuerpo, alcance in matriz(jug):
        estado, respuesta = llamar(metodo, ruta, token, cuerpo)
        esperado_ok = permitido(alcance)
        ok = autorizado(estado) == esperado_ok and (estado == 403 if not esperado_ok else True) and estado < 500
        detalle.append("%s %s -> %d" % (metodo, ruta, estado))
        if not ok:
            fallas.append("%s %s -> %d (%s)" % (metodo, ruta, estado, respuesta))
    total = len(matriz(jug))
    registrar(caso, "token vigente de %s" % actor, actor, "%d endpoints del Portal Administrativo" % total,
              "permitidos responden sin 401/403/5xx; prohibidos 403",
              "OK %d/%d" % (total - len(fallas), total) + ("; fallas: " + "; ".join(fallas) if fallas else ""),
              not fallas, detalle)


# ---------------------------------------------------------------------------------------------------- casos
def main():
    global api, log_api, BASE_DATOS, clave_integracion
    clave_integracion = os.environ.get("Integracion__ClienteDeportivo__Clave", "")
    argumentos = argparse.ArgumentParser()
    argumentos.add_argument("--base", default="http://localhost:5161")
    argumentos.add_argument("--log", required=True)
    argumentos.add_argument("--salida", default="evidencia_hu061.json")
    argumentos.add_argument("--base-datos", default=BASE_DATOS)
    opciones = argumentos.parse_args()
    api, log_api = opciones.base.rstrip("/"), opciones.log
    BASE_DATOS = opciones.base_datos

    ids = {clave: int(escalar("SELECT u.PK_id_usuario FROM dbo.USUARIOS u JOIN dbo.PERSONAS p ON p.PK_id_persona = u.FK_id_persona WHERE p.dni = '%s'" % dni))
           for clave, dni in DNI.items() if clave != "JUG"}
    jug = int(escalar("SELECT j.PK_id_jugador FROM dbo.JUGADORES j JOIN dbo.PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni = '%s'" % DNI["JUG"]))
    sa1, cand, cand2, dep, cand3 = ids["SA1"], ids["CAND"], ids["CAND2"], ids["DEP"], ids["CAND3"]

    # ---------------- ENV
    estado_inicial = {k: usuario(v) for k, v in ids.items()}
    ok = (estado_inicial["SA1"]["acceso"] == 1 and estado_inicial["SA1"]["rol"] == "1"
          and all(estado_inicial[k]["acceso"] == 0 and estado_inicial[k]["rol"] == "NULL" for k in ("CAND", "CAND2", "DEP", "CAND3")))
    registrar("ENV-01", "base recien preparada", "-", "verificar estado inicial de los datos de prueba",
              "SA1 SuperAdmin; CAND, CAND2, CAND3, DEP sin acceso", json.dumps(estado_inicial), ok)
    personas_antes = int(escalar("SELECT COUNT(*) FROM dbo.PERSONAS"))
    usuarios_antes = int(escalar("SELECT COUNT(*) FROM dbo.USUARIOS"))

    estado, cuerpo = login(DNI["SA1"], CONTRASENIA_DEPORTIVA)
    t_sa = cuerpo.get("token") if estado == 200 else None
    cabecera, datos = claims(t_sa)
    registrar("SES-01", "SA1 con contrasena", "SA1", "POST /api/auth/login",
              "200, rol 1, token con tokenVersion", "%d, role=%s, alg=%s, rol_portal=%s, tokenVersion=%s, roles=%s"
              % (estado, cuerpo.get("role"), cabecera.get("alg"), datos.get("rol_portal"), datos.get("tokenVersion"), roles_del_token(t_sa)),
              estado == 200 and cuerpo.get("role") == 1 and datos.get("rol_portal") == "1" and "tokenVersion" in datos)
    registrar("SES-02", "token de SES-01", "-", "algoritmo de firma del JWT (HU-UNI-02)",
              "RS256 (arquitectura objetivo)", "alg=%s (clave simetrica Jwt:Key)" % cabecera.get("alg"),
              cabecera.get("alg") == "RS256",
              estado="BLOCKED" if cabecera.get("alg") != "RS256" else None)
    registrar("SES-03", "respuesta de SES-01", "-", "transporte de la sesion (HU-UNI-03)",
              "cookie HttpOnly emitida por el login", "el token viaja en el cuerpo JSON; sin Set-Cookie; el frontend lo guarda en sessionStorage y lo manda como Bearer",
              False, estado="BLOCKED")

    # ---------------- HAB: habilitacion de una persona del otro portal
    estado, candidatos = llamar("GET", "/api/usuarios/candidatos", t_sa)
    ids_candidatos = sorted(c["idUsuario"] for c in candidatos) if estado == 200 else []
    registrar("HAB-01", "CAND, CAND2 y CAND3 cargados por el Deportivo (FK_id_rol 1/2), sin acceso", "SA1",
              "GET /api/usuarios/candidatos", "incluye CAND, CAND2, CAND3; no incluye a DEP (Director Tecnico) ni a SA1",
              "%d %s" % (estado, ids_candidatos),
              estado == 200 and {cand, cand2, cand3} <= set(ids_candidatos) and dep not in ids_candidatos and sa1 not in ids_candidatos)

    estado, cuerpo = login(DNI["CAND"], CONTRASENIA_DEPORTIVA)
    t_cand_deportivo = cuerpo.get("token") if estado == 200 else None
    registrar("HAB-02", "CAND solo con acceso deportivo", "CAND", "login con su contrasena deportiva",
              "200, role 0 (sin acceso Administrativo)", "%d role=%s" % (estado, (cuerpo or {}).get("role")),
              estado == 200 and cuerpo.get("role") == 0)

    evento_antes = ultimo_evento()
    estado, cuerpo = llamar("POST", "/api/usuarios/%d/habilitar" % cand, t_sa, {"email": "cand@cacc.test", "rolPortal": 2})
    registrar("HAB-03", "CAND en candidatos", "SA1", "POST /api/usuarios/{CAND}/habilitar rol Administrador",
              "200", "%d %s" % (estado, cuerpo), estado == 200)

    despues = usuario(cand)
    personas_despues = int(escalar("SELECT COUNT(*) FROM dbo.PERSONAS"))
    usuarios_despues = int(escalar("SELECT COUNT(*) FROM dbo.USUARIOS"))
    registrar("HAB-04", "HAB-03", "-", "estado en base de CAND",
              "misma PERSONA y USUARIO, acceso_portal 1, rol_portal 2, activacion_pendiente 1; sin filas nuevas",
              "persona %d->%d, acceso=%d, rol=%s, pendiente=%d, PERSONAS %d->%d, USUARIOS %d->%d"
              % (estado_inicial["CAND"]["persona"], despues["persona"], despues["acceso"], despues["rol"], despues["pendiente"],
                 personas_antes, personas_despues, usuarios_antes, usuarios_despues),
              despues["persona"] == estado_inicial["CAND"]["persona"] and despues["acceso"] == 1 and despues["rol"] == "2"
              and despues["pendiente"] == 1 and personas_antes == personas_despues and usuarios_antes == usuarios_despues)

    filas_token = sql("SELECT tipo, CASE WHEN consumido_utc IS NULL THEN 0 ELSE 1 END, DATEDIFF(MINUTE, creado_utc, vence_utc) "
                      "FROM dbo.TOKEN_ACCESO_CUENTA WHERE id_usuario = %d" % cand)
    url, token_activacion = enlace_activacion("cand@cacc.test")
    registrar("HAB-05", "HAB-03", "-", "activacion GENERADA (token en base + enlace)",
              "1 token ACTIVACION sin consumir, 30 min; enlace /crear-contrasena", "tokens=%s; enlace=%s" % (filas_token, url and url.split("?")[0]),
              len(filas_token) == 1 and filas_token[0][0] == "ACTIVACION" and filas_token[0][1] == "0" and token_activacion is not None)
    registrar("HAB-06", "Development con Email:LogResetLink=true, sin SMTP de prueba", "-", "activacion ENVIADA / RECIBIDA",
              "correo entregado al buzon de cand@cacc.test",
              "no verificable: la API escribio el enlace en su log y no intento SMTP; no hay buzon de prueba en este entorno",
              False, estado="BLOCKED")
    candidatos_despues = [c["idUsuario"] for c in llamar("GET", "/api/usuarios/candidatos", t_sa)[1]]
    habilitados = {u["idUsuario"]: u for u in llamar("GET", "/api/usuarios/habilitados", t_sa)[1]}
    registrar("HAB-07", "HAB-03", "SA1", "listas de Usuarios y Permisos",
              "CAND sale de candidatos y aparece una sola vez en habilitados (rol 2, activacion pendiente)",
              "en candidatos=%s; habilitado=%s" % (cand in candidatos_despues, habilitados.get(cand)),
              cand not in candidatos_despues and habilitados.get(cand, {}).get("rolPortal") == 2
              and habilitados[cand].get("activacionPendiente") is True)

    estado, cuerpo = login(DNI["CAND"], CONTRASENIA_DEPORTIVA)
    registrar("HAB-08", "CAND habilitado, activacion pendiente", "CAND", "login con su contrasena deportiva (para usar el Deportivo)",
              "200 role 0: los permisos son independientes, habilitar el Administrativo no le quita el Deportivo",
              "%d %s" % (estado, cuerpo), estado == 200)
    estado_dep, _ = llamar("GET", "/api/integracion/v1/estado-deuda/dni/%s" % DNI["JUG"], t_cand_deportivo)
    registrar("HAB-09", "token deportivo de CAND emitido antes de habilitarlo", "CAND", "seguir usando ese token",
              "sigue autenticando (no se revoca al habilitar); 'Administrativo' (rol 2 deportivo) no puede consultar estado-deuda -> 403",
              "%d" % estado_dep, estado_dep == 403)

    estado, cuerpo = llamar("POST", "/api/auth/account-access/complete", cuerpo={"token": token_activacion, "password": CONTRASENIA_NUEVA})
    tras_activar = usuario(cand)
    registrar("HAB-10", "enlace de HAB-05", "CAND", "POST /api/auth/account-access/complete",
              "200; activacion_pendiente 0; token consumido", "%d %s; pendiente=%d" % (estado, cuerpo, tras_activar["pendiente"]),
              estado == 200 and tras_activar["pendiente"] == 0)
    estado, cuerpo = llamar("POST", "/api/auth/account-access/complete", cuerpo={"token": token_activacion, "password": CONTRASENIA_NUEVA + "x"})
    registrar("HAB-11", "enlace ya usado", "CAND", "reusar el enlace de activacion", "400", "%d" % estado, estado == 400)

    estado, cuerpo = login(DNI["CAND"], CONTRASENIA_NUEVA)
    t_adm = cuerpo.get("token") if estado == 200 else None
    datos = claims(t_adm)[1] if t_adm else {}
    registrar("HAB-12", "CAND activado", "CAND", "login con la contrasena nueva",
              "200, role 2; token con rol_portal 2 y sin claims deportivos (aislamiento del Administrador)",
              "%d role=%s rol_portal=%s roles=%s id_rol=%s" % (estado, (cuerpo or {}).get("role"), datos.get("rol_portal"),
                                                              roles_del_token(t_adm) if t_adm else None, datos.get("id_rol")),
              estado == 200 and cuerpo.get("role") == 2 and roles_del_token(t_adm) == ["2"] and "id_rol" not in datos)
    estado, _ = login(DNI["CAND"], CONTRASENIA_DEPORTIVA)
    registrar("HAB-13", "CAND activado", "CAND", "login con la contrasena deportiva anterior",
              "401: cuenta unica, una sola contrasena (la de la activacion la reemplaza en los dos portales)", "%d" % estado, estado == 401)

    # ---------------- SA / ADM / CRU: matriz de permisos por API
    recorrer_matriz("SA-01", "SA1 (SuperAdmin)", t_sa, jug, lambda alcance: True)
    recorrer_matriz("ADM-01", "CAND (Administrador)", t_adm, jug, lambda alcance: alcance == "A")
    estado, _ = llamar("POST", "/api/integracion/v1/jugadores/%d/cargos-alta" % jug, t_adm)
    registrar("ADM-02", "token de CAND (Administrador)", "CAND", "POST /api/integracion/v1/jugadores/{id}/cargos-alta",
              "403 (el Administrador del Administrativo no usa la integracion)", "%d" % estado, estado == 403)

    estado, cuerpo = login(DNI["DEP"], CONTRASENIA_DEPORTIVA)
    t_dep = cuerpo.get("token") if estado == 200 else None
    registrar("CRU-01", "DEP solo deportivo (Director Tecnico), nunca habilitado", "DEP", "login comun",
              "200 role 0 (sesion valida para el Deportivo; el frontend Administrativo lo manda al Deportivo)",
              "%d role=%s roles=%s" % (estado, (cuerpo or {}).get("role"), roles_del_token(t_dep) if t_dep else None),
              estado == 200 and cuerpo.get("role") == 0 and "1" not in roles_del_token(t_dep) and "2" not in roles_del_token(t_dep))
    recorrer_matriz("CRU-02", "DEP (solo deportivo)", t_dep, jug, lambda alcance: False)
    estado_qr, _ = llamar("GET", "/api/integracion/v1/estado-deuda/dni/%s" % DNI["JUG"], t_dep)
    registrar("CRU-03", "token de DEP", "DEP", "GET estado-deuda (consulta permitida al Deportivo, acotada a sus categorias)",
              "no 401/403 (DEP no tiene categorias: el jugador queda fuera de su alcance -> 404)", "%d" % estado_qr, estado_qr == 404)
    estado, cuerpo = login(DNI["CAND2"], CONTRASENIA_DEPORTIVA)
    t_cand2_deportivo = cuerpo.get("token") if estado == 200 else None
    recorrer_matriz("CRU-04", "CAND2 (Administrador (Tesorero) deportivo, sin habilitar)", t_cand2_deportivo, jug, lambda alcance: False)

    # ---------------- ROL: cambio de rol y politica de sesion
    estado, _ = llamar("GET", "/api/estadisticas/resumen-general", t_adm)
    registrar("ROL-01", "CAND Administrador con sesion abierta", "CAND", "GET resumen-general (solo SuperAdmin)", "403", "%d" % estado, estado == 403)
    v_antes = usuario(cand)["version"]
    estado, cuerpo = llamar("PUT", "/api/usuarios/%d/rol" % cand, t_sa, {"rolPortal": 1})
    v_despues = usuario(cand)
    registrar("ROL-02", "CAND Administrador", "SA1", "PUT /api/usuarios/{CAND}/rol -> SuperAdmin",
              "200; rol_portal 1; token_version +1", "%d; rol=%s; version %d->%d" % (estado, v_despues["rol"], v_antes, v_despues["version"]),
              estado == 200 and v_despues["rol"] == "1" and v_despues["version"] == v_antes + 1)
    estado, _ = llamar("GET", "/api/pagos/resumen", t_adm)
    estado2, _ = llamar("GET", "/api/estadisticas/resumen-general", t_adm)
    registrar("ROL-03", "token de CAND emitido como Administrador", "CAND", "usar la sesion anterior al cambio",
              "401 en todo (la sesion vieja se revoca; no se transforma en SuperAdmin)", "pagos/resumen %d, resumen-general %d" % (estado, estado2),
              estado == 401 and estado2 == 401)
    estado, cuerpo = login(DNI["CAND"], CONTRASENIA_NUEVA)
    t_cand_sa = cuerpo.get("token") if estado == 200 else None
    e1, _ = llamar("GET", "/api/estadisticas/resumen-general", t_cand_sa)
    e2, _ = llamar("GET", "/api/usuarios/habilitados", t_cand_sa)
    registrar("ROL-04", "ROL-02", "CAND", "nueva sesion", "role 1; endpoints de SuperAdmin 200",
              "login %d role=%s; resumen-general %d; usuarios %d" % (estado, (cuerpo or {}).get("role"), e1, e2),
              estado == 200 and cuerpo.get("role") == 1 and e1 == 200 and e2 == 200)
    estado, cuerpo = llamar("PUT", "/api/usuarios/%d/rol" % cand, t_sa, {"rolPortal": 2})
    e_viejo, _ = llamar("GET", "/api/estadisticas/resumen-general", t_cand_sa)
    registrar("ROL-05", "dos SuperAdmin habilitados (SA1 y CAND)", "SA1", "PUT /api/usuarios/{CAND}/rol -> Administrador; usar la sesion de SuperAdmin",
              "200; la sesion de SuperAdmin queda revocada (401)", "cambio %d; sesion vieja %d" % (estado, e_viejo), estado == 200 and e_viejo == 401)
    estado, cuerpo = login(DNI["CAND"], CONTRASENIA_NUEVA)
    t_adm2 = cuerpo.get("token") if estado == 200 else None
    e1, _ = llamar("GET", "/api/estadisticas/resumen-general", t_adm2)
    e2, _ = llamar("GET", "/api/pagos/resumen", t_adm2)
    registrar("ROL-06", "ROL-05", "CAND", "nueva sesion", "role 2; resumen-general 403; pagos 200",
              "login %d role=%s; resumen-general %d; pagos %d" % (estado, (cuerpo or {}).get("role"), e1, e2),
              estado == 200 and cuerpo.get("role") == 2 and e1 == 403 and e2 == 200)

    # ---------------- AUD (operacion de un Administrador)
    evento_adm = ultimo_evento()
    estado, cuerpo = llamar("POST", "/api/players/%d/discount" % jug, t_adm2,
                            {"reason": "Becado", "valueType": "%", "percentage": 50, "startDate": "2026-11-01", "endDate": "2026-12-31"})
    eventos = sql("SELECT entidad, accion, ISNULL(CONVERT(varchar, id_usuario), 'NULL'), ISNULL(email_usuario, 'NULL'), "
                  "ISNULL(dni_usuario, 'NULL'), CONVERT(varchar, fecha_utc, 126) FROM dbo.AUDITORIA_CAMBIOS WHERE PK_id_evento > %d" % evento_adm)
    registrar("AUD-01", "CAND Administrador", "CAND", "POST /api/players/{JUG}/discount (beca 50%)",
              "201; un evento JUGADORES_DESCUENTOS INSERT con el operador CAND (id, correo, DNI) y fecha",
              "%d; eventos=%s" % (estado, eventos),
              estado == 201 and len(eventos) == 1 and eventos[0][0] == "JUGADORES_DESCUENTOS" and eventos[0][1] == "INSERT"
              and eventos[0][2] == str(cand) and eventos[0][3] == "cand@cacc.test" and eventos[0][4] == DNI["CAND"])
    evento_rechazo = ultimo_evento()
    estado, _ = llamar("PUT", "/api/players/%d/discount" % jug, t_adm2,
                       {"reason": "Becado", "valueType": "%", "percentage": 10, "startDate": "2026-11-01", "endDate": "2026-12-31"})
    registrar("AUD-02", "CAND Administrador", "CAND", "PUT discount (editar: solo SuperAdmin)", "403 y ningun evento nuevo",
              "%d; eventos nuevos=%d" % (estado, ultimo_evento() - evento_rechazo), estado == 403 and ultimo_evento() == evento_rechazo)

    # ---------------- DES: deshabilitacion con sesion abierta
    e_antes, _ = llamar("GET", "/api/pagos/resumen", t_adm2)
    v_antes = usuario(cand)["version"]
    estado, cuerpo = llamar("POST", "/api/usuarios/%d/deshabilitar" % cand, t_sa)
    tras = usuario(cand)
    registrar("DES-01", "CAND Administrador con sesion abierta (pagos %d)" % e_antes, "SA1", "POST /api/usuarios/{CAND}/deshabilitar",
              "200; acceso_portal 0; token_version +1", "%d; acceso=%d; version %d->%d" % (estado, tras["acceso"], v_antes, tras["version"]),
              estado == 200 and tras["acceso"] == 0 and tras["version"] == v_antes + 1)
    e1, _ = llamar("GET", "/api/pagos/resumen", t_adm2)
    e2, _ = llamar("POST", "/api/players/%d/discount/1/void" % jug, t_adm2, {})
    registrar("DES-02", "token de CAND anterior a la baja (todavia no vencido)", "CAND", "GET pagos/resumen y POST void con la sesion vieja",
              "401 en las dos", "%d, %d" % (e1, e2), e1 == 401 and e2 == 401)
    estado, cuerpo = login(DNI["CAND"], CONTRASENIA_NUEVA)
    t_baja = cuerpo.get("token") if estado == 200 else None
    e_admin, _ = llamar("GET", "/api/pagos/resumen", t_baja) if t_baja else (None, None)
    registrar("DES-03", "CAND deshabilitado (conserva su rol deportivo)", "CAND", "login y uso del Portal Administrativo",
              "sin sesion Administrativa: o 401, o role 0 con 403 en el Administrativo",
              "login %d role=%s; pagos/resumen %s" % (estado, (cuerpo or {}).get("role"), e_admin),
              estado == 401 or ((cuerpo or {}).get("role") == 0 and e_admin == 403))
    deshabilitados = [u["idUsuario"] for u in llamar("GET", "/api/usuarios/deshabilitados", t_sa)[1]]
    candidatos = [c["idUsuario"] for c in llamar("GET", "/api/usuarios/candidatos", t_sa)[1]]
    registrar("DES-04", "DES-01", "SA1", "listas", "CAND en deshabilitados, no en candidatos",
              "deshabilitados=%s candidatos=%s" % (deshabilitados, candidatos), cand in deshabilitados and cand not in candidatos)

    # ---------------- REH: rehabilitacion
    personas_antes = int(escalar("SELECT COUNT(*) FROM dbo.PERSONAS"))
    usuarios_antes = int(escalar("SELECT COUNT(*) FROM dbo.USUARIOS"))
    rol_previo = usuario(cand)["rol"]
    estado, cuerpo = llamar("POST", "/api/usuarios/%d/reactivar" % cand, t_sa, {"rolPortal": int(rol_previo)})
    tras = usuario(cand)
    registrar("REH-01", "CAND deshabilitado; la pantalla propone el rol que tenia (rolReactivar.set(usuario.rolPortal))", "SA1",
              "POST /api/usuarios/{CAND}/reactivar con el rol propuesto",
              "200; mismo USUARIO y PERSONA; mismo rol (2); sin filas nuevas",
              "%d; persona %d; rol %s->%s; PERSONAS %d->%d; USUARIOS %d->%s" % (estado, tras["persona"], rol_previo, tras["rol"], personas_antes,
                                                                         int(escalar("SELECT COUNT(*) FROM dbo.PERSONAS")), usuarios_antes,
                                                                         escalar("SELECT COUNT(*) FROM dbo.USUARIOS")),
              estado == 200 and tras["rol"] == "2" and tras["persona"] == estado_inicial["CAND"]["persona"]
              and int(escalar("SELECT COUNT(*) FROM dbo.USUARIOS")) == usuarios_antes)
    e_viejo, _ = llamar("GET", "/api/pagos/resumen", t_adm2)
    registrar("REH-02", "token revocado en DES-01", "CAND", "usar la sesion vieja despues de reactivar", "401 (no revive)", "%d" % e_viejo, e_viejo == 401)
    estado, cuerpo = login(DNI["CAND"], CONTRASENIA_NUEVA)
    t_adm3 = cuerpo.get("token") if estado == 200 else None
    e1, _ = llamar("GET", "/api/pagos/resumen", t_adm3)
    e2, _ = llamar("GET", "/api/estadisticas/resumen-general", t_adm3)
    registrar("REH-03", "REH-01", "CAND", "login con la misma contrasena y sesion nueva", "200 role 2; pagos 200; resumen-general 403",
              "%d role=%s; %d; %d" % (estado, (cuerpo or {}).get("role"), e1, e2), estado == 200 and cuerpo.get("role") == 2 and e1 == 200 and e2 == 403)

    # ---------------- LAST: ultimo SuperAdmin
    antes = usuario(sa1)
    evento_antes = ultimo_evento()
    e1, c1 = llamar("PUT", "/api/usuarios/%d/rol" % sa1, t_sa, {"rolPortal": 2})
    e2, c2 = llamar("POST", "/api/usuarios/%d/deshabilitar" % sa1, t_sa)
    despues = usuario(sa1)
    registrar("LAST-01", "SA1 unico SuperAdmin habilitado", "SA1", "PUT rol propio -> Administrador; POST deshabilitar propio",
              "409 en los dos; SA1 sin cambios en base; sin eventos de auditoria", "rol %d, deshabilitar %d; antes=%s despues=%s; eventos nuevos=%d"
              % (e1, e2, antes, despues, ultimo_evento() - evento_antes),
              e1 == 409 and e2 == 409 and antes == despues and ultimo_evento() == evento_antes)
    e_sa, _ = llamar("GET", "/api/usuarios/habilitados", t_sa)
    registrar("LAST-02", "LAST-01", "SA1", "la sesion de SA1 sigue funcionando", "200", "%d" % e_sa, e_sa == 200)

    estado, _ = llamar("POST", "/api/usuarios/%d/habilitar" % cand2, t_sa, {"email": "cand2@cacc.test", "rolPortal": 1})
    _, token2 = enlace_activacion("cand2@cacc.test")
    e_act, _ = llamar("POST", "/api/auth/account-access/complete", cuerpo={"token": token2, "password": CONTRASENIA_NUEVA})
    e_log, cuerpo = login(DNI["CAND2"], CONTRASENIA_NUEVA)
    t_sa2 = cuerpo.get("token") if e_log == 200 else None
    registrar("LAST-03", "CAND2 candidato", "SA1", "habilitar CAND2 como SuperAdmin y activarlo", "200, 200, login role 1",
              "%d, %d, %d role=%s" % (estado, e_act, e_log, (cuerpo or {}).get("role")),
              estado == 200 and e_act == 200 and e_log == 200 and cuerpo.get("role") == 1)
    e1, _ = llamar("PUT", "/api/usuarios/%d/rol" % cand2, t_sa, {"rolPortal": 2})
    e2, _ = llamar("PUT", "/api/usuarios/%d/rol" % cand2, t_sa, {"rolPortal": 1})
    e3, _ = llamar("POST", "/api/usuarios/%d/deshabilitar" % cand2, t_sa)
    registrar("LAST-04", "dos SuperAdmin activos (SA1 y CAND2)", "SA1", "bajar CAND2 a Administrador, subirlo y deshabilitarlo",
              "200, 200, 200", "%d, %d, %d" % (e1, e2, e3), e1 == 200 and e2 == 200 and e3 == 200)

    # SA1 vuelve a ser el unico SuperAdmin operativo. CAND3 se habilita como SuperAdmin pero NO activa su cuenta.
    estado, _ = llamar("POST", "/api/usuarios/%d/habilitar" % cand3, t_sa, {"email": "cand3@cacc.test", "rolPortal": 1})
    pendiente = usuario(cand3)
    antes = usuario(sa1)
    e1, c1 = llamar("PUT", "/api/usuarios/%d/rol" % sa1, t_sa, {"rolPortal": 2})
    despues = usuario(sa1)
    operativos = int(escalar("SELECT COUNT(*) FROM dbo.USUARIOS WHERE rol_portal = 1 AND acceso_portal = 1 AND activo = 1 AND activacion_pendiente = 0"))
    e_rec, _ = llamar("POST", "/api/auth/password-reset/request", cuerpo={"usuario": DNI["CAND3"]})
    tokens_rec = escalar("SELECT COUNT(*) FROM dbo.TOKEN_ACCESO_CUENTA WHERE id_usuario = %d AND tipo = 'RECUPERACION'" % cand3)
    registrar("LAST-05", "SA1 unico SuperAdmin operativo; CAND3 SuperAdmin con activacion pendiente (habilitar %d, pendiente=%d)"
              % (estado, pendiente["pendiente"]), "SA1",
              "PUT rol propio -> Administrador",
              "409: un SuperAdmin que no activo su cuenta no puede iniciar sesion, no cuenta como el SuperAdmin que queda",
              "%d %s; SA1 rol %s->%s; SuperAdmin operativos=%d; recuperacion publica de CAND3: %d, tokens RECUPERACION=%s"
              % (e1, c1, antes["rol"], despues["rol"], operativos, e_rec, tokens_rec),
              e1 == 409)
    if e1 == 200:
        # Restaura el escenario: CAND3 activa (su enlace sigue vigente 30 minutos) y le devuelve el rol a SA1.
        _, token3 = enlace_activacion("cand3@cacc.test")
        e_act, _ = llamar("POST", "/api/auth/account-access/complete", cuerpo={"token": token3, "password": CONTRASENIA_NUEVA})
        e_log, cuerpo = login(DNI["CAND3"], CONTRASENIA_NUEVA)
        t_sa3 = cuerpo.get("token") if e_log == 200 else None
        e_res, _ = llamar("PUT", "/api/usuarios/%d/rol" % sa1, t_sa3, {"rolPortal": 1})
        estado_sa, cuerpo = login(DNI["SA1"], CONTRASENIA_DEPORTIVA)
        t_sa = cuerpo.get("token") if estado_sa == 200 else t_sa
        registrar("LAST-06", "LAST-05 dejo a SA1 como Administrador", "CAND3", "activar CAND3 a tiempo y devolverle el rol a SA1",
                  "restauracion posible solo mientras el enlace de 30 minutos siga vigente",
                  "activar %d; login %d; devolver rol %d; SA1 role=%s" % (e_act, e_log, e_res, (cuerpo or {}).get("role")),
                  e_res == 200 and (cuerpo or {}).get("role") == 1)

    # ---------------- AUD: eventos de las operaciones sobre la cuenta CAND
    eventos = sql("SELECT PK_id_evento, accion, ISNULL(CONVERT(varchar, id_usuario), 'NULL'), ISNULL(email_usuario, 'NULL'), "
                  "CONVERT(varchar, fecha_utc, 126), JSON_VALUE(datos_antes, '$.accesoPortal'), JSON_VALUE(datos_despues, '$.accesoPortal'), "
                  "ISNULL(JSON_VALUE(datos_antes, '$.rolPortal'), 'NULL'), ISNULL(JSON_VALUE(datos_despues, '$.rolPortal'), 'NULL'), "
                  "JSON_VALUE(datos_despues, '$.credencialActualizada') "
                  "FROM dbo.AUDITORIA_CAMBIOS WHERE entidad = 'USUARIOS' AND id_entidad = '%d' AND PK_id_evento > 0 ORDER BY PK_id_evento" % cand)
    # Seed (INSERT sin operador) + habilitar + activar + rol 1 + rol 2 + deshabilitar + reactivar.
    esperados = [("INSERT", "NULL"), ("UPDATE", str(sa1)), ("UPDATE", str(cand)), ("UPDATE", str(sa1)), ("UPDATE", str(sa1)),
                 ("UPDATE", str(sa1)), ("UPDATE", str(sa1))]
    reales = [(e[1], e[2]) for e in eventos]
    registrar("AUD-03", "operaciones HAB-03, HAB-10, ROL-02, ROL-05, DES-01, REH-01 sobre CAND", "-",
              "AUDITORIA_CAMBIOS de USUARIOS/CAND", "un evento por operacion, con el operador correcto y fecha; sin duplicados",
              "%d eventos: %s" % (len(eventos), [" ".join(e) for e in eventos]), reales == esperados, eventos)
    estado, cuerpo = llamar("GET", "/api/auditoria/cambios?entidad=USUARIOS&tamanoPagina=100", t_sa)
    items = (cuerpo or {}).get("items", []) if isinstance(cuerpo, dict) else []
    muestra = items[0] if items else None
    registrar("AUD-04", "AUD-03", "SA1", "GET /api/auditoria/cambios?entidad=USUARIOS (HU-041)",
              "200; los eventos se consultan con operador, fecha, accion y entidad", "%d; total=%s; ejemplo=%s"
              % (estado, (cuerpo or {}).get("total") if isinstance(cuerpo, dict) else None, json.dumps(muestra, ensure_ascii=False)[:400]),
              estado == 200 and len(items) > 0)

    # ---------------- SES: logout (HU-UNI-05 / HU-077)
    estado, _ = llamar("POST", "/api/auth/logout", t_adm3)
    e_post, _ = llamar("GET", "/api/pagos/resumen", t_adm3)
    registrar("SES-04", "sesion de CAND (REH-03)", "CAND", "POST /api/auth/logout y reusar el token", "204; despues 401", "%d; %d" % (estado, e_post),
              estado == 204 and e_post == 401)

    with open(opciones.salida, "w", encoding="utf-8") as archivo:
        json.dump(resultados, archivo, ensure_ascii=False, indent=2)
    resumen = {e: sum(1 for r in resultados if r["estado"] == e) for e in ("PASS", "FAIL", "BLOCKED")}
    print("\nResumen:", resumen, "->", opciones.salida)
    return 0 if resumen["FAIL"] == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
