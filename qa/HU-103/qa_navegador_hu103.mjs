// QA manual asistido de HU-103: Chrome headless (el instalado, perfil temporal) por CDP, sin dependencias.
// Uso (con ng serve en :4200 y la API en :5118 sobre la base de HU-059): CACC_CLAVE=... node qa/HU-103/qa_navegador_hu103.mjs <carpeta-salida> <idJugador>
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const SALIDA = process.argv[2];
const ID_JUGADOR = process.argv[3] ?? '1';
const BASE = 'http://localhost:4200';
const CLAVE = process.env.CACC_CLAVE;
fs.mkdirSync(SALIDA, { recursive: true });
const espera = (ms) => new Promise((r) => setTimeout(r, ms));

const perfil = fs.mkdtempSync(path.join(os.tmpdir(), 'cacc-qa103-'));
const chrome = spawn('C:/Program Files/Google/Chrome/Application/chrome.exe', [
  '--headless=new', '--remote-debugging-port=9333', `--user-data-dir=${perfil}`, '--no-first-run',
  '--no-default-browser-check', '--disable-extensions', '--window-size=1440,900', 'about:blank',
]);

let ws; let siguiente = 0; const pendientes = new Map(); const dialogos = [];
async function conectar() {
  for (let i = 0; i < 50; i++) {
    try {
      const lista = await (await fetch('http://127.0.0.1:9333/json/list')).json();
      const pagina = lista.find((t) => t.type === 'page');
      if (pagina) { ws = new WebSocket(pagina.webSocketDebuggerUrl); break; }
    } catch { /* todavía arrancando */ }
    await espera(200);
  }
  await new Promise((r) => ws.addEventListener('open', r, { once: true }));
  ws.addEventListener('message', (e) => {
    const m = JSON.parse(e.data);
    if (m.id && pendientes.has(m.id)) { pendientes.get(m.id)(m); pendientes.delete(m.id); }
    if (m.method === 'Page.javascriptDialogOpening') {
      dialogos.push(m.params.message);
      enviar('Page.handleJavaScriptDialog', { accept: true });
    }
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
  await espera(450);
}
const mouse = (x, y) => enviar('Input.dispatchMouseEvent', { type: 'mouseMoved', x, y });
async function tecla(key, code, keyCode, shift = false) {
  const base = { key, code, windowsVirtualKeyCode: keyCode, nativeVirtualKeyCode: keyCode, modifiers: shift ? 8 : 0 };
  await enviar('Input.dispatchKeyEvent', { type: 'rawKeyDown', ...base });
  await enviar('Input.dispatchKeyEvent', { type: 'keyUp', ...base });
}
async function captura(nombre) {
  const r = await enviar('Page.captureScreenshot', { format: 'png' });
  fs.writeFileSync(path.join(SALIDA, `${nombre}.png`), Buffer.from(r.result.data, 'base64'));
}

const resultados = [];
function registrar(rol, id, caso, ok, obtenido) {
  resultados.push({ rol, id, caso, estado: ok ? 'PASS' : 'FAIL', obtenido });
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${rol.padEnd(13)} ${id.padEnd(9)} ${caso} | ${typeof obtenido === 'string' ? obtenido : JSON.stringify(obtenido)}`);
}

// Estado de la pantalla: barra (tramos visibles), h1, activos, tooltip, estilos del activo, sidebar y desbordes.
const ESTADO = `(() => {
  const vis = (el) => el && getComputedStyle(el).display !== 'none' && el.getBoundingClientRect().width > 0;
  const t = (el) => (el?.textContent ?? '').replace(/\\s+/g, ' ').trim();
  const tramos = [...document.querySelectorAll('.topbar-breadcrumb > span:not([aria-hidden="true"])')].filter(vis).map(t);
  const activos = [...document.querySelectorAll('.sidebar-nav [aria-current="page"]')];
  const a = activos[0]; const cs = a ? getComputedStyle(a) : null;
  const aside = document.querySelector('aside.sidebar');
  const corte = [...document.querySelectorAll('.topbar-breadcrumb > span')].filter(vis).filter((s) => s.scrollWidth > s.clientWidth + 1).map(t);
  return {
    url: location.pathname, barra: tramos.join(' · '), h1: [...document.querySelectorAll('main h1')].map(t),
    activos: activos.map(t), claseActiva: [...document.querySelectorAll('.sidebar-nav .nav-item.active')].map(t),
    tooltipActivo: a?.getAttribute('title') ?? null,
    activoFondo: cs?.backgroundColor, activoBorde: cs ? cs.borderLeftColor + ' ' + cs.borderLeftWidth : null,
    menu: [...document.querySelectorAll('.sidebar-nav a.nav-item')].map((x) => ({ texto: t(x), title: x.getAttribute('title'), href: x.getAttribute('href') })),
    pie: [...document.querySelectorAll('.sidebar-footer button')].map(t),
    anchoSidebar: Math.round(aside.getBoundingClientRect().width), xSidebar: Math.round(aside.getBoundingClientRect().x),
    abierto: aside.classList.contains('is-open'), backdrop: !!document.querySelector('.sidebar-backdrop'),
    scrollHorizontal: document.documentElement.scrollWidth > window.innerWidth,
    barraCortada: corte, anchoVentana: innerWidth,
    barraDesborda: [...document.querySelectorAll('.admin-topbar *')].filter(vis).some((x) => x.getBoundingClientRect().right > innerWidth + 0.5),
    portalEnUnaLinea: (() => { const p = document.querySelector('.breadcrumb-portal'); return !p || p.getBoundingClientRect().height < 24; })(),
    textoMenuVisible: getComputedStyle(document.querySelector('.sidebar-nav a.nav-item span')).opacity,
  };
})()`;

async function login(usuario) {
  await enviar('Page.navigate', { url: `${BASE}/` });
  await hasta(`document.querySelector('input[formcontrolname="username"]')`);
  await js(`(() => {
    const set = (sel, v) => { const el = document.querySelector(sel); const d = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value'); d.set.call(el, v); el.dispatchEvent(new Event('input', { bubbles: true })); };
    set('input[formcontrolname="username"]', ${JSON.stringify(usuario)});
    set('input[formcontrolname="password"]', ${JSON.stringify(CLAVE)});
    document.querySelector('form button[type="submit"]').click();
  })()`);
  await hasta(`location.pathname !== '/'`);
  if (await js(`location.pathname === '/portales'`)) {
    await hasta(`document.querySelector('.card-portal-administrativo .btn-portal')`);
    await js(`document.querySelector('.card-portal-administrativo .btn-portal').click()`);
  }
  await hasta(`document.querySelector('.topbar-breadcrumb') && location.pathname.startsWith('/admin/portal/')`);
  await espera(600);
}
async function clicMenu(texto) {
  await js(`[...document.querySelectorAll('.sidebar-nav a.nav-item')].find((a) => a.textContent.trim() === ${JSON.stringify(texto)}).click()`);
  await hasta(`document.querySelector('.sidebar-nav [aria-current="page"]')?.textContent.trim() === ${JSON.stringify(texto)}`, 8000);
  await hasta(`document.querySelector('main h1')`, 8000);
  await espera(250);
}
async function ir(url) {
  await js(`history.pushState({}, '', ${JSON.stringify(url)}); dispatchEvent(new PopStateEvent('popstate'))`);
  await espera(900);
}

async function recorrerRol(rol, usuario, esperados) {
  await viewport(1440, 900);
  await login(usuario);
  await mouse(900, 450);
  await espera(500);
  let e = await js(ESTADO);
  registrar(rol, 'MENU', 'opciones visibles (en orden)', JSON.stringify(e.menu.map((m) => m.texto)) === JSON.stringify(esperados), e.menu.map((m) => m.texto));
  registrar(rol, 'TOOLTIP', 'title de cada ítem = su texto', e.menu.every((m) => m.title === m.texto), e.menu.filter((m) => m.title !== m.texto));
  registrar(rol, 'PIE', 'acciones del pie', JSON.stringify(e.pie) === JSON.stringify(rol === 'SuperAdmin' ? ['Cambiar portal', 'Cerrar sesión'] : ['Cerrar sesión']), e.pie);

  const matriz = [];
  for (const nombre of esperados) {
    await clicMenu(nombre);
    await mouse(900, 450); await espera(450);
    e = await js(ESTADO);
    matriz.push({ url: e.url, menu: nombre, barra: e.barra, h1: e.h1[0] ?? null, activos: e.activos });
    const ok = e.barra === `Portal administrativo · ${nombre}` && e.h1.length === 1 && e.h1[0] === nombre
      && e.activos.length === 1 && e.activos[0] === nombre && e.claseActiva.length === 1 && e.tooltipActivo === nombre && !e.scrollHorizontal;
    registrar(rol, 'SEC', `${nombre}: barra/h1/activo/tooltip`, ok, { barra: e.barra, h1: e.h1, activos: e.activos, fondo: e.activoFondo, borde: e.activoBorde });
    await captura(`${rol}_${e.url.split('/').pop()}_colapsado`);
  }

  // Hover real: entra el mouse al riel -> se expande; sale -> vuelve a 72px.
  await mouse(900, 450); await espera(500);
  const antes = await js(ESTADO);
  await mouse(36, 300); await espera(700);
  const dentro = await js(ESTADO);
  await captura(`${rol}_hover_expandido`);
  await mouse(900, 450); await espera(700);
  const fuera = await js(ESTADO);
  registrar(rol, 'HOVER', 'colapsado 72px -> mouse entra: 252px y texto visible -> sale: 72px', antes.anchoSidebar === 72 && dentro.anchoSidebar === 252 && dentro.textoMenuVisible === '1' && fuera.anchoSidebar === 72 && antes.textoMenuVisible === '0',
    `${antes.anchoSidebar}px (texto ${antes.textoMenuVisible}) -> ${dentro.anchoSidebar}px (texto ${dentro.textoMenuVisible}) -> ${fuera.anchoSidebar}px`);

  // Teclado: Tab entra al sidebar, lo expande (focus-within) y el foco tiene contorno blanco.
  await js(`document.activeElement?.blur(); document.querySelector('.sidebar-header img')?.setAttribute('tabindex','-1'); document.querySelector('.sidebar-header img')?.focus(); document.querySelector('.sidebar-header img')?.removeAttribute('tabindex')`);
  await tecla('Tab', 'Tab', 9);
  await espera(600);
  const foco = await js(`(() => { const f = document.activeElement; const cs = getComputedStyle(f); return { texto: f.textContent.trim(), clase: f.className, outline: cs.outlineStyle + ' ' + cs.outlineColor + ' ' + cs.outlineWidth, offset: cs.outlineOffset, ancho: Math.round(document.querySelector('aside.sidebar').getBoundingClientRect().width) }; })()`);
  await captura(`${rol}_foco_teclado`);
  registrar(rol, 'FOCO', 'Tab al primer ítem: sidebar expandido y contorno blanco hacia adentro', foco.ancho === 252 && /rgb\(255, 255, 255\)/.test(foco.outline) && foco.offset === '-2px' && foco.clase.includes('nav-item'), foco);
  await js(`document.activeElement?.blur()`);
  await mouse(900, 450); await espera(500);

  return matriz;
}

async function rutasHijas(rol) {
  const casos = rol === 'SuperAdmin'
    ? [[`/admin/portal/jugadores/${ID_JUGADOR}`, 'Actividad y Movimientos', 'Perfil de Jugador'], [`/admin/portal/jugadores/${ID_JUGADOR}/deuda`, 'Cuotas y Pagos', 'Deuda Pendiente']]
    : [[`/admin/portal/jugadores/${ID_JUGADOR}/deuda`, 'Cuotas y Pagos', 'Deuda Pendiente']];
  const filas = [];
  for (const [url, seccion, sub] of casos) {
    await ir(url);
    await hasta(`document.querySelector('main h1')`, 8000);
    const e = await js(ESTADO);
    filas.push({ url, menu: seccion, barra: e.barra, h1: e.h1[0] ?? null, activos: e.activos });
    registrar(rol, 'HIJA', `${url}: activo ${seccion}, barra con "${sub}", h1 = nombre del jugador`, e.activos.length === 1 && e.activos[0] === seccion && e.barra === `Portal administrativo · ${seccion} · ${sub}` && e.h1.length === 1 && e.h1[0] !== seccion,
      { barra: e.barra, h1: e.h1, activos: e.activos });
    await captura(`${rol}_${url.split('/').slice(-2).join('_')}`);
  }
  // Query params y #fragmento no cambian el título ni la marca.
  await ir('/admin/portal/deudas-morosidad?categoria=3#tabla');
  const q = await js(ESTADO);
  registrar(rol, 'QUERY', 'URL con ?query y #fragmento', q.barra === 'Portal administrativo · Deudas y Morosidad' && q.activos.join() === 'Deudas y Morosidad', { barra: q.barra, activos: q.activos });
  return filas;
}

async function responsive(rol) {
  await ir('/admin/portal/jugadores/' + ID_JUGADOR + '/deuda');
  for (const [w, h, nombre] of [[1920, 1080, 'desktop'], [1366, 768, 'notebook'], [1024, 768, 'tablet-h'], [768, 1024, 'tablet-v'], [600, 960, 'tablet-chica'], [375, 812, 'mobile']]) {
    for (const url of ['/admin/portal/actualizacion-aranceles', `/admin/portal/jugadores/${ID_JUGADOR}/deuda`]) {
      if (rol !== 'SuperAdmin' && url.includes('aranceles')) continue;
      await viewport(w, h, w <= 600);
      await ir(url);
      await mouse(w - 10, h - 10); await espera(400);
      const e = await js(ESTADO);
      const esperado = w <= 520 ? 'Portal administrativo'
        : url.includes('deuda') ? (w <= 768 ? 'Portal administrativo · Cuotas y Pagos' : 'Portal administrativo · Cuotas y Pagos · Deuda Pendiente')
        : 'Portal administrativo · Actualización de Aranceles';
      const sidebarFuera = w <= 768 ? (e.xSidebar <= -e.anchoSidebar + 1 && !e.abierto) : e.anchoSidebar === 72;
      registrar(rol, 'RESP', `${nombre} ${w}x${h} ${url.split('/').slice(-1)[0]}: barra "${esperado}", sin scroll horizontal, texto cortado ni desborde`, e.barra === esperado && !e.scrollHorizontal && e.barraCortada.length === 0 && !e.barraDesborda && e.portalEnUnaLinea && sidebarFuera,
        { barra: e.barra, scrollH: e.scrollHorizontal, cortado: e.barraCortada, desborda: e.barraDesborda, unaLinea: e.portalEnUnaLinea, sidebar: `${e.xSidebar}/${e.anchoSidebar}` });
      await captura(`${rol}_${nombre}_${url.includes('deuda') ? 'deuda' : 'aranceles'}`);
    }
  }
}

async function drawer(rol, destino) {
  await viewport(375, 812, true);
  await ir('/admin/portal/cuotas-pagos');
  let e = await js(ESTADO);
  const cerrado = !e.abierto && !e.backdrop && e.xSidebar < 0;
  await js(`document.querySelector('button.sidebar-toggle').click()`); await espera(500);
  e = await js(ESTADO);
  const abierto = e.abierto && e.backdrop && e.xSidebar === 0;
  const aria = await js(`document.querySelector('button.sidebar-toggle').getAttribute('aria-expanded')`);
  await captura(`${rol}_drawer_abierto`);
  // Scroll del menú dentro del drawer en una pantalla baja.
  const scroll = await js(`(() => { const n = document.querySelector('.sidebar-nav'); const s = getComputedStyle(n).overflowY; n.scrollTop = 0; return s; })()`);
  // Elegir una opción navega y cierra.
  await js(`[...document.querySelectorAll('.sidebar-nav a.nav-item')].find((a) => a.textContent.trim() === ${JSON.stringify(destino)}).click()`);
  await hasta(`!document.querySelector('aside.sidebar').classList.contains('is-open')`, 4000); await espera(400);
  const trasNavegar = await js(ESTADO);
  // Fondo oscuro cierra.
  await js(`document.querySelector('button.sidebar-toggle').click()`); await espera(400);
  await js(`document.querySelector('.sidebar-backdrop').click()`); await espera(450);
  const trasFondo = await js(ESTADO);
  // Esc cierra.
  await js(`document.querySelector('button.sidebar-toggle').click()`); await espera(400);
  await tecla('Escape', 'Escape', 27); await espera(450);
  const trasEsc = await js(ESTADO);
  // Hover en móvil no lo abre (es táctil: solo la hamburguesa).
  await mouse(20, 300); await espera(500);
  const hoverMovil = await js(ESTADO);
  registrar(rol, 'DRAWER', 'cerrado -> hamburguesa abre (aria-expanded, overlay) -> opción navega y cierra -> overlay cierra -> Esc cierra; hover no lo abre',
    cerrado && abierto && aria === 'true' && !trasNavegar.abierto && trasNavegar.activos.join() === destino && trasNavegar.url.endsWith('/' + (destino === 'Deudas y Morosidad' ? 'deudas-morosidad' : 'x')) && !trasFondo.abierto && !trasFondo.backdrop && !trasEsc.abierto && !hoverMovil.abierto && hoverMovil.xSidebar < 0,
    { cerrado, abierto, aria, scroll, trasNavegar: [trasNavegar.url, trasNavegar.activos], trasFondo: trasFondo.abierto, trasEsc: trasEsc.abierto, hoverMovil: hoverMovil.xSidebar });
}

async function prohibidas() {
  const filas = [];
  for (const s of ['resumen-general', 'reportes', 'usuarios', 'auditoria', 'recaudacion-mensual', 'actividad-movimientos', 'actualizacion-aranceles', `jugadores/${ID_JUGADOR}`]) {
    dialogos.length = 0;
    await viewport(1440, 900);
    await ir(`/admin/portal/${s}`);
    await espera(400);
    const e = await js(ESTADO);
    filas.push({ s, url: e.url, dialogo: dialogos[0] ?? null });
    registrar('Administrador', 'GUARD', `/admin/portal/${s} por URL: no entra (guard sin cambios)`, !e.url.endsWith('/' + s), { quedó: e.url, aviso: dialogos[0] ?? null });
  }
  return filas;
}

const SUPER = ['Resumen General', 'Cuotas y Pagos', 'Deudas y Morosidad', 'Becados y Descuentos', 'Recaudación Mensual', 'Actividad y Movimientos', 'Reportes', 'Actualización de Aranceles', 'Usuarios y Permisos', 'Auditoría'];
const ADMIN = ['Cuotas y Pagos', 'Deudas y Morosidad', 'Becados y Descuentos'];
try {
  await conectar();
  await enviar('Page.enable'); await enviar('Runtime.enable');
  const salida = { matriz: {}, hijas: {}, prohibidas: [] };
  salida.matriz.SuperAdmin = await recorrerRol('SuperAdmin', '20111222', SUPER);
  salida.hijas.SuperAdmin = await rutasHijas('SuperAdmin');
  await responsive('SuperAdmin');
  await drawer('SuperAdmin', 'Deudas y Morosidad');
  await js(`sessionStorage.clear(); localStorage.clear()`);
  salida.matriz.Administrador = await recorrerRol('Administrador', '30000005', ADMIN);
  salida.hijas.Administrador = await rutasHijas('Administrador');
  await responsive('Administrador');
  await drawer('Administrador', 'Deudas y Morosidad');
  salida.prohibidas = await prohibidas();
  salida.resultados = resultados;
  fs.writeFileSync(path.join(SALIDA, 'qa_hu103.json'), JSON.stringify(salida, null, 2));
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
