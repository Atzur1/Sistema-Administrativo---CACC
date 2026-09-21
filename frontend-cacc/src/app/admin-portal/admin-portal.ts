import { Component, OnInit, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { RouterOutlet, RouterLink, RouterLinkActive, Router, NavigationEnd } from '@angular/router';
import { AuthService } from '../services/auth';
import { filter } from 'rxjs/operators';
import { Toast } from '../shared/toast/toast';
import { NotificationBell } from '../shared/notification-bell/notification-bell';

@Component({
    selector: 'app-admin-portal',
    standalone: true,
    imports: [CommonModule, RouterOutlet, RouterLink, RouterLinkActive, Toast, NotificationBell],
    templateUrl: './admin-portal.html',
    styleUrl: './admin-portal.css',
})
export class AdminPortal implements OnInit {

  // Topbar properties
currentPageTitle: string = '';
userName: string = '';
userEmail: string = '';
userInitials: string = '';

// Off-canvas sidebar state, only meaningful under the 768px breakpoint
sidebarOpen: boolean = false;

// Modo compacto de escritorio (icon-only): decisión de cada admin sobre su
// propia pantalla, así que se guarda en localStorage — no es estado del
// servidor ni algo que otro usuario deba compartir.
private static readonly SIDEBAR_COLLAPSED_KEY = 'cacc_sidebar_collapsed';
sidebarCollapsed: boolean = false;

// Maps each route segment to its display name
private pageTitles: Record<string, string> = {
    'resumen-general':        'Resumen General',
    'actividad-movimientos':  'Actividad y Movimientos',
    'deudas-morosidad':       'Deudas y Morosidad',
    'usuarios':               'Usuarios y Permisos',
    'cuotas-pagos':           'Cuotas y Pagos',
    'reportes':               'Reportes',
    'actualizacion-aranceles': 'Actualización de Aranceles',
};

private destroyRef = inject(DestroyRef);

constructor(
    private router: Router,
    private authService: AuthService
) {}

ngOnInit() {
    this.sidebarCollapsed = localStorage.getItem(AdminPortal.SIDEBAR_COLLAPSED_KEY) === 'true';

    // Load user data from session
    const usuario = this.authService.getUsuario();
    if (usuario) {
    this.userEmail = usuario.email;
    // Use email prefix as display name (e.g. "admin" from "admin@cacc.com")
    this.userName = usuario.email.split('@')[0];
    this.userInitials = this.userName.slice(0, 2).toUpperCase();
    }

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

// Open/close the mobile sidebar drawer
toggleSidebar() {
    this.sidebarOpen = !this.sidebarOpen;
}

closeSidebar() {
    this.sidebarOpen = false;
}

// Colapsa el sidebar a solo íconos en escritorio, para aprovechar el ancho
// de pantalla en tablas y grillas que lo necesitan (Deudas y Morosidad,
// Reportes, etc.). No tiene efecto en el drawer móvil, que es otro mecanismo.
toggleCollapse() {
    this.sidebarCollapsed = !this.sidebarCollapsed;
    localStorage.setItem(AdminPortal.SIDEBAR_COLLAPSED_KEY, String(this.sidebarCollapsed));
}

// Go back to the portals selection screen
cambiarPortal() {
    this.router.navigate(['/portales']);
}

// Log out and return to the login screen
cerrarSesion() {
    this.authService.logout();
    this.router.navigate(['/']);
    }
}