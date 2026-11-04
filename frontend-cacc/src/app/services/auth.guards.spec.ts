import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { AuthService, FINANCIAL_ACCESS_DENIED, adminGuard, portalesGuard } from './auth';

// HU-022: acceso al panel de Cuotas por URL directa. Se arma un árbol de rutas
// con la misma forma que app.routes.ts (adminGuard en el padre, el mensaje en el hijo).

@Component({ template: '' })
class Blank {}

// salirDelAdministrativo: con el Deportivo navega fuera del sitio (devuelve false); sin ningún portal cierra la
// sesión y vuelve al login. Se simula con el mismo contrato que AuthService.
function setup(rol: number | null, portales: string[] = []) {
  const auth = {
    isAuthenticated: () => rol !== null,
    isAdmin: () => rol === 1 || rol === 2,
    salirDelAdministrativo: vi.fn((router: Router) =>
      portales.includes('deportivo') ? false : router.createUrlTree(['/'], { queryParams: { sesion: 'sin-acceso' } }),
    ),
    destinoInicial: () => (rol === 1 ? '/portales' : rol === 2 ? '/admin/portal' : portales.includes('deportivo') ? 'deportivo' : null),
    irAlInicio: vi.fn(() => portales.includes('deportivo')),
  };

  TestBed.configureTestingModule({
    providers: [
      { provide: AuthService, useValue: auth },
      provideRouter([
        { path: '', component: Blank },
        { path: 'portales', component: Blank, canActivate: [portalesGuard] },
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
  return auth;
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
    expect(TestBed.inject(Router).url).toBe('/?sesion=sin-acceso');
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

// HU-007: un Director Técnico o Preparador Físico (solo el Deportivo) que escribe una URL del Administrativo.
describe('adminGuard y portalesGuard - cuenta solo del Deportivo (HU-007)', () => {
  let alertSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    alertSpy = vi.spyOn(window, 'alert').mockImplementation(() => {});
  });

  afterEach(() => alertSpy.mockRestore());

  it('una URL del Administrativo se rechaza con el aviso y va directo al Deportivo, sin pasar por la elección de portales', async () => {
    const auth = setup(0, ['deportivo']);
    const harness = await RouterTestingHarness.create();

    await harness.navigateByUrl('/admin/portal/deudas-morosidad');

    expect(alertSpy).toHaveBeenCalledWith('No tienes permisos para acceder al Portal Administrativo.');
    expect(auth.salirDelAdministrativo).toHaveBeenCalledTimes(1);
    expect(auth.irAlInicio).not.toHaveBeenCalled();
    expect(TestBed.inject(Router).url).not.toContain('/admin/portal');
    expect(TestBed.inject(Router).url).not.toBe('/portales');
  });

  it('la elección de portales tampoco se le muestra: va directo al Deportivo', async () => {
    const auth = setup(0, ['deportivo']);
    const harness = await RouterTestingHarness.create();

    await harness.navigateByUrl('/portales');

    expect(auth.irAlInicio).toHaveBeenCalledTimes(1);
    expect(TestBed.inject(Router).url).not.toBe('/portales');
  });

  it('el SuperAdmin sí ve la elección de portales', async () => {
    setup(1, ['administrativo', 'deportivo', 'personas']);
    const harness = await RouterTestingHarness.create();

    await harness.navigateByUrl('/portales');

    expect(TestBed.inject(Router).url).toBe('/portales');
  });
});
