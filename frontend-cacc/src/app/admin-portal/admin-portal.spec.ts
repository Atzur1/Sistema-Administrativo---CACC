import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Route, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { routes } from '../app.routes';
import { AuthService, superAdminGuard } from '../services/auth';
import { DtAdminChat } from '../shared/dt-admin-chat/dt-admin-chat';
import { NotificationBell } from '../shared/notification-bell/notification-bell';
import { Toast } from '../shared/toast/toast';
import { AdminPortal } from './admin-portal';
import { PORTAL_ADMINISTRATIVO, SECCIONES, Seccion } from './secciones';

// HU-103: barra superior, ítem activo, nombres y drawer del Portal Administrativo. Las rutas son las de app.routes.ts
// (paths y data reales); solo se cambian las pantallas por una vacía y se sacan los guards, que ya prueba
// auth.guards.spec.ts.

@Component({ template: '' })
class Pantalla {}

@Component({ selector: 'app-toast', template: '' })
class ToastStub {}

@Component({ selector: 'app-notification-bell', template: '' })
class NotificationBellStub {}

@Component({ selector: 'app-dt-admin-chat', template: '' })
class DtAdminChatStub {}

const portalReal = routes.find((r) => r.path === 'admin/portal')!;
const hijasReales = portalReal.children!;

const SOLO_SUPERADMIN: Seccion[] = [
  'resumen-general', 'recaudacion-mensual', 'actividad-movimientos', 'reportes',
  'actualizacion-aranceles', 'usuarios', 'auditoria',
];
const DEL_ADMINISTRADOR: Seccion[] = ['cuotas-pagos', 'deudas-morosidad', 'becados-descuentos'];
// Orden del menú del SuperAdmin (por grupo de tarea).
const MENU_SUPERADMIN: Seccion[] = [
  'resumen-general', 'cuotas-pagos', 'deudas-morosidad', 'becados-descuentos', 'recaudacion-mensual',
  'actividad-movimientos', 'reportes', 'actualizacion-aranceles', 'usuarios', 'auditoria',
];

async function montar(rol: 1 | 2, url: string) {
  TestBed.configureTestingModule({
    providers: [
      provideRouter([
        {
          path: 'admin/portal',
          component: AdminPortal,
          children: hijasReales.map((r): Route => ({ path: r.path, data: r.data, component: Pantalla })),
        },
        { path: 'portales', component: Pantalla },
        { path: '', component: Pantalla },
      ]),
      {
        provide: AuthService,
        useValue: {
          isSuperAdmin: () => rol === 1,
          getUsuario: () => ({ email: 'prueba@cacc.test', firstName: 'Prueba', lastName: 'Portal', role: rol }),
          cerrarSesion: () => {},
        },
      },
    ],
  });
  TestBed.overrideComponent(AdminPortal, {
    remove: { imports: [Toast, NotificationBell, DtAdminChat] },
    add: { imports: [ToastStub, NotificationBellStub, DtAdminChatStub] },
  });
  const harness = await RouterTestingHarness.create();
  await harness.navigateByUrl(url);
  return harness;
}

