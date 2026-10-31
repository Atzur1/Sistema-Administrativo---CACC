// QA de HU-106 en el navegador: el resumen de impacto real (API + base de prueba) en escritorio, tablet y celular, y la
// confirmación con teclado. Chrome headless (el instalado, perfil temporal) por CDP, sin dependencias.
// Uso (ng serve en :4200, API en :5118): CACC_CLAVE=... node qa/HU-106/qa_navegador_hu106.mjs <carpeta-salida>
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const SALIDA = process.argv[2];
const BASE = 'http://localhost:4200';
const CLAVE = process.env.CACC_CLAVE;
fs.mkdirSync(SALIDA, { recursive: true });
const espera = (ms) => new Promise((r) => setTimeout(r, ms));

const perfil = fs.mkdtempSync(path.join(os.tmpdir(), 'cacc-qa106-'));
const chrome = spawn('C:/Program Files/Google/Chrome/Application/chrome.exe', [
  '--headless=new', '--remote-debugging-port=9334', `--user-data-dir=${perfil}`, '--no-first-run',
  '--no-default-browser-check', '--disable-extensions', '--window-size=1440,900', 'about:blank',
]);

let ws; let siguiente = 0; const pendientes = new Map();
async function conectar() {
  for (let i = 0; i < 50; i++) {
    try {
      const pagina = (await (await fetch('http://127.0.0.1:9334/json/list')).json()).find((t) => t.type === 'page');
      if (pagina) { ws = new WebSocket(pagina.webSocketDebuggerUrl); break; }
    } catch { /* arrancando */ }
    await espera(200);
  }
  await new Promise((r) => ws.addEventListener('open', r, { once: true }));
  ws.addEventListener('message', (e) => {
    const m = JSON.parse(e.data);
    if (m.id && pendientes.has(m.id)) { pendientes.get(m.id)(m); pendientes.delete(m.id); }
    if (m.method === 'Page.javascriptDialogOpening') enviar('Page.handleJavaScriptDialog', { accept: true });
  });
}
function enviar(method, params = {}) {
  const id = ++siguiente;
  ws.send(JSON.stringify({ id, method, params }));
  return new Promise((r) => pendientes.set(id, r));
}
async function js(expr) {
  const r = await enviar('Runtime.evaluate', { expression: expr, returnByValue: true, awaitPromise: true });
  if (r.result?.exceptionDetails) throw new Error(`JS: ${r.result.exceptionDetails.exception?.description ?? expr}`);
  return r.result?.result?.value;
}
async function hasta(expr, ms = 15000) {
  const fin = Date.now() + ms;
  while (Date.now() < fin) { if (await js(`!!(${expr})`)) return true; await espera(100); }
  return false;
}
async function viewport(width, height, mobile = false) {
  await enviar('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 1, mobile });
  await espera(400);
}
async function tecla(key, code, keyCode) {
  const b = { key, code, windowsVirtualKeyCode: keyCode, nativeVirtualKeyCode: keyCode };
  await enviar('Input.dispatchKeyEvent', { type: 'rawKeyDown', ...b });
  await enviar('Input.dispatchKeyEvent', { type: 'keyUp', ...b });
}
const escribir = (texto) => enviar('Input.insertText', { text: texto });
async function captura(nombre) {
  const r = await enviar('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
  fs.writeFileSync(path.join(SALIDA, `${nombre}.png`), Buffer.from(r.result.data, 'base64'));
}

const resultados = [];
function registrar(id, caso, ok, obtenido) {
  resultados.push({ id, caso, estado: ok ? 'PASS' : 'FAIL', obtenido });
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${id.padEnd(8)} ${caso} | ${typeof obtenido === 'string' ? obtenido : JSON.stringify(obtenido)}`);
}

async function login(usuario) {
  await enviar('Page.navigate', { url: `${BASE}/` });
  await hasta(`document.querySelector('input[formcontrolname="username"]')`);
  await js(`(() => {
    const set = (s, v) => { const el = document.querySelector(s); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(el, v); el.dispatchEvent(new Event('input', { bubbles: true })); };
    set('input[formcontrolname="username"]', ${JSON.stringify(usuario)});
    set('input[formcontrolname="password"]', ${JSON.stringify(CLAVE)});
    document.querySelector('form button[type="submit"]').click();
  })()`);
  await hasta(`location.pathname !== '/'`);
  if (await js(`location.pathname === '/portales'`)) {
    await hasta(`document.querySelector('.card-portal-administrativo .btn-portal')`);
    await js(`document.querySelector('.card-portal-administrativo .btn-portal').click()`);
  }
  await hasta(`location.pathname.startsWith('/admin/portal/')`);
}

// Carga el form del componente real y pide el impacto (llama a la API de verdad, como el botón "Revisar impacto").
async function revisarImpacto(target, modo, monto, desde) {
  await js(`(async () => {
    const comp = ng.getComponent(document.querySelector('app-actualizacion-aranceles'));
    comp.setIncreaseMode(${JSON.stringify(modo)});
    comp.feeForm.setValue({ target: ${JSON.stringify(target)}, amount: ${JSON.stringify(monto)}, validFrom: ${JSON.stringify(desde)} });
    ng.applyChanges(comp);
    document.querySelector('.submit-button').click();
  })()`);
  return hasta(`document.querySelector('app-ventana .ventana')`, 10000);
}

const ESTADO = `(() => {
  const v = document.querySelector('app-ventana .ventana');
  if (!v) return null;
  const t = (e) => (e?.textContent ?? '').replace(/\\s+/g, ' ').trim();
  const confirmar = v.querySelector('.ventana-confirmar');
  const filas = [...v.querySelectorAll('.impacto-tabla tbody tr')];
  const r = v.getBoundingClientRect();
  return {
    titulo: t(v.querySelector('.ventana-titulo')), subtitulo: t(v.querySelector('.ventana-subtitulo')),
    datos: [...v.querySelectorAll('.impacto-dato')].map((d) => t(d.querySelector('dt')) + ': ' + t(d.querySelector('dd'))),
    filas: filas.length, primeraFila: t(filas[0]), total: t(v.querySelector('.impacto-tabla tfoot')),
    notas: [...v.querySelectorAll('.impacto-notas li, .impacto-cero')].map(t),
    confirmacion: t(v.querySelector('.impacto-confirmacion label')), confirmarHabilitado: confirmar ? !confirmar.disabled : null,
    foco: document.activeElement ? (document.activeElement.className || document.activeElement.tagName) : null,
    focoAdentro: v.contains(document.activeElement),
    ventanaDentro: r.left >= -0.5 && r.right <= innerWidth + 0.5, scrollHorizontal: document.documentElement.scrollWidth > innerWidth,
    theadVisible: (() => { const h = v.querySelector('.impacto-tabla thead'); return h ? h.getBoundingClientRect().height > 2 : null; })(),
    etiquetasCelda: (() => { const c = v.querySelector('.impacto-tabla tbody td'); return c ? getComputedStyle(c, '::before').content : null; })(),
    error: t(v.querySelector('.ventana-error')),
  };
})()`;

try {
  await conectar();
  await enviar('Page.enable'); await enviar('Runtime.enable');
  await viewport(1440, 900);
  await login('20111222');
  await enviar('Page.navigate', { url: `${BASE}/admin/portal/actualizacion-aranceles` });
  await hasta(`document.querySelector('app-actualizacion-aranceles .submit-button') && window.ng`);
  await espera(800);
  const arancelesAntes = await js(`(async () => (await (await fetch('/api/aranceles/historial', { headers: { Authorization: 'Bearer ' + JSON.parse(sessionStorage.getItem(Object.keys(sessionStorage).find((k) => sessionStorage.getItem(k)?.includes('token')))).token } })).json()).length)()`);

  // Varias categorías: Masculino +10 % desde enero.
  const abierto = await revisarImpacto('genero:Masculino', 'percentage', '10', '2027-01-01');
  let e = await js(ESTADO);
  registrar('UI-1', 'Revisar impacto abre el resumen con los números del backend', abierto && e?.filas > 1 && e.datos.some((d) => d.startsWith('Jugadores afectados')), { subtitulo: e?.subtitulo, datos: e?.datos, filas: e?.filas });
  registrar('A11Y-1', 'Al abrir, el foco queda dentro del resumen', !!e?.focoAdentro, e?.foco);
  registrar('CONF-1', 'Varias categorías: Confirmar deshabilitado hasta escribir la cantidad', e?.confirmarHabilitado === false && /Escribí \d+ para confirmar/.test(e?.confirmacion ?? ''), e?.confirmacion);
  const jugadores = Number((e.confirmacion.match(/Escribí (\d+)/) ?? [])[1]);
  await captura('escritorio_varias_categorias');

  // Teclado: llegar al campo con Tab, escribir mal, corregir.
  await js(`document.querySelector('#impacto-confirmacion').focus()`);
  await escribir(String(jugadores - 1));
  await espera(250); // eventCoalescing: la vista se actualiza en el cuadro siguiente
  e = await js(ESTADO);
  const conError = e.confirmarHabilitado;
  await js(`(() => { const i = document.querySelector('#impacto-confirmacion'); i.select(); })()`);
  await escribir(String(jugadores));
  await espera(250);
  e = await js(ESTADO);
  const valores = await js(`(() => { const c = ng.getComponent(document.querySelector('app-actualizacion-aranceles')); return { input: document.querySelector('#impacto-confirmacion').value, escrito: c.confirmacionEscrita, jugadores: c.impacto?.jugadores, valida: c.confirmacionValida }; })()`);
  registrar('CONF-2', `Con teclado: ${jugadores - 1} lo deja deshabilitado, ${jugadores} lo habilita`, conError === false && e.confirmarHabilitado === true, { antes: conError, despues: e.confirmarHabilitado, ...valores });
  await tecla('Tab', 'Tab', 9);
  const siguienteFoco = await js(`document.activeElement?.textContent?.trim()`);
  registrar('A11Y-2', 'Tab sigue dentro del resumen (Volver / Confirmar)', ['Volver', 'Confirmar actualización'].includes(siguienteFoco), siguienteFoco);

  // Responsive del mismo resumen.
  for (const [w, h, nombre] of [[1024, 768, 'notebook'], [768, 1024, 'tablet'], [375, 812, 'celular']]) {
    await viewport(w, h, w < 600);
    e = await js(ESTADO);
    const tablaOk = w <= 640 ? (e.theadVisible === false && /Categoría|Género/.test(e.etiquetasCelda ?? '')) : e.theadVisible === true;
    registrar('RESP', `${nombre} ${w}x${h}: el resumen entra en la pantalla, sin scroll horizontal y con todos los datos`, e.ventanaDentro && !e.scrollHorizontal && e.datos.length === 7 && e.datos.some((d) => d.startsWith("Rige desde")) && tablaOk, { filas: e.filas, thead: e.theadVisible, etiqueta: e.etiquetasCelda });
    await captura(`${nombre}_varias_categorias`);
  }
  await viewport(1440, 900);

  // Esc: cierra sin grabar.
  await tecla('Escape', 'Escape', 27);
  await espera(300);
  const cerrado = await js(`!document.querySelector('app-ventana .ventana')`);
  const arancelesDespues = await js(`(async () => (await (await fetch('/api/aranceles/historial', { headers: { Authorization: 'Bearer ' + JSON.parse(sessionStorage.getItem(Object.keys(sessionStorage).find((k) => sessionStorage.getItem(k)?.includes('token')))).token } })).json()).length)()`);
  registrar('CANC-1', 'Esc cierra el resumen y no se programa nada', cerrado && arancelesDespues === arancelesAntes, `${arancelesAntes} -> ${arancelesDespues} aranceles`);

  // Cero jugadores (una categoría): sin confirmación reforzada.
  const categoriaVacia = await js(`ng.getComponent(document.querySelector('app-actualizacion-aranceles')).targets.find((t) => t.label.includes('HU106 sin jugadores'))?.value`);
  await revisarImpacto(categoriaVacia, 'absolute', '50000', '2027-01-01');
  e = await js(ESTADO);
  registrar('UI-2', 'Cero jugadores: lo dice claramente, sin desglose ni dinero inventado, y confirma con el botón', e?.notas.some((n) => n.startsWith('Jugadores afectados: 0')) && e.filas === 0 && e.confirmarHabilitado === true && !e.confirmacion, { datos: e?.datos, notas: e?.notas });
  await captura('escritorio_cero_jugadores');
  await tecla('Escape', 'Escape', 27);

  // Cambiar el form después de simular: el resumen se descarta.
  await revisarImpacto('genero:Masculino', 'percentage', '10', '2027-01-01');
  await js(`(() => { const c = ng.getComponent(document.querySelector('app-actualizacion-aranceles')); c.feeForm.patchValue({ amount: '11' }); ng.applyChanges(c); })()`);
  registrar('INV-1', 'Cambiar un dato del cambio descarta el resumen (hay que volver a revisarlo)', await js(`!document.querySelector('app-ventana .ventana')`), 'cerrado');

  fs.writeFileSync(path.join(SALIDA, 'qa_navegador_hu106.json'), JSON.stringify(resultados, null, 2));
  const fallas = resultados.filter((r) => r.estado === 'FAIL').length;
  console.log(`\nTotal: ${resultados.length - fallas} PASS | ${fallas} FAIL`);
  process.exitCode = fallas ? 1 : 0;
} catch (err) {
  console.error('ERROR', err);
  process.exitCode = 2;
} finally {
  try { ws?.close(); } catch {}
  chrome.kill();
  await espera(500);
  try { fs.rmSync(perfil, { recursive: true, force: true }); } catch {}
}
