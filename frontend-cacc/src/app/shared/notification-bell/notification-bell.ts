import { Component, ElementRef, HostListener, NgZone, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { NotificationService } from '../notifications/notification.service';

// Historial de las notificaciones que ya se convirtieron en toast (pagos,
// beneficios, aranceles): a diferencia del toast, esto no desaparece solo —
// queda ahí para poder repasar "¿qué guardé recién?" aunque el aviso
// flotante ya se haya ido.
@Component({
    selector: 'app-notification-bell',
    standalone: true,
    imports: [CommonModule],
    templateUrl: './notification-bell.html',
    styleUrl: './notification-bell.css',
})
export class NotificationBell {
    private notifications = inject(NotificationService);
    private ngZone = inject(NgZone);

    readonly items = this.notifications.notifications;
    readonly unreadCount = this.notifications.unreadCount;

    isOpen = signal(false);

    constructor(private elementRef: ElementRef<HTMLElement>) {}

    toggle(): void {
        this.ngZone.run(() => {
            this.isOpen.update((open) => !open);
            if (this.isOpen()) {
                this.notifications.markAllRead();
            }
        });
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

    @HostListener('document:keydown.escape')
    onEscape(): void {
        if (this.isOpen()) {
            this.ngZone.run(() => this.isOpen.set(false));
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
