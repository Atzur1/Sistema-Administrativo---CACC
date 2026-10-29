import { TestBed } from '@angular/core/testing';

import { AuthService } from './auth';
import { navegador } from './navegador';
import { environment } from '../../environments/environment';
import { Router } from '@angular/router';
import { HttpClient, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

describe('AuthService', () => {
  let service: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    sessionStorage.clear();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('restores a valid session after the service is recreated', () => {
    const token = `header.${btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + 600 }))}.signature`;
    service.login({ username: 'admin@cacc.com', password: 'test-password' }).subscribe();
    const request = http.expectOne('/api/auth/login');
    expect(request.request.body).toEqual({ username: 'admin@cacc.com', password: 'test-password' });
    request.flush({ email: 'admin@cacc.com', role: 1, token, portales: ['administrativo', 'deportivo', 'personas'] });

    const fresh = new AuthService(TestBed.inject(HttpClient));
    expect(fresh.isAuthenticated()).toBe(true);
  });

  // Portales que el backend le asigna a cada rol (ApiGestion/Services/PortalesDeLaCuenta.cs).
  const PORTALES_DE: Record<number, string[]> = {
    0: ['deportivo'],
    1: ['administrativo', 'deportivo', 'personas'],
    2: ['administrativo'],
  };

  function iniciarSesion(role: number, portales: string[] = PORTALES_DE[role] ?? []): string {
    const token = `header.${btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + 600 }))}.signature`;
    service.login({ username: '20111222', password: 'test-password' }).subscribe();
    http.expectOne('/api/auth/login').flush({ email: 'cuenta@cacc.com', role, token, portales });
    return token;
  }

  describe('paso al Portal Deportivo (inicio de sesión único)', () => {
    afterEach(() => vi.restoreAllMocks());

    it('entrega la sesión en el fragmento de la dirección, no en la query', () => {
      const irA = vi.spyOn(navegador, 'irA').mockImplementation(() => {});
      const token = iniciarSesion(1);

      expect(service.abrirPortalDeportivo()).toBe(true);

      expect(irA).toHaveBeenCalledWith(`${environment.deportivoUrl}/login#sso=${encodeURIComponent(token)}`);
      expect(irA.mock.calls[0][0]).not.toContain('?');
    });

    it('puede abrir el Deportivo directo en una pantalla (por ejemplo, el alta de personas)', () => {
      const irA = vi.spyOn(navegador, 'irA').mockImplementation(() => {});
      const token = iniciarSesion(1);

      service.abrirPortalDeportivo('/admin/registro-personas');

      expect(irA).toHaveBeenCalledWith(
        `${environment.deportivoUrl}/login?returnUrl=${encodeURIComponent('/admin/registro-personas')}#sso=${encodeURIComponent(token)}`,
      );
    });

    it('sin sesión no abre el Portal Deportivo', () => {
      const irA = vi.spyOn(navegador, 'irA').mockImplementation(() => {});
      expect(service.abrirPortalDeportivo()).toBe(false);
      expect(irA).not.toHaveBeenCalled();
    });

    it('una cuenta sin acceso al Administrativo (rol 0) va directo al Portal Deportivo', () => {
      const irA = vi.spyOn(navegador, 'irA').mockImplementation(() => {});
      const router = { navigate: vi.fn() } as unknown as Router;
      iniciarSesion(0);

      service.irAlInicio(router);

      expect(irA).toHaveBeenCalledTimes(1);
      expect(router.navigate).not.toHaveBeenCalled();
    });

    it('el SuperAdmin elige portal y el Administrador va directo al Administrativo', () => {
      const irA = vi.spyOn(navegador, 'irA').mockImplementation(() => {});
      const router = { navigate: vi.fn() } as unknown as Router;

      iniciarSesion(1);
      service.irAlInicio(router);
      expect(router.navigate).toHaveBeenLastCalledWith(['/portales']);

      service.logout();
      iniciarSesion(2);
      service.irAlInicio(router);
      expect(router.navigate).toHaveBeenLastCalledWith(['/admin/portal']);
      expect(irA).not.toHaveBeenCalled();
    });
  });

  // HU-007: el destino sale de los portales que manda el backend, no del nombre del rol.
  describe('destino después del login (HU-007)', () => {
    afterEach(() => vi.restoreAllMocks());

    it('quien solo tiene el Deportivo (Director Técnico, Preparador Físico) va directo, sin pasar por ninguna ruta del Administrativo', () => {
      const irA = vi.spyOn(navegador, 'irA').mockImplementation(() => {});
      const router = { navigate: vi.fn() } as unknown as Router;
      iniciarSesion(0, ['deportivo']);

      expect(service.destinoInicial()).toBe('deportivo');
      expect(service.irAlInicio(router)).toBe(true);

      expect(irA).toHaveBeenCalledTimes(1);
      expect(irA.mock.calls[0][0]).toContain(`${environment.deportivoUrl}/login#sso=`);
      expect(router.navigate).not.toHaveBeenCalled();
    });

    it('con varios portales que incluyen el Administrativo se muestra la elección de portales', () => {
      iniciarSesion(1);
      expect(service.destinoInicial()).toBe('/portales');
    });

    it('sin ningún portal no se lo manda al Administrativo: se cierra la sesión y vuelve al login con el aviso', () => {
      const irA = vi.spyOn(navegador, 'irA').mockImplementation(() => {});
      const router = { navigate: vi.fn() } as unknown as Router;
      iniciarSesion(0, []);

      expect(service.destinoInicial()).toBeNull();
      expect(service.irAlInicio(router)).toBe(false);

      expect(service.isAuthenticated()).toBe(false);
      expect(router.navigate).toHaveBeenCalledWith(['/'], { queryParams: { sesion: 'sin-acceso' } });
      expect(irA).not.toHaveBeenCalled();
    });

    it('una sesión guardada sin portales (formato anterior) se descarta: hay que volver a iniciar sesión', () => {
      const token = `header.${btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + 600 }))}.signature`;
      sessionStorage.setItem('cacc-session', JSON.stringify({ email: 'cuenta@cacc.com', role: 0, token }));

      const fresh = new AuthService(TestBed.inject(HttpClient));

      expect(fresh.isAuthenticated()).toBe(false);
    });

    // Quien fuerza una URL del Administrativo sin permiso: un solo salto, nunca de vuelta a una ruta del Administrativo.
    it('al salir del Administrativo, quien tiene el Deportivo va directo allá', () => {
      const irA = vi.spyOn(navegador, 'irA').mockImplementation(() => {});
      const router = { createUrlTree: vi.fn() } as unknown as Router;
      iniciarSesion(0, ['deportivo']);

      expect(service.salirDelAdministrativo(router)).toBe(false);

      expect(irA).toHaveBeenCalledTimes(1);
      expect(router.createUrlTree).not.toHaveBeenCalled();
    });

    it('al salir del Administrativo, una sesión que no tiene a dónde ir se cierra (sin loop entre guards)', () => {
      const tree = {};
      const router = { createUrlTree: vi.fn(() => tree) } as unknown as Router;
      iniciarSesion(3, ['administrativo']);

      expect(service.salirDelAdministrativo(router)).toBe(tree);

      expect(service.isAuthenticated()).toBe(false);
      expect(router.createUrlTree).toHaveBeenCalledWith(['/'], { queryParams: { sesion: 'sin-acceso' } });
    });
  });
});
