import { Component, ElementRef, HostListener, NgZone, ViewChild, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { NotificationService } from '../notifications/notification.service';
import { AuthService } from '../../services/auth';
import { PendingActionsService } from '../../services/pending-actions';

// La campanita tiene dos cosas distintas, que no se mezclan:
//  - Pendientes de acción (HU-104, solo SuperAdmin): meses con cuotas sin generar y personas que esperan acceso al
//    portal. Salen del coordinador de pendientes y solo bajan cuando se resuelven (no al abrir ni al leer). El globo
//    del SuperAdmin cuenta estos pendientes.
//  - Historial: las notificaciones que ya se mostraron como toast (pagos, beneficios, aranceles). No desaparece solo,
//    para poder repasar "¿qué guardé recién?". Abrir la campanita lo marca como leído y "Limpiar historial" lo vacía,
//    sin tocar los pendientes. Para el Administrador, el globo sigue contando su historial sin leer, como siempre.
@Component({
    selector: 'app-notification-bell',
    standalone: true,
    imports: [CommonModule, RouterLink],
    templateUrl: './notification-bell.html',
    styleUrl: './notification-bell.css',
})
export class NotificationBell {
    private notifications = inject(NotificationService);
    private auth = inject(AuthService);
    private ngZone = inject(NgZone);
    readonly pendientes = inject(PendingActionsService);

    @ViewChild('disparador') private disparador?: ElementRef<HTMLButtonElement>;

    readonly panelId = 'panel-notificaciones';

    readonly items = this.notifications.notifications;
    readonly unreadCount = this.notifications.unreadCount;

    readonly cuotasPendientes = computed(() => this.pendientes.items().filter((p) => p.kind === 'missing-fees'));
    readonly candidatosPendientes = computed(() => this.pendientes.items().filter((p) => p.kind === 'candidate'));

    isOpen = signal(false);

    constructor(private elementRef: ElementRef<HTMLElement>) {}

    // Los pendientes de esta HU son solo del SuperAdmin: a otro rol no se le muestran (ni se consultan).
    get esSuperAdmin(): boolean {
        return this.auth.isSuperAdmin();
    }

    // Lo que cuenta el globo: pendientes para el SuperAdmin; historial sin leer para el resto.
    get cantidadGlobo(): number {
        return this.esSuperAdmin ? this.pendientes.total() : this.unreadCount();
    }

    // Con muchos, el globo dice "9+", pero el nombre del botón y el panel dicen la cantidad real.
    get nombreBoton(): string {
        if (this.esSuperAdmin) {
            const n = this.pendientes.total();
            const incompleto = this.pendientes.status() === 'partial' ? ' (datos incompletos)' : '';
            return `Notificaciones: ${this.textoPendientes(n)}${incompleto}`;
        }
        const sinLeer = this.unreadCount();
        return sinLeer > 0 ? `Notificaciones: ${sinLeer} sin leer` : 'Notificaciones';
    }

    textoPendientes(n: number): string {
        return `${n} ${n === 1 ? 'pendiente' : 'pendientes'} de acción`;
    }

    // Todavía no hay nada para mostrar de ninguna fuente.
    get buscando(): boolean {
        const estado = this.pendientes.status();
        return estado === 'idle' || estado === 'loading';
    }

    toggle(): void {
        this.ngZone.run(() => {
            this.isOpen.update((open) => !open);
            if (this.isOpen()) {
                this.notifications.markAllRead();
            }
        });
    }

    // Seguir un enlace de un pendiente: se cierra el panel y se navega (el foco queda en la pantalla nueva).
    alNavegar(): void {
        this.isOpen.set(false);
    }

    clear(): void {
        this.notifications.clearAll();
    }

    // Igual que CustomSelect: cierra si el click fue afuera del componente.
    @HostListener('document:click', ['$event'])
    onDocumentClick(event: MouseEvent): void {
        if (this.isOpen() && !this.elementRef.nativeElement.contains(event.target as Node)) {
            this.ngZone.run(() => this.isOpen.set(false));
        }
    }

    // Esc cierra y devuelve el foco al botón de la campanita.
    @HostListener('document:keydown.escape')
    onEscape(): void {
        if (this.isOpen()) {
            this.ngZone.run(() => this.isOpen.set(false));
            this.disparador?.nativeElement.focus();
        }
    }

    relativeTime(date: Date): string {
        const diffMs = Date.now() - date.getTime();
        const diffMin = Math.floor(diffMs / 60000);

        if (diffMin < 1) return 'Ahora';
        if (diffMin < 60) return `Hace ${diffMin} min`;

        const diffH = Math.floor(diffMin / 60);
        if (diffH < 24) return `Hace ${diffH} h`;

        return date.toLocaleDateString('es-AR', { day: '2-digit', month: '2-digit' });
    }
}
