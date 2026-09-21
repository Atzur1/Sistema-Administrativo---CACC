import { Component, OnDestroy, effect, inject, signal } from '@angular/core';
import { NotificationKind, NotificationService } from '../notifications/notification.service';

// Confirmación flotante, anclada arriba a la derecha (cerca del avatar del
// admin). Se monta una única vez en AdminPortal y escucha NotificationService:
// las pantallas que disparan un pago/beneficio/arancel no la instancian ellas
// mismas, solo llaman a NotificationService.notify(...).
@Component({
    selector: 'app-toast',
    standalone: true,
    imports: [],
    templateUrl: './toast.html',
    styleUrl: './toast.css',
})
export class Toast implements OnDestroy {
    private static readonly DURATION_MS = 3500;
    private static readonly FADE_MS = 300;

    private notifications = inject(NotificationService);

    message = signal('');
    kind = signal<NotificationKind>('success');
    leaving = signal(false);

    private fadeTimer?: ReturnType<typeof setTimeout>;
    private clearTimer?: ReturnType<typeof setTimeout>;

    constructor() {
        effect(() => {
            const event = this.notifications.toastEvent();
            if (event) {
                this.show(event.message, event.kind);
            }
        });
    }

    private show(text: string, kind: NotificationKind): void {
        clearTimeout(this.fadeTimer);
        clearTimeout(this.clearTimer);

        this.message.set(text);
        this.kind.set(kind);
        this.leaving.set(false);

        // El fade arranca antes de sacar el nodo para que no desaparezca de golpe
        this.fadeTimer = setTimeout(() => this.leaving.set(true), Toast.DURATION_MS - Toast.FADE_MS);
        this.clearTimer = setTimeout(() => {
            this.message.set('');
            this.leaving.set(false);
        }, Toast.DURATION_MS);
    }

    ngOnDestroy(): void {
        clearTimeout(this.fadeTimer);
        clearTimeout(this.clearTimer);
    }
}
