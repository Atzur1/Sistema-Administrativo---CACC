// QA de HU-107 en el navegador: la pantalla real de Recaudación Mensual (API + base de prueba) con fecha, paginación y
// orden en el servidor, métricas que no cambian al paginar, otra zona horaria en el navegador, teclado y celular.
// Chrome headless (el instalado, perfil temporal) por CDP, sin dependencias.
// Uso (ng serve en :4200, API en :5118): CACC_CLAVE=... CACC_USUARIO=... node qa/HU-107/qa_navegador_hu107.mjs <carpeta-salida>
// Lo lanza qa_hu107.ps1 -Navegador, que genera la clave de la corrida.
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const SALIDA = process.argv[2];
const BASE = 'http://localhost:4200';
const CLAVE = process.env.CACC_CLAVE;
const USUARIO = process.env.CACC_USUARIO;
fs.mkdirSync(SALIDA, { recursive: true });
const espera = (ms) => new Promise((r) => setTimeout(r, ms));

const perfil = fs.mkdtempSync(path.join(os.tmpdir(), 'cacc-qa107-'));
const chrome = spawn('C:/Program Files/Google/Chrome/Application/chrome.exe', [
  '--headless=new', '--remote-debugging-port=9335', `--user-data-dir=${perfil}`, '--no-first-run',
  '--no-default-browser-check', '--disable-extensions', '--window-size=1440,900', 'about:blank',
]);

