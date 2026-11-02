import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AdminPortal } from './admin-portal';
import { AuthService } from '../services/auth';
import { PendingActionsService } from '../services/pending-actions';

// HU-104: el Portal Administrativo inicia los pendientes al entrar (desde cualquier pantalla, sin pasar por Resumen
// General ni por Usuarios) y los detiene al salir. Se prueba solo ese cableado: la plantilla del portal se reemplaza.
async function crear() {
  const pendientes = { start: vi.fn(), stop: vi.fn() };
  const cerrarSesion = vi.fn();
  await TestBed.configureTestingModule({
    imports: [AdminPortal],
    providers: [
      provideRouter([]),
      { provide: PendingActionsService, useValue: pendientes },
      { provide: AuthService, useValue: { getUsuario: () => null, isSuperAdmin: () => true, cerrarSesion } },
    ],
  })
    .overrideComponent(AdminPortal, { set: { template: '', imports: [] } })
    .compileComponents();
  const fixture = TestBed.createComponent(AdminPortal);
  fixture.detectChanges();
  return { fixture, pendientes, cerrarSesion };
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
