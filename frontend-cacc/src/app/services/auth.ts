import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router, CanActivateFn, ActivatedRouteSnapshot, UrlTree } from '@angular/router';
import { Observable, tap } from 'rxjs';
import { API_BASE_URL } from './api-url';
import { environment } from '../../environments/environment';
import { navegador } from './navegador';
import { LoginModel, LoginResponseModel } from '../models/LoginModel';

const SESSION_KEY = 'cacc-session';

// HU-007: valor de ?sesion= con el que el login muestra NO_TIENE_ACCESOS.
export const SIN_ACCESO = 'sin-acceso';
export const NO_TIENE_ACCESOS = 'Tu cuenta no tiene accesos habilitados.';

// HU-022: mensaje para quien fuerza la URL de una sección financiera sin rol
// administrativo. Se declara en la ruta (data.accesoDenegado) y lo lee el guard.
export const FINANCIAL_ACCESS_DENIED =
  'Acceso Denegado: No cuenta con permisos suficientes para acceder al panel financiero.';

// adminGuard corre en el padre /admin/portal, antes que los hijos: para saber a qué
// sección se intentaba entrar hay que bajar hasta la ruta hoja del snapshot.
function accesoDenegadoDe(root: ActivatedRouteSnapshot): string | null {
  let route: ActivatedRouteSnapshot | null = root;
  let mensaje: string | null = null;
  while (route) {
    mensaje = (route.data?.['accesoDenegado'] as string | undefined) ?? mensaje;
    route = route.firstChild;
  }
  return mensaje;
}

@Injectable({
  providedIn: 'root',
})
export class AuthService {
  private readonly API_URL = `${API_BASE_URL}/auth/login`;
  private usuario: LoginResponseModel | null = null;

  constructor(private http: HttpClient) {
    this.usuario = this.restoreSession();
  }

  login(credentials: LoginModel): Observable<LoginResponseModel> {
    return this.http.post<LoginResponseModel>(this.API_URL, credentials).pipe(
      tap((respuesta) => {
        this.usuario = respuesta;
        sessionStorage.setItem(SESSION_KEY, JSON.stringify(respuesta));
      }),
    );
  }

  // `usuario` es el DNI de la cuenta (o, durante la transición, su correo); el enlace llega al correo registrado.
  requestPasswordReset(usuario: string): Observable<{ mensaje: string }> {
    return this.http.post<{ mensaje: string }>(`${API_BASE_URL}/auth/password-reset/request`, {
      usuario,
    });
  }

  completeAccountAccess(token: string, password: string): Observable<{ mensaje: string }> {
    return this.http.post<{ mensaje: string }>(`${API_BASE_URL}/auth/account-access/complete`, { token, password });
  }

  getUsuario(): LoginResponseModel | null {
    return this.usuario;
  }

  getRol(): number | null {
    return this.getUsuario()?.role ?? null;
  }

  getToken(): string | null {
    return this.getUsuario()?.token ?? null;
  }

  isAuthenticated(): boolean {
    return this.getUsuario() !== null;
  }

  // Cualquiera de los dos roles del portal administrativo: SuperAdmin (1) o
  // Administrador (2, acceso acotado a Cuotas y Pagos / Deudas y Morosidad /
  // Becados y Descuentos). Para lo que es exclusivo de SuperAdmin, usar isSuperAdmin().
  isAdmin(): boolean {
    return this.getRol() === 1 || this.getRol() === 2;
  }

  isSuperAdmin(): boolean {
    return this.getRol() === 1;
  }

  // HU-007: el destino sale de los portales que manda el backend (PortalesDeLaCuenta), no del nombre del rol:
  //  - varios portales con el Administrativo (el SuperAdmin): la elección de portales.
  //  - solo el Administrativo (el Administrador): directo al Portal Administrativo.
  //  - solo el Deportivo (Director Técnico, Preparador Físico, Médico, Don QR…): directo al Portal Deportivo,
  //    sin renderizar nada del Administrativo.
  //  - ninguno: null. Nunca se cae en el Administrativo por defecto.
  destinoInicial(): '/portales' | '/admin/portal' | 'deportivo' | null {
    const portales = this.getUsuario()?.portales ?? [];
    if (portales.includes('administrativo')) return portales.length > 1 ? '/portales' : '/admin/portal';
    if (portales.includes('deportivo')) return 'deportivo';
    return null;
  }

  // Donde cae cada cuenta recien logueada (y a donde se la devuelve si pide una pantalla que no es suya).
  // false: no hay a dónde ir; la sesión se cierra y vuelve al login con el aviso.
  irAlInicio(router: Router): boolean {
    const destino = this.destinoInicial();
    if (destino === 'deportivo') return this.abrirPortalDeportivo();
    if (destino) {
      router.navigate([destino]);
      return true;
    }
    this.logout();
    router.navigate(['/'], { queryParams: { sesion: SIN_ACCESO } });
    return false;
  }

