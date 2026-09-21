import { Injectable, computed, signal } from '@angular/core';

export type NotificationKind = 'success' | 'error' | 'cancelled';

export interface AppNotification {
    id: number;
    message: string;
    kind: NotificationKind;
    timestamp: Date;
    read: boolean;
}

interface ToastEvent {
    id: number;
    message: string;
    kind: NotificationKind;
}

// Single source of truth para las notificaciones de acciones administrativas
// (registrar pago, asignar/cancelar beneficio, programar arancel): cada
// llamado a notify() hace dos cosas a la vez — dispara el toast flotante
// (efímero) y agrega un registro permanente a la campana, para que quede
// historial de qué se hizo aunque el toast ya haya desaparecido.
@Injectable({ providedIn: 'root' })
export class NotificationService {
    private nextId = 1;

    private readonly _notifications = signal<AppNotification[]>([]);
    private readonly _toastEvent = signal<ToastEvent | null>(null);

    readonly notifications = this._notifications.asReadonly();
    readonly toastEvent = this._toastEvent.asReadonly();
    readonly unreadCount = computed(() => this._notifications().filter((n) => !n.read).length);

    notify(message: string, kind: NotificationKind = 'success'): void {
        const id = this.nextId++;

        // Tope de 50: es un historial de sesión, no un log — sin límite crecería
        // sin parar en una jornada larga cargando pagos uno por uno.
        this._notifications.update((list) => [{ id, message, kind, timestamp: new Date(), read: false }, ...list].slice(0, 50));

        // Objeto nuevo en cada llamado (aunque el texto se repita) para que el
        // toast siempre dispare de nuevo, incluso si dos acciones seguidas
        // muestran el mismo mensaje.
        this._toastEvent.set({ id, message, kind });
    }

    markAllRead(): void {
        this._notifications.update((list) => list.map((n) => (n.read ? n : { ...n, read: true })));
    }

    clearAll(): void {
        this._notifications.set([]);
    }
}
