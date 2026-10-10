import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { authInterceptor } from './auth-interceptor';

describe('authInterceptor', () => {
  let http: HttpClient;
  let httpTesting: HttpTestingController;
  let router: Router;

  const token = `header.${btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + 600 }))}.signature`;

  beforeEach(() => {
    sessionStorage.clear();
    sessionStorage.setItem(
      'cacc-session',
      JSON.stringify({ email: 'admin@cacc.com', rol: 1, token }),
    );

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });

    http = TestBed.inject(HttpClient);
    httpTesting = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => {
    httpTesting.verify();
    sessionStorage.clear();
  });

  it('clears a revoked session and redirects to login with an explanation', () => {
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    http.get('/api/jugadores').subscribe({ error: () => {} });

    const request = httpTesting.expectOne('/api/jugadores');
    expect(request.request.headers.get('Authorization')).toBe(`Bearer ${token}`);
    request.flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(sessionStorage.getItem('cacc-session')).toBeNull();
    expect(navigate).toHaveBeenCalledWith(['/'], { queryParams: { sesion: 'vencida' } });
  });

  it('never sends the token to another origin', () => {
    http.get('https://otro-sitio.example.com/api/jugadores').subscribe();

    const request = httpTesting.expectOne('https://otro-sitio.example.com/api/jugadores');
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({});
  });

  it('attaches the token to the logout request so the server can revoke it', () => {
    http.post('/api/auth/logout', null).subscribe();

    const request = httpTesting.expectOne('/api/auth/logout');
    expect(request.request.headers.get('Authorization')).toBe(`Bearer ${token}`);
    request.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('does not attach the stale token to public login requests', () => {
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    http.post('/api/auth/login', { usuario: 'admin@cacc.com', contrasena: 'incorrecta' }).subscribe({
      error: () => {},
    });

    const request = httpTesting.expectOne('/api/auth/login');
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(sessionStorage.getItem('cacc-session')).not.toBeNull();
    expect(navigate).not.toHaveBeenCalled();
  });
});
