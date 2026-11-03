import { Component, Injectable, OnDestroy, inject, signal } from '@angular/core';

// HU-105: cuánto se puede deshacer una acción reversible, contado desde que el servidor la confirmó.
export const UNDO_WINDOW_MS = 5000;

export type UndoNoticeState = 'available' | 'undoing';

export interface UndoNotice {
  id: number;
  message: string;
  // Nombre accesible del botón: empieza con "Deshacer" (lo que se ve) y dice qué deshace.
  undoLabel: string;
  state: UndoNoticeState;
}

export interface UndoOffer {
  message: string;
  undoLabel: string;
  // Al tocar "Deshacer" (una sola vez). Quien ofrece el aviso lo quita con dismiss() cuando termina.
  onUndo: () => void;
  // Al vencer el plazo sin que se haya deshecho.
  onExpire: () => void;
}

interface Entry {
  offer: UndoOffer;
  timer: ReturnType<typeof setTimeout>;
}

// Avisos con "Deshacer", uno por operación y cada uno con su plazo. Van aparte del toast (que muestra uno solo y lo
// reemplaza con el siguiente) y de la campanita (no son pendientes ni historial). Viven mientras dure la sesión en el
// portal, aunque se cambie de pantalla; al salir del portal se borran (clear).
@Injectable({ providedIn: 'root' })
export class UndoNoticesService implements OnDestroy {
  private readonly lista = signal<UndoNotice[]>([]);
  readonly notices = this.lista.asReadonly();

  private readonly entries = new Map<number, Entry>();
  private sequence = 0;

  offer(offer: UndoOffer): number {
    const id = ++this.sequence;
    const timer = setTimeout(() => this.expire(id), UNDO_WINDOW_MS);
    this.entries.set(id, { offer, timer });
    this.lista.update((avisos) => [...avisos, { id, message: offer.message, undoLabel: offer.undoLabel, state: 'available' }]);
    return id;
  }

  undo(id: number): void {
    const entry = this.entries.get(id);
    const aviso = this.lista().find((a) => a.id === id);
    if (!entry || aviso?.state !== 'available') return;
    clearTimeout(entry.timer);
    this.lista.update((avisos) => avisos.map((a) => (a.id === id ? { ...a, state: 'undoing' } : a)));
    entry.offer.onUndo();
  }

  dismiss(id: number): void {
    const entry = this.entries.get(id);
    if (entry) clearTimeout(entry.timer);
    this.entries.delete(id);
    this.lista.update((avisos) => avisos.filter((a) => a.id !== id));
  }

  // Cierre de sesión o salida del portal: sin vencimientos ni deshacer pendientes.
  clear(): void {
    for (const entry of this.entries.values()) clearTimeout(entry.timer);
    this.entries.clear();
    this.lista.set([]);
  }

  ngOnDestroy(): void {
    this.clear();
  }

  private expire(id: number): void {
    const entry = this.entries.get(id);
    if (!entry) return;
    this.dismiss(id);
    entry.offer.onExpire();
  }
}

// Se monta una sola vez en el Portal Administrativo, como el toast. La región existe siempre (vacía si no hay avisos)
// para que el lector de pantalla anuncie cada aviso nuevo sin mover el foco.
@Component({
  selector: 'app-undo-notices',
  standalone: true,
  templateUrl: './undo-notices.html',
  styleUrl: './undo-notices.css',
})
export class UndoNotices implements OnDestroy {
  readonly avisos = inject(UndoNoticesService);

  ngOnDestroy(): void {
    this.avisos.clear();
  }
}
