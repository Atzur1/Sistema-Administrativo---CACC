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
    request.flush({ email: 'admin@cacc.com', role: 1, token });

    const fresh = new AuthService(TestBed.inject(HttpClient));
    expect(fresh.isAuthenticated()).toBe(true);
  });

  function iniciarSesion(role: number): string {
    const token = `header.${btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + 600 }))}.signature`;
    service.login({ username: '20111222', password: 'test-password' }).subscribe();
    http.expectOne('/api/auth/login').flush({ email: 'cuenta@cacc.com', role, token });
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
});