const elementos = (h: RouterTestingHarness, selector: string) =>
  Array.from((h.fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>(selector));
const texto = (el: Element | null | undefined) => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();
// Los tramos de la barra (sin los separadores, que son aria-hidden): lo que lee un lector de pantalla.
const barra = (h: RouterTestingHarness) =>
  elementos(h, '.topbar-breadcrumb > span:not([aria-hidden="true"])').map(texto).join(' · ');
const activos = (h: RouterTestingHarness) => elementos(h, '.sidebar-nav [aria-current="page"]');
const itemsMenu = (h: RouterTestingHarness) => elementos(h, '.sidebar-nav a.nav-item');

describe('AdminPortal - orientación (HU-103)', () => {
  describe('barra superior', () => {
    for (const seccion of MENU_SUPERADMIN) {
      it(`${seccion}: muestra "Portal administrativo · ${SECCIONES[seccion]}" (contexto, no el h1 solo)`, async () => {
        const h = await montar(1, `/admin/portal/${seccion}`);
        expect(barra(h)).toBe(`${PORTAL_ADMINISTRATIVO} · ${SECCIONES[seccion]}`);
      });
    }

    it('no depende del texto de la URL: query params y #fragmento no cambian el título', async () => {
      const h = await montar(1, '/admin/portal/cuotas-pagos?jugador=12#player');
      expect(barra(h)).toBe(`${PORTAL_ADMINISTRATIVO} · Cuotas y Pagos`);
    });

    it('pantalla de detalle: portal · sección · subpágina, sin el id del jugador', async () => {
      const h = await montar(2, '/admin/portal/jugadores/57/deuda');
      expect(barra(h)).toBe(`${PORTAL_ADMINISTRATIVO} · Cuotas y Pagos · Deuda Pendiente`);
      expect(barra(h)).not.toContain('57');
    });
  });

  describe('ítem activo', () => {
    for (const seccion of MENU_SUPERADMIN) {
      it(`${seccion}: un solo ítem activo (aria-current="page"), el de esa sección`, async () => {
        const h = await montar(1, `/admin/portal/${seccion}`);
        const activo = activos(h);
        expect(activo.length).toBe(1);
        expect(texto(activo[0])).toBe(SECCIONES[seccion]);
        expect(activo[0].getAttribute('href')).toBe(`/admin/portal/${seccion}`);
      });
    }

    it('jugadores/:id/deuda mantiene activa Cuotas y Pagos (adonde vuelve su botón Volver)', async () => {
      const h = await montar(2, '/admin/portal/jugadores/8/deuda');
      expect(activos(h).map(texto)).toEqual(['Cuotas y Pagos']);
    });

    it('jugadores/:id mantiene activa Actividad y Movimientos (adonde vuelve su botón Volver)', async () => {
      const h = await montar(1, '/admin/portal/jugadores/8');
      expect(activos(h).map(texto)).toEqual(['Actividad y Movimientos']);
      expect(barra(h)).toBe(`${PORTAL_ADMINISTRATIVO} · Actividad y Movimientos · Perfil de Jugador`);
    });

    it('al navegar cambia la marca: nunca quedan dos ítems activos', async () => {
      const h = await montar(1, '/admin/portal/usuarios');
      await h.navigateByUrl('/admin/portal/auditoria');
      expect(activos(h).map(texto)).toEqual(['Auditoría']);
      await h.navigateByUrl('/admin/portal/jugadores/3/deuda');
      expect(activos(h).map(texto)).toEqual(['Cuotas y Pagos']);
    });

    it('las rutas hijas de app.routes.ts declaran una sección que existe en el menú', () => {
      for (const r of hijasReales.filter((x) => x.data?.['seccion'])) {
        expect(Object.keys(SECCIONES)).toContain(r.data!['seccion']);
        expect(typeof r.data!['subpagina']).toBe('string');
      }
    });
  });

  describe('nombres y menú colapsado', () => {
    it('cada ítem tiene como tooltip (title) exactamente el nombre que muestra, que es el oficial', async () => {
      const h = await montar(1, '/admin/portal/resumen-general');
      const items = itemsMenu(h);
      expect(items.map(texto)).toEqual(MENU_SUPERADMIN.map((s) => SECCIONES[s]));
      for (const item of items) {
        expect(item.getAttribute('title')).toBe(texto(item));
      }
    });

    it('el nombre sigue en el árbol accesible con el menú colapsado (el texto se oculta con max-width, no con display:none)', async () => {
      const h = await montar(1, '/admin/portal/reportes');
      for (const span of elementos(h, '.sidebar-nav a.nav-item span')) {
        expect(span.hidden).toBe(false);
        expect(getComputedStyle(span).display).not.toBe('none');
      }
      expect(elementos(h, 'nav.sidebar-nav')[0].getAttribute('aria-label')).toBe('Secciones del portal');
    });
  });

  describe('permisos (sin cambios)', () => {
    it('SuperAdmin ve las 10 secciones y "Cambiar portal"', async () => {
      const h = await montar(1, '/admin/portal/resumen-general');
      expect(itemsMenu(h).map((a) => a.getAttribute('href'))).toEqual(MENU_SUPERADMIN.map((s) => `/admin/portal/${s}`));
      expect(elementos(h, '.sidebar-footer button').map(texto)).toEqual(['Cambiar portal', 'Cerrar sesión']);
    });

    it('Administrador ve solo Cobranza y ninguna sección reservada a SuperAdmin', async () => {
      const h = await montar(2, '/admin/portal/cuotas-pagos');
      const visibles = itemsMenu(h).map((a) => a.getAttribute('href'));
      expect(visibles).toEqual(DEL_ADMINISTRADOR.map((s) => `/admin/portal/${s}`));
      for (const s of SOLO_SUPERADMIN) expect(visibles).not.toContain(`/admin/portal/${s}`);
      expect(elementos(h, '.sidebar-footer button').map(texto)).toEqual(['Cerrar sesión']);
    });

    it('el menú coincide con los guards reales: lo que ve el Administrador no tiene superAdminGuard y el resto sí', () => {
      const guarda = (s: Seccion) => hijasReales.find((r) => r.path === s)?.canActivate?.includes(superAdminGuard) ?? false;
      for (const s of DEL_ADMINISTRADOR) expect(guarda(s)).toBe(false);
      for (const s of SOLO_SUPERADMIN) expect(guarda(s)).toBe(true);
    });
  });

  describe('drawer móvil (sin cambios)', () => {
    const aside = (h: RouterTestingHarness) => elementos(h, 'aside.sidebar')[0];
    const boton = (h: RouterTestingHarness) => elementos(h, 'button.sidebar-toggle')[0];

    it('la hamburguesa abre el drawer y el fondo oscuro lo cierra', async () => {
      const h = await montar(2, '/admin/portal/cuotas-pagos');
      expect(boton(h).getAttribute('aria-expanded')).toBe('false');
      boton(h).click();
      h.detectChanges();
      expect(boton(h).getAttribute('aria-expanded')).toBe('true');
      expect(aside(h).classList).toContain('is-open');
      elementos(h, '.sidebar-backdrop')[0].click();
      h.detectChanges();
      expect(aside(h).classList).not.toContain('is-open');
      expect(elementos(h, '.sidebar-backdrop').length).toBe(0);
    });

    it('elegir una opción navega y cierra el drawer', async () => {
      const h = await montar(2, '/admin/portal/cuotas-pagos');
      boton(h).click();
      h.detectChanges();
      itemsMenu(h).find((a) => texto(a) === 'Deudas y Morosidad')!.click();
      await h.fixture.whenStable();
      h.detectChanges();
      expect(aside(h).classList).not.toContain('is-open');
      expect(activos(h).map(texto)).toEqual(['Deudas y Morosidad']);
    });

    it('Esc cierra el drawer', async () => {
      const h = await montar(2, '/admin/portal/becados-descuentos');
      boton(h).click();
      h.detectChanges();
      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
      h.detectChanges();
      expect(aside(h).classList).not.toContain('is-open');
    });
  });
});
