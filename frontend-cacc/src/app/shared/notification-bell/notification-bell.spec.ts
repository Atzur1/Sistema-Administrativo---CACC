import { ChangeDetectorRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';

import { NotificationBell } from './notification-bell';
import { NotificationService } from '../notifications/notification.service';
import { PendingActionsService } from '../../services/pending-actions';
import { CuotasFaltantesService } from '../../services/cuotas-faltantes';
import { UsuariosPortalService } from '../../services/usuarios-portal';
import { AuthService } from '../../services/auth';
import { MonthWithMissingFeesModel } from '../../models/MissingFeeModel';
import { PortalUserCandidateModel } from '../../models/PortalUserModel';

// HU-104: la campanita avisa lo que espera una decisión del SuperAdmin, aparte del historial de acciones.

const mes = (periodo: string, cantidad: number): MonthWithMissingFeesModel => ({ periodo, cantidad, montoTotal: cantidad * 92000 });
const cand = (idUsuario: number, nombre: string): PortalUserCandidateModel =>
  ({ idUsuario, nombre, apellido: 'Pérez', dni: '30111222', email: `${nombre}@cacc.test`, fkIdRolSugerido: 2 });

interface Opciones {
  rol?: number;
  meses?: () => Observable<MonthWithMissingFeesModel[]>;
  candidatos?: () => Observable<PortalUserCandidateModel[]>;
}

async function crear(opciones: Opciones = {}) {
  const getMeses = vi.fn(opciones.meses ?? (() => of([mes('2026-09-01', 45), mes('2026-08-01', 25)])));
  const getCandidatos = vi.fn(opciones.candidatos ?? (() => of([cand(20, 'Ana'), cand(21, 'Luis'), cand(22, 'Eva')])));
  const rol = opciones.rol ?? 1;

  await TestBed.configureTestingModule({
    imports: [NotificationBell],
    providers: [
      // Las pantallas a las que llevan los enlaces (vacías: acá solo importa que la navegación exista).
      provideRouter([{ path: 'admin/portal/resumen-general', children: [] }, { path: 'admin/portal/usuarios', children: [] }]),
      { provide: CuotasFaltantesService, useValue: { getMeses } },
      { provide: UsuariosPortalService, useValue: { getCandidatos } },
      { provide: AuthService, useValue: { isSuperAdmin: () => rol === 1, getToken: () => 't' } },
    ],
  }).compileComponents();

  // Lo arranca el Portal Administrativo al entrar (acá, a mano).
  const pendientes = TestBed.inject(PendingActionsService);
  pendientes.start();

  const fixture = TestBed.createComponent(NotificationBell);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const refrescar = () => {
    fixture.componentRef.injector.get(ChangeDetectorRef).markForCheck();
    fixture.detectChanges();
  };
  const boton = () => el.querySelector<HTMLButtonElement>('.bell-trigger')!;
  const badge = () => el.querySelector('.bell-badge')?.textContent?.trim() ?? null;
  const abrir = () => { boton().click(); refrescar(); };
  return { fixture, el, refrescar, boton, badge, abrir, pendientes, getMeses, getCandidatos, notifications: TestBed.inject(NotificationService) };
}

describe('NotificationBell - pendientes de acción (HU-104)', () => {
  afterEach(() => TestBed.inject(PendingActionsService).stop());

  it('el contador cuenta meses más personas: 2 meses y 3 candidatos son 5', async () => {
    const { badge, boton } = await crear();

    expect(badge()).toBe('5');
    expect(boton().getAttribute('aria-label')).toBe('Notificaciones: 5 pendientes de acción');
  });

  it('con más de 9 el globo muestra "9+", pero el nombre accesible y el panel dicen la cantidad real', async () => {
    const meses = Array.from({ length: 12 }, (_, i) => mes(`2025-${String(i + 1).padStart(2, '0')}-01`, 3));
    const { badge, boton, abrir, el } = await crear({ meses: () => of(meses), candidatos: () => of([]) });

    expect(badge()).toBe('9+');
    expect(boton().getAttribute('aria-label')).toBe('Notificaciones: 12 pendientes de acción');
    abrir();
    expect(el.querySelector('.bell-pending-head')!.textContent).toContain('12');
  });

  it('cada pendiente tiene su texto y un enlace real a donde se resuelve', async () => {
    const { abrir, el } = await crear();
    abrir();

    const enlaces = Array.from(el.querySelectorAll<HTMLAnchorElement>('.bell-pending a'));
    expect(enlaces.map((a) => a.textContent!.trim())).toEqual([
      'Cuotas de septiembre de 2026 sin generar (45)',
      'Cuotas de agosto de 2026 sin generar (25)',
      'Revisar acceso de Ana Pérez',
      'Revisar acceso de Luis Pérez',
      'Revisar acceso de Eva Pérez',
    ]);
    expect(enlaces[0].getAttribute('href')).toBe('/admin/portal/resumen-general');
    expect(enlaces[2].getAttribute('href')).toBe('/admin/portal/usuarios?pestana=candidatos');
  });

  it('abrir, cerrar y volver a abrir no baja el contador ni vuelve a consultar', async () => {
    const { abrir, badge, getMeses, getCandidatos } = await crear();

    abrir();
    abrir();
    abrir();

    expect(badge()).toBe('5');
    expect(getMeses).toHaveBeenCalledTimes(1);
    expect(getCandidatos).toHaveBeenCalledTimes(1);
  });

  it('una acción con toast va al historial y no suma pendientes; "Limpiar historial" no borra pendientes', async () => {
    const { abrir, el, refrescar, badge, notifications, pendientes } = await crear();

    notifications.notify('Pago registrado correctamente.', 'success');
    refrescar();
    expect(pendientes.total()).toBe(5);
    expect(badge()).toBe('5');

    abrir();
    expect(el.querySelector('.bell-history')!.textContent).toContain('Pago registrado correctamente.');
    const limpiar = el.querySelector<HTMLButtonElement>('.bell-clear')!;
    expect(limpiar.textContent!.trim()).toBe('Limpiar historial');
    limpiar.click();
    refrescar();

    expect(notifications.notifications()).toEqual([]);
    expect(el.querySelectorAll('.bell-pending a')).toHaveLength(5);
  });

  it('seguir un enlace cierra el panel', async () => {
    const { abrir, el, refrescar } = await crear();
    abrir();

    el.querySelector<HTMLAnchorElement>('.bell-pending a')!.click();
    refrescar();

    expect(el.querySelector('.bell-panel')).toBeNull();
  });

  it('mientras carga no dice que no hay pendientes', async () => {
    const { abrir, el } = await crear({ meses: () => new Observable(), candidatos: () => new Observable() });
    abrir();

    expect(el.textContent).not.toContain('No tenés pendientes de acción');
    expect(el.querySelector('.bell-pending')!.textContent).toContain('Buscando pendientes');
  });

  it('con las dos fuentes vacías no muestra globo y lo confirma en el panel', async () => {
    const { abrir, el, badge } = await crear({ meses: () => of([]), candidatos: () => of([]) });

    expect(badge()).toBeNull();
    abrir();
    expect(el.querySelector('.bell-pending')!.textContent).toContain('No tenés pendientes de acción');
  });

  it('si una fuente falla, lo dice con texto, muestra la otra y no afirma que el total esté completo', async () => {
    const { abrir, el, badge } = await crear({ meses: () => throwError(() => new HttpErrorResponse({ status: 500 })) });

    expect(badge()).toBe('3');
    abrir();
    const panel = el.querySelector('.bell-pending')!.textContent!;
    expect(panel).toContain('No se pudieron consultar los meses con cuotas sin generar');
    expect(panel).not.toContain('No tenés pendientes de acción');
    expect(el.querySelectorAll('.bell-pending a')).toHaveLength(3);
  });

  it('se abre con un botón que dice qué controla, y Esc cierra y devuelve el foco al botón', async () => {
    const { abrir, el, boton, refrescar } = await crear();
    abrir();

    const panel = el.querySelector('.bell-panel')!;
    expect(boton().getAttribute('aria-expanded')).toBe('true');
    expect(boton().getAttribute('aria-controls')).toBe(panel.id);
    expect(panel.getAttribute('role')).not.toBe('menu');

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    refrescar();
    expect(el.querySelector('.bell-panel')).toBeNull();
    expect(document.activeElement).toBe(boton());
  });
});

describe('NotificationBell - Administrador (HU-104)', () => {
  afterEach(() => TestBed.inject(PendingActionsService).stop());

  it('no consulta ni muestra pendientes protegidos; conserva su historial', async () => {
    const { abrir, el, badge, refrescar, notifications, getMeses, getCandidatos } = await crear({ rol: 2 });

    expect(getMeses).not.toHaveBeenCalled();
    expect(getCandidatos).not.toHaveBeenCalled();
    notifications.notify('Beneficio asignado.', 'success');
    refrescar();
    expect(badge()).toBe('1');                            // el historial sin leer, como siempre

    abrir();
    expect(el.querySelector('.bell-pending')).toBeNull();
    expect(el.textContent).toContain('Beneficio asignado.');
  });
});
