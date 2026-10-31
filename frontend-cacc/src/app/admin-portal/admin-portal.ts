import { Component, NgZone, OnInit, DestroyRef, HostListener, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { RouterOutlet, RouterLink, Router, NavigationEnd } from '@angular/router';
import { AuthService } from '../services/auth';
import { PORTAL_ADMINISTRATIVO, SECCIONES, Seccion, esSeccion } from './secciones';
import { filter } from 'rxjs/operators';
import { Toast } from '../shared/toast/toast';
import { NotificationBell } from '../shared/notification-bell/notification-bell';
import { DtAdminChat } from '../shared/dt-admin-chat/dt-admin-chat';

@Component({
    selector: 'app-admin-portal',
    standalone: true,
    imports: [CommonModule, RouterOutlet, RouterLink, Toast, NotificationBell, DtAdminChat],
    templateUrl: './admin-portal.html',
    styleUrl: './admin-portal.css',
})
export class AdminPortal implements OnInit {

  // Topbar properties
currentPageTitle: string = '';
get userName(): string {
    const user = this.authService.getUsuario();
    return user ? [user.firstName, user.lastName].filter(Boolean).join(' ') || user.email.split('@')[0] : '';
}
get userEmail(): string { return this.authService.getUsuario()?.email ?? ''; }
get userInitials(): string { return this.userName.slice(0, 2).toUpperCase(); }
get isSuperAdmin(): boolean { return this.authService.isSuperAdmin(); }
get roleLabel(): string { return this.isSuperAdmin ? 'Superadmin' : 'Administrador'; }

// Off-canvas sidebar state, only meaningful under the 768px breakpoint
sidebarOpen: boolean = false;

// Nombres de las secciones (HU-103): los mismos en el menú, la barra superior y el h1 de cada pantalla.
readonly portal = PORTAL_ADMINISTRATIVO;
readonly secciones = SECCIONES;
// Ítem del menú que corresponde a la pantalla actual. Sale de la ruta activa, no de comparar textos de la URL:
// así una pantalla de detalle (jugadores/:id/deuda) mantiene marcada su sección y nunca quedan dos ítems activos.
seccionActiva: Seccion | null = null;
// Último tramo de la barra superior en las pantallas de detalle ("Deuda Pendiente"); null en las secciones.
subpagina: string | null = null;

private destroyRef = inject(DestroyRef);

constructor(
    private router: Router,
    private authService: AuthService,
    private ngZone: NgZone
) {}

ngOnInit() {
    // Update page title on every navigation event.
    // takeUntilDestroyed unsubscribes when the shell is destroyed, so leaving
    // and re-entering the portal does not stack one live subscription per visit.
this.router.events
    .pipe(
        filter(event => event instanceof NavigationEnd),
        takeUntilDestroyed(this.destroyRef)
    )
    .subscribe(() => {
        this.updatePageTitle();
        // Tapping a link on mobile navigates and closes the drawer
        this.sidebarOpen = false;
    });

    // Set initial title on load
    this.updatePageTitle();
    }

  // La pantalla actual es la ruta hija más profunda. Su sección es data.seccion (pantallas de detalle) o su propio
  // path (secciones); el id de un jugador, los query params o un #fragmento no cambian nada.
private updatePageTitle() {
    let ruta = this.router.routerState.snapshot.root;
    while (ruta.firstChild) ruta = ruta.firstChild;

    const seccion = ruta.data['seccion'] ?? ruta.routeConfig?.path;
    this.seccionActiva = esSeccion(seccion) ? seccion : null;
    this.subpagina = typeof ruta.data['subpagina'] === 'string' ? ruta.data['subpagina'] : null;
    this.currentPageTitle = this.seccionActiva ? SECCIONES[this.seccionActiva] : '';
}

esActiva(seccion: Seccion): boolean {
    return this.seccionActiva === seccion;
}

// Open/close the mobile sidebar drawer.
// ngZone.run(): AdminPortal es el shell persistente de todo el portal — si
// alguna pantalla hija (ej. los gráficos de Chart.js en Resumen General o
// Actualización de Aranceles) saca el render a runOutsideAngular y la
// navegación no vuelve a entrar a la zona, estos clicks cambian el estado
// pero Angular no repinta hasta el próximo evento que sí corra adentro —
// por eso "no pasaba nada" hasta recargar la página.
toggleSidebar() {
    this.ngZone.run(() => (this.sidebarOpen = !this.sidebarOpen));
}

closeSidebar() {
    this.ngZone.run(() => (this.sidebarOpen = false));
}

// Esc cierra el menú lateral en celulares (HU-098), como cualquier otro menú del portal.
@HostListener('document:keydown.escape')
cerrarSidebarConEsc() {
    if (this.sidebarOpen) this.closeSidebar();
}
// Go back to the portals selection screen
cambiarPortal() {
    this.router.navigate(['/portales']);
}

// Log out and return to the login screen
cerrarSesion() {
    this.authService.cerrarSesion();
    this.router.navigate(['/']);
    }
}
