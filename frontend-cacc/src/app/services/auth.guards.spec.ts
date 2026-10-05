import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { AuthService, FINANCIAL_ACCESS_DENIED, adminGuard } from './auth';

// HU-022: acceso al panel de Cuotas por URL directa. Se arma un árbol de rutas
// con la misma forma que app.routes.ts (adminGuard en el padre, el mensaje en el hijo).

@Component({ template: '' })
class Blank {}

function setup(rol: number | null) {
  const auth = {
    isAuthenticated: () => rol !== null,
    isAdmin: () => rol === 1 || rol === 2,
  };

  TestBed.configureTestingModule({
    providers: [
      { provide: AuthService, useValue: auth },
      provideRouter([
        { path: '', component: Blank },
        { path: 'portales', component: Blank },
        {
          path: 'admin/portal',
          component: Blank,
          canActivate: [adminGuard],
          children: [
            { path: 'cuotas-pagos', component: Blank, data: { accesoDenegado: FINANCIAL_ACCESS_DENIED } },
            { path: 'deudas-morosidad', component: Blank },
          ],
        },
      ]),
    ],
  });
}

describe('adminGuard - panel de Cuotas (HU-022)', () => {
  let alertSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    alertSpy = vi.spyOn(window, 'alert').mockImplementation(() => {});
  });

  afterEach(() => alertSpy.mockRestore());

  it('rejects a non-admin user who forces the URL, with the financial access-denied message', async () => {
    setup(3);
    const harness = await RouterTestingHarness.create();

    await harness.navigateByUrl('/admin/portal/cuotas-pagos');

    expect(alertSpy).toHaveBeenCalledWith(FINANCIAL_ACCESS_DENIED);
    expect(TestBed.inject(Router).url).toBe('/portales');
  });

  it('keeps the generic message for non-financial sections', async () => {
    setup(3);
    const harness = await RouterTestingHarness.create();

    await harness.navigateByUrl('/admin/portal/deudas-morosidad');

    expect(alertSpy).toHaveBeenCalledWith('No tienes permisos para acceder al Portal Administrativo.');
  });

  it('sends an anonymous user to the login', async () => {
    setup(null);
    const harness = await RouterTestingHarness.create();

    await harness.navigateByUrl('/admin/portal/cuotas-pagos');

    expect(alertSpy).toHaveBeenCalledWith('Debes iniciar sesión para acceder a esta sección.');
    expect(TestBed.inject(Router).url).toBe('/');
  });

  it.each([1, 2])('lets role %i into the panel', async (rol) => {
    setup(rol);
    const harness = await RouterTestingHarness.create();

    await harness.navigateByUrl('/admin/portal/cuotas-pagos');

    expect(alertSpy).not.toHaveBeenCalled();
    expect(TestBed.inject(Router).url).toBe('/admin/portal/cuotas-pagos');
  });
});