  // Para adminGuard, cuando una cuenta sin rol administrativo pide una ruta del Administrativo: un solo salto y nunca
  // de vuelta a otra ruta del Administrativo (si no, los guards se mandarían la cuenta entre sí sin fin).
  salirDelAdministrativo(router: Router): false | UrlTree {
    if (this.destinoInicial() === 'deportivo' && this.abrirPortalDeportivo()) return false;
    this.logout();
    return router.createUrlTree(['/'], { queryParams: { sesion: SIN_ACCESO } });
  }

  // El Portal Deportivo es otro sitio y no comparte el almacenamiento del navegador: se le entrega la sesión en el
  // fragmento de la dirección (#sso=<token>), que no viaja al servidor. Su pantalla de ingreso la recibe y la borra de la barra.
  // `destino` es una ruta del Deportivo a la que ir al entrar (por ejemplo '/admin/registro-personas').
  abrirPortalDeportivo(destino?: string): boolean {
    const token = this.getToken();
    if (!token) return false;
    const volverA = destino ? `?returnUrl=${encodeURIComponent(destino)}` : '';
    navegador.irA(`${environment.deportivoUrl.replace(/\/+$/, '')}/login${volverA}#sso=${encodeURIComponent(token)}`);
    return true;
  }

  // Solo borra la sesión del navegador (lo usa el interceptor cuando el servidor ya rechazó el token).
  logout() {
    this.usuario = null;
    sessionStorage.removeItem(SESSION_KEY);
  }

  // Cierre de sesión elegido por el usuario: además le pide al servidor que invalide el token, que si no
  // seguiría sirviendo hasta su vencimiento. Si el servidor no responde, la sesión local se cierra igual.
  cerrarSesion() {
    const token = this.getToken();
    if (token) {
      this.http
        .post(`${API_BASE_URL}/auth/logout`, null, { headers: { Authorization: `Bearer ${token}` } })
        .subscribe({ error: () => {} });
    }
    this.logout();
  }

  // A session saved with the previous shape (rol instead of role) fails the check and is discarded: the user
  // just signs in again.
  private restoreSession(): LoginResponseModel | null {
    const raw = sessionStorage.getItem(SESSION_KEY);
    if (!raw) return null;
    try {
      const session = JSON.parse(raw) as LoginResponseModel;
      const payload = session.token.split('.')[1];
      if (!payload || !session.email || !session.token || !Number.isInteger(session.role) || !Array.isArray(session.portales))
        throw new Error('Invalid session');
      const base64 = payload.replace(/-/g, '+').replace(/_/g, '/');
      const claims = JSON.parse(atob(base64.padEnd(Math.ceil(base64.length / 4) * 4, '=')));
      if (!Number.isFinite(claims.exp) || claims.exp * 1000 <= Date.now())
        throw new Error('Expired session');
      return session;
    } catch {
      sessionStorage.removeItem(SESSION_KEY);
      return null;
    }
  }
}

// Exige sesión iniciada, sin importar el rol. Para pantallas que cualquier
// usuario logueado puede ver (ej. "/portales", la elección de entorno) —
// a diferencia de adminGuard, que además exige rol Admin.
export const authGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (!authService.isAuthenticated()) {
    alert('Debes iniciar sesión para acceder a esta sección.');
    router.navigate(['/']);
    return false;
  }

  return true;
};

// Pantalla de eleccion de portales: la ve unicamente el SuperAdmin. Cualquier otra cuenta va directo a su portal.
export const portalesGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (!authService.isAuthenticated()) {
    alert('Debes iniciar sesión para acceder a esta sección.');
    router.navigate(['/']);
    return false;
  }

  if (authService.destinoInicial() !== '/portales') {
    authService.irAlInicio(router);
    return false;
  }

  return true;
};

export const adminGuard: CanActivateFn = (_route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (!authService.isAuthenticated()) {
    alert('Debes iniciar sesión para acceder a esta sección.');
    router.navigate(['/']);
    return false;
  }

  // HU-007: quien solo tiene el Deportivo va directo allá (antes pasaba por /portales y de ahí al Deportivo).
  if (!authService.isAdmin()) {
    alert(accesoDenegadoDe(state.root) ?? 'No tienes permisos para acceder al Portal Administrativo.');
    return authService.salirDelAdministrativo(router);
  }

  return true;
};

// Para las secciones que el Administrador (rol 2) no puede ver (todo lo que no
// sea Cuotas y Pagos / Deudas y Morosidad / Becados y Descuentos). adminGuard ya
// corrió en el padre /admin/portal, así que acá solo hace falta exigir SuperAdmin.
export const superAdminGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (!authService.isSuperAdmin()) {
    alert('No tienes permisos para acceder a esta sección.');
    router.navigate(['/admin/portal']);
    return false;
  }

  return true;
};

// Landing del portal administrativo al entrar en /admin/portal sin sub-ruta:
// SuperAdmin va a Resumen General, Administrador va directo a Cuotas y Pagos
// (no tiene Resumen General en su alcance).
export const adminHomeRedirect: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  const destino = authService.isSuperAdmin() ? 'resumen-general' : 'cuotas-pagos';
  router.navigate(['/admin/portal', destino]);
  return false;
};