let ws; let siguiente = 0; const pendientes = new Map();
async function conectar() {
  for (let i = 0; i < 50; i++) {
    try {
      const pagina = (await (await fetch('http://127.0.0.1:9335/json/list')).json()).find((t) => t.type === 'page');
      if (pagina) { ws = new WebSocket(pagina.webSocketDebuggerUrl); break; }
    } catch { /* arrancando */ }
    await espera(200);
  }
  await new Promise((r) => ws.addEventListener('open', r, { once: true }));
  ws.addEventListener('message', (e) => {
    const m = JSON.parse(e.data);
    if (m.id && pendientes.has(m.id)) { pendientes.get(m.id)(m); pendientes.delete(m.id); }
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
  await enviar('Input.dispatchKeyEvent', { type: 'keyDown', text: key === 'Enter' ? '\r' : undefined, ...b });
  await enviar('Input.dispatchKeyEvent', { type: 'keyUp', ...b });
}
async function captura(nombre) {
  const r = await enviar('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
  fs.writeFileSync(path.join(SALIDA, `${nombre}.png`), Buffer.from(r.result.data, 'base64'));
}

const resultados = [];
// Si algo queda esperando una respuesta de Chrome que no llega, se corta con lo que haya.
const vigia = setTimeout(() => { console.log('FAIL  NAV-ERR  El recorrido terminó a tiempo | vigía: más de 4 minutos'); fs.writeFileSync(path.join(SALIDA, 'resultado_navegador.json'), JSON.stringify(resultados, null, 2)); chrome.kill(); process.exit(1); }, 240000);
function registrar(id, caso, ok, obtenido) {
  resultados.push({ id, caso, estado: ok ? 'PASS' : 'FAIL', obtenido });
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${id.padEnd(8)} ${caso} | ${typeof obtenido === 'string' ? obtenido : JSON.stringify(obtenido)}`);
}

async function login() {
  await enviar('Page.navigate', { url: `${BASE}/` });
  await hasta(`document.querySelector('input[formcontrolname="username"]')`);
  await js(`(() => {
    const set = (s, v) => { const el = document.querySelector(s); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(el, v); el.dispatchEvent(new Event('input', { bubbles: true })); };
    set('input[formcontrolname="username"]', ${JSON.stringify(USUARIO)});
    set('input[formcontrolname="password"]', ${JSON.stringify(CLAVE)});
    document.querySelector('form button[type="submit"]').click();
  })()`);
  if (!(await hasta(`location.pathname !== '/'`))) {
    const aviso = await js(`[...document.querySelectorAll('.error, .alert, [role=alert], .mensaje-error, small, .login-error')].map((e) => e.textContent.trim()).filter(Boolean).join(' | ')`);
    const boton = await js(`(() => { const b = document.querySelector('form button[type="submit"]'); return b ? { texto: b.textContent.trim(), deshabilitado: b.disabled } : null; })()`);
    throw new Error(`El login no avanzó: ${JSON.stringify({ aviso, boton })}`);
  }
  if (await js(`location.pathname === '/portales'`)) {
    await hasta(`document.querySelector('.card-portal-administrativo .btn-portal')`);
    await js(`document.querySelector('.card-portal-administrativo .btn-portal').click()`);
  }
  await hasta(`location.pathname.startsWith('/admin/portal/')`);
}

const COMP = `ng.getComponent(document.querySelector('app-recaudacion-mensual'))`;
// Estado visible de la pantalla, leído del DOM (lo que ve la persona).
const ESTADO = `(() => {
  const t = (e) => (e?.textContent ?? '').replace(/\\s+/g, ' ').trim();
  const filas = [...document.querySelectorAll('.tabla tbody tr')].map((tr) => [...tr.querySelectorAll('td')].map(t));
  const ths = [...document.querySelectorAll('.tabla thead th')];
  return {
    conteo: t(document.querySelector('.tabla-conteo')),
    estadoPaginas: t(document.querySelector('.pagination-status')),
    encabezados: ths.map((th) => t(th).replace(/[↓↑↕]/g, '').trim()),
    ariaSort: ths.map((th) => th.getAttribute('aria-sort')),
    filas,
    tarjetas: t(document.querySelector('.cards')),
    header: t(document.querySelector('.header-metrics')),
    donut: document.querySelector('.donut-arc')?.getAttribute('stroke-dasharray') ?? null,
    leyenda: t(document.querySelector('.desglose-legend')),
    vacio: t(document.querySelector('.panel-pagos app-estado-vacio')),
    hayPaginador: !!document.querySelector('.pagination'),
    anchoPagina: document.documentElement.scrollWidth, anchoVentana: innerWidth,
  };
})()`;
const estado = () => js(ESTADO);
const listo = () => hasta(`!${COMP}.cargando && !${COMP}.cargandoPagos`);
const peticiones = () => js(`performance.getEntriesByType('resource').map((e) => e.name).filter((n) => n.includes('/api/'))`);

async function elegirPeriodo(mes, anio = 2026) {
  await js(`(() => { const c = ${COMP}; c.onAnioChange(${anio}); c.onMesChange(${JSON.stringify(mes)}); })()`);
  await espera(150);
  await listo();
  await espera(150);
}
async function clicEn(selector, texto) {
  await js(`[...document.querySelectorAll(${JSON.stringify(selector)})].find((b) => b.textContent.includes(${JSON.stringify(texto)})).click()`);
  await espera(150);
  await listo();
  await espera(150);
}

const FECHA = /^\d\d\/\d\d\/\d{4}( \d\d:\d\d)?$/;
const monto = (texto) => Number(texto.replace(/[^\d]/g, ''));

try {
  await conectar();
  console.log(`· conectado a Chrome (usuario ${USUARIO ?? 'sin definir'}, clave ${CLAVE ? `definida, ${CLAVE.length} caracteres` : 'SIN DEFINIR'})`);
  await enviar('Page.enable');
  await enviar('Runtime.enable');
  await viewport(1440, 900);
  await login();
  await enviar('Page.navigate', { url: `${BASE}/admin/portal/recaudacion-mensual` });
  await hasta(`document.querySelector('app-recaudacion-mensual')`);
  await listo();
  await elegirPeriodo('Septiembre');
  console.log('· septiembre cargado');

  // NAV-1: fecha y total real
  let e = await estado();
  registrar('NAV-1', 'Septiembre: 10 filas, columna Fecha primera en dd/MM/yyyy HH:mm, "126 pagos" y "Página 1 de 13"',
    e.filas.length === 10 && e.encabezados[0] === 'Fecha' && e.filas.every((f) => FECHA.test(f[0])) &&
    e.conteo.includes('126 pagos en Septiembre 2026') && e.estadoPaginas === 'Página 1 de 13',
    { conteo: e.conteo, paginas: e.estadoPaginas, encabezados: e.encabezados, primera: e.filas[0] });
  // El número del donut se anima (~800 ms): se lee terminada la animación.
  await espera(1500);
  const donut = await js(`({ centro: document.querySelector('.donut-value')?.textContent.trim(), header: document.querySelectorAll('.header-metric-value')[1]?.textContent.trim() })`);
  registrar('NAV-1b', 'El donut muestra los pagos de TODO el período (126), igual que el encabezado',
    donut.centro === '126' && donut.header === '126', donut);
  await viewport(1440, 1300);
  await captura('escritorio_septiembre_p1');
  await viewport(1440, 900);

  // NAV-2: cambiar de página no toca tarjetas ni donut, ni vuelve a pedir los totales
  const antes = e;
  await js(`performance.clearResourceTimings()`);
  await clicEn('.pagination-button', 'Siguiente');
  e = await estado();
  const pedidas = await peticiones();
  registrar('NAV-2', 'Siguiente: página 2, otras filas; tarjetas, encabezado y donut idénticos; solo se pidió /pagos',
    e.estadoPaginas === 'Página 2 de 13' && JSON.stringify(e.filas) !== JSON.stringify(antes.filas) &&
    e.tarjetas === antes.tarjetas && e.header === antes.header && e.donut === antes.donut && e.leyenda === antes.leyenda &&
    pedidas.length === 1 && pedidas[0].includes('/api/recaudacion/pagos?') && pedidas[0].includes('page=2'),
    { paginas: e.estadoPaginas, pedidas, donut: e.donut, tarjetasIguales: e.tarjetas === antes.tarjetas });

  // NAV-3: recorrer todas las páginas desde la pantalla: 126 pagos, ninguno repetido
  const ids = new Set(); let filasVistas = 0;
  await js(`${COMP}.irAPagina(1)`); await listo();
  for (let n = 1; n <= 13; n++) {
    const pagina = await js(`${COMP}.filas.map((f) => f.idPago)`);
    filasVistas += pagina.length; pagina.forEach((id) => ids.add(id));
    if (n < 13) await clicEn('.pagination-button', 'Siguiente');
  }
  e = await estado();
  registrar('NAV-3', 'Recorrer las 13 páginas con "Siguiente": 126 filas, 126 distintas; en la última "Siguiente" se deshabilita',
    filasVistas === 126 && ids.size === 126 && e.estadoPaginas === 'Página 13 de 13' &&
    (await js(`[...document.querySelectorAll('.pagination-button')].find((b) => b.textContent.includes('Siguiente')).disabled`)),
    { filasVistas, distintas: ids.size, ultima: e.filas.length });

  // NAV-4: ordenar por fecha (asc) vuelve a la página 1 y muestra lo más antiguo primero
  await clicEn('.orden-boton', 'Fecha');
  e = await estado();
  registrar('NAV-4', 'Clic en Fecha: ascendente, página 1, empieza el 01/09',
    e.ariaSort[0] === 'ascending' && e.estadoPaginas === 'Página 1 de 13' && e.filas[0][0].startsWith('01/09/2026'),
    { ariaSort: e.ariaSort[0], paginas: e.estadoPaginas, primeras: e.filas.slice(0, 4).map((f) => f[0]) });
  await captura('escritorio_fecha_asc');

  // NAV-5: ordenar por monto, numérico
  await clicEn('.orden-boton', 'Monto');
  e = await estado();
  const desc = e.filas.map((f) => monto(f[5]));
  await clicEn('.orden-boton', 'Monto');
  const e2 = await estado();
  const asc = e2.filas.map((f) => monto(f[5]));
  registrar('NAV-5', 'Clic en Monto: $100.000 primero; otro clic: $9.000 primero (orden numérico, no de texto)',
    desc[0] === 100000 && desc.every((v, i) => i === 0 || desc[i - 1] >= v) && asc[0] === 9000 && asc.every((v, i) => i === 0 || asc[i - 1] <= v) &&
    e2.ariaSort.at(-1) === 'ascending' && e2.ariaSort[0] === 'none',
    { desc: desc.slice(0, 3), asc: asc.slice(0, 3), ariaSort: e2.ariaSort });

  // NAV-6: teclado: Tab hasta el encabezado Fecha y Enter cambia el orden
  await js(`document.querySelector('.orden-boton').focus()`);
  await tecla('Enter', 'Enter', 13);
  await espera(150); await listo(); await espera(150);
  e = await estado();
  const foco = await js(`document.activeElement?.textContent?.replace(/\\s+/g, ' ').trim()`);
  registrar('NAV-6', 'Teclado: Enter sobre el encabezado Fecha ordena (desc) y el foco sigue en el botón',
    e.ariaSort[0] === 'descending' && foco.startsWith('Fecha'), { ariaSort: e.ariaSort[0], foco });

  // NAV-7: otra zona horaria en el navegador: la fecha y la hora siguen siendo las del club. Se recarga la pantalla
  // con la zona cambiada y se compara el mismo período con el mismo orden (fecha desc).
  const abrirSeptiembre = async () => {
    await enviar('Page.navigate', { url: `${BASE}/admin/portal/recaudacion-mensual` });
    await hasta(`document.querySelector('app-recaudacion-mensual')`);
    await listo();
    await elegirPeriodo('Septiembre');
    return (await estado()).filas.map((f) => f[0]);
  };
  const conArgentina = await abrirSeptiembre();
  await enviar('Emulation.setTimezoneOverride', { timezoneId: 'Asia/Tokyo' });
  const conTokio = await abrirSeptiembre();
  const zona = await js(`Intl.DateTimeFormat().resolvedOptions().timeZone`);
  registrar('NAV-7', 'Navegador en Asia/Tokyo (+12 h), pantalla recargada: fechas y horas idénticas a las del club (30/09/2026 23:59 sigue en 30/09)',
    zona === 'Asia/Tokyo' && JSON.stringify(conTokio) === JSON.stringify(conArgentina) && conTokio[0] === '30/09/2026 23:59',
    { zona, primera: conTokio[0], iguales: JSON.stringify(conTokio) === JSON.stringify(conArgentina) });
  await enviar('Emulation.setTimezoneOverride', { timezoneId: '' });
  await abrirSeptiembre();

  // NAV-8: exportar pide el período completo, sin página
  await js(`performance.clearResourceTimings()`);
  await js(`${COMP}.irAPagina(3)`); await listo();
  await js(`performance.clearResourceTimings()`);
  await js(`${COMP}.exportarRecaudacion('csv')`);
  await hasta(`!${COMP}.exportando`, 15000);
  await js(`${COMP}.exportarRecaudacion('pdf')`);
  await hasta(`!${COMP}.exportando`, 15000);
  const exportes = (await peticiones()).filter((n) => n.includes('/reportes/'));
  registrar('NAV-8', 'Exportar CSV y PDF desde la página 3: piden mes y año, sin page/pageSize',
    exportes.length === 2 && exportes.every((n) => n.includes('anio=2026') && n.includes('mes=9') && !/page/i.test(n)), exportes);

  // NAV-9: cambio de período: vuelve a página 1, recalcula total y tarjetas
  await elegirPeriodo('Octubre');
  e = await estado();
  registrar('NAV-9', 'Octubre (pocos pagos): página 1, "7 pagos", sin paginador, tarjetas del mes nuevo',
    e.conteo.includes('7 pagos en Octubre 2026') && !e.hayPaginador && e.filas.length === 7 && e.header !== antes.header,
    { conteo: e.conteo, filas: e.filas.length, header: e.header });
  await elegirPeriodo('Agosto');
  e = await estado();
  registrar('NAV-10', 'Agosto (vacío): "0 pagos", estado vacío, sin tabla ni paginador, tarjetas en $0',
    e.conteo.includes('0 pagos en Agosto 2026') && e.filas.length === 0 && !e.hayPaginador &&
    e.vacio.includes('No hay cobros') && /^\$\s*0(?!\d)/.test(e.header),
    { conteo: e.conteo, vacio: e.vacio, header: e.header });
  await captura('escritorio_agosto_vacio');

  // NAV-11/12: tablet y celular sin scroll horizontal de página, paginador usable
  await elegirPeriodo('Septiembre');
  for (const [nombre, ancho, alto, movil] of [['tablet', 820, 1180, true], ['celular', 390, 844, true]]) {
    await viewport(ancho, alto, movil);
    await espera(300);
    e = await estado();
    const paginador = await js(`(() => { const p = document.querySelector('.pagination'); if (!p) return null; const r = p.getBoundingClientRect(); return { izq: Math.round(r.left), der: Math.round(r.right) }; })()`);
    registrar(nombre === 'tablet' ? 'NAV-11' : 'NAV-12', `${nombre} (${ancho}px): sin scroll horizontal de página; paginador entero en pantalla; fechas legibles`,
      e.anchoPagina <= e.anchoVentana && paginador && paginador.izq >= 0 && paginador.der <= ancho && e.filas.every((f) => FECHA.test(f[0])),
      { scrollWidth: e.anchoPagina, innerWidth: e.anchoVentana, paginador, fecha: e.filas[0]?.[0] });
    await js(`document.querySelector('.panel-pagos').scrollIntoView({ block: 'start' })`);
    await espera(200);
    await captura(`${nombre}_septiembre`);
  }
} catch (error) {
  registrar('NAV-ERR', 'El recorrido terminó sin errores', false, String(error));
} finally {
  fs.writeFileSync(path.join(SALIDA, 'resultado_navegador.json'), JSON.stringify(resultados, null, 2));
  try { ws?.close(); } catch { /* ya cerrado */ }
  chrome.kill();
  await espera(500);
  try { fs.rmSync(perfil, { recursive: true, force: true }); } catch { /* Chrome todavía libera archivos */ }
}
clearTimeout(vigia);
process.exit(resultados.some((r) => r.estado === 'FAIL') ? 1 : 0);
