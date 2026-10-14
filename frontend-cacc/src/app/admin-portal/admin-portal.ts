import { Component, NgZone, OnInit, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { RouterOutlet, RouterLink, RouterLinkActive, Router, NavigationEnd } from '@angular/router';
import { AuthService } from '../services/auth';
import { filter } from 'rxjs/operators';
import { Toast } from '../shared/toast/toast';
import { NotificationBell } from '../shared/notification-bell/notification-bell';
import { DtAdminChat } from '../shared/dt-admin-chat/dt-admin-chat';

const PINNED_KEY = 'menuFijado';

function readPinned(): boolean {
    try {
        return localStorage.getItem(PINNED_KEY) === '1';
    } catch {
        return false;
    }
}

@Component({
    selector: 'app-admin-portal',
    standalone: true,
    imports: [CommonModule, RouterOutlet, RouterLink, RouterLinkActive, Toast, NotificationBell, DtAdminChat],
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

// Menú fijado abierto (solo escritorio). Se recuerda en este navegador; si el
// almacenamiento no está disponible, el menú simplemente arranca sin fijar.
sidebarPinned: boolean = readPinned();

// Maps each route segment to its display name
private pageTitles: Record<string, string> = {
    'resumen-general':        'Resumen General',
    'actividad-movimientos':  'Actividad y Movimientos',
    'auditoria': 'Auditoría',
    'deudas-morosidad':       'Deudas y Morosidad',
    'usuarios':               'Usuarios y Permisos',
    'cuotas-pagos':           'Cuotas y Pagos',
    'reportes':               'Reportes',
    'actualizacion-aranceles': 'Actualización de Aranceles',
    'becados-descuentos':     'Becados y Descuentos',
    'recaudacion-mensual':    'Recaudación Mensual',
};

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

  // Reads the last URL segment and maps it to a display name.
  // "jugadores/:id" has a numeric id as its last segment, so it's matched by the
  // second-to-last segment instead — otherwise the topbar would show a raw id.
private updatePageTitle() {
    const urlSegments = this.router.url.split('/');
    const lastSegment = urlSegments[urlSegments.length - 1];

    if (lastSegment === 'deuda' && urlSegments[urlSegments.length - 3] === 'jugadores') {
        this.currentPageTitle = 'Deuda Pendiente';
        return;
    }

    if (urlSegments[urlSegments.length - 2] === 'jugadores') {
        this.currentPageTitle = 'Perfil de Jugador';
        return;
    }

    this.currentPageTitle = this.pageTitles[lastSegment] ?? '';
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

// Fija o suelta el menú en escritorio. Mismo ngZone.run() que arriba: este shell
// persistente no siempre repinta solo después de un click.
togglePinned() {
    this.ngZone.run(() => {
        this.sidebarPinned = !this.sidebarPinned;
        try {
            localStorage.setItem(PINNED_KEY, this.sidebarPinned ? '1' : '0');
        } catch {
            // Sin almacenamiento (modo privado, datos bloqueados): se aplica igual en esta sesión.
        }
    });
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
