import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AdminPortal } from './admin-portal';
import { AuthService } from '../services/auth';
import { PendingActionsService } from '../services/pending-actions';
import { RoleChangesService } from '../services/role-changes';

// HU-104: el Portal Administrativo inicia los pendientes al entrar (desde cualquier pantalla, sin pasar por Resumen
// General ni por Usuarios) y los detiene al salir. Se prueba solo ese cableado: la plantilla del portal se reemplaza.
async function crear() {
  const pendientes = { start: vi.fn(), stop: vi.fn() };
  const cambiosDeRol = { clear: vi.fn() };
  const cerrarSesion = vi.fn();
  await TestBed.configureTestingModule({
    imports: [AdminPortal],
    providers: [
      provideRouter([]),
      { provide: PendingActionsService, useValue: pendientes },
      { provide: RoleChangesService, useValue: cambiosDeRol },
      { provide: AuthService, useValue: { getUsuario: () => null, isSuperAdmin: () => true, cerrarSesion } },
    ],
  })
    .overrideComponent(AdminPortal, { set: { template: '', imports: [] } })
    .compileComponents();
  const fixture = TestBed.createComponent(AdminPortal);
  fixture.detectChanges();
  return { fixture, pendientes, cerrarSesion, cambiosDeRol };
}

describe('AdminPortal - pendientes de acción (HU-104)', () => {
  it('al entrar al portal inicia los pendientes, sin esperar a ninguna pantalla', async () => {
    const { pendientes } = await crear();

    expect(pendientes.start).toHaveBeenCalledTimes(1);
  });

  it('al salir del portal (por ejemplo, sesión vencida) los detiene', async () => {
    const { fixture, pendientes } = await crear();

    fixture.destroy();

    expect(pendientes.stop).toHaveBeenCalled();
  });

  it('cerrar sesión los detiene antes de salir', async () => {
    const { fixture, pendientes, cerrarSesion } = await crear();

    fixture.componentInstance.cerrarSesion();

    expect(pendientes.stop).toHaveBeenCalled();
    expect(cerrarSesion).toHaveBeenCalled();
  });
});

// HU-105: los avisos con "Deshacer" duran mientras se navega dentro del portal y se borran al salir de él.
describe('AdminPortal - avisos para deshacer (HU-105)', () => {
  it('cerrar sesión borra los avisos y lo que estaba en camino', async () => {
    const { fixture, cambiosDeRol } = await crear();

    fixture.componentInstance.cerrarSesion();

    expect(cambiosDeRol.clear).toHaveBeenCalled();
  });

  it('salir del portal por otra vía (por ejemplo, sesión vencida) también los borra', async () => {
    const { fixture, cambiosDeRol } = await crear();
    expect(cambiosDeRol.clear).not.toHaveBeenCalled();

    fixture.destroy();

    expect(cambiosDeRol.clear).toHaveBeenCalled();
  });
});
