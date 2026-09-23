import {
    Component,
    ElementRef,
    HostListener,
    NgZone,
    ViewChild,
    effect,
    inject,
    signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import {
    BotIntent,
    FALLBACK_TEXT,
    GREETING_TEXT,
    QuickAction,
    SUGGESTED_KEYWORDS,
    TopicChip,
    fallbackTopics,
    findIntentById,
    isClearCommand,
    isClearIntent,
    isGreeting,
    matchIntent,
    relatedTopics,
} from './bot-knowledge-base';

interface ChatMessage {
    id: number;
    from: 'bot' | 'user';
    text?: string;
    title?: string;
    steps?: string[];
    action?: QuickAction;
    chips?: TopicChip[];
    suggestedKeywords?: string[];
}

// Se persiste la conversación en sessionStorage bajo esta clave: sobrevive
// a cerrar/abrir el drawer y a recargar la pestaña, pero no se acumula
// indefinidamente entre sesiones (se limpia sola al cerrar la pestaña).
const STORAGE_KEY = 'cacc_copilot_state';

interface PersistedState {
    messages: ChatMessage[];
    isOpen: boolean;
}

let nextMessageId = 1;

// "El Copiloto CACC": drawer de ayuda flotante, siempre montado una sola vez
// en el shell del portal admin (como app-toast) — no por pantalla. Es un bot
// de intenciones por palabras clave (ver bot-knowledge-base.ts), no un LLM:
// el objetivo de esta primera versión es reorientar al administrador con
// pasos cortos y un acceso directo, no sostener una charla abierta.
@Component({
    selector: 'app-copilot-chat',
    standalone: true,
    imports: [CommonModule],
    templateUrl: './copilot-chat.html',
    styleUrl: './copilot-chat.css',
})
export class CopilotChat {
    private router = inject(Router);
    private ngZone = inject(NgZone);

    isOpen = signal(false);
    draft = signal('');
    messages = signal<ChatMessage[]>([]);
    // Pista de "/clear" al enfocar el input — no permanente, para no
    // recargar visualmente un input que casi siempre está vacío.
    showClearHint = signal(false);

    @ViewChild('scrollAnchor') private scrollAnchor?: ElementRef<HTMLElement>;

    constructor() {
        this.restoreState();

        // Cualquier cambio en la conversación o en abierto/cerrado se
        // guarda solo — el usuario no "guarda" el chat, simplemente sigue
        // ahí cuando vuelve a abrirlo.
        effect(() => {
            this.saveState({ messages: this.messages(), isOpen: this.isOpen() });
        });

        // El panel vive detrás de un @if: cada vez que isOpen pasa a true
        // (toggle manual, o ya restaurado como abierto al montar el
        // componente tras un reload) Angular recién está creando el drawer
        // y su ancla de scroll — sin esto, reabrir un chat con historial
        // aparece arriba de todo en vez de mostrar la última respuesta.
        effect(() => {
            if (this.isOpen()) {
                this.scrollToBottom('auto');
            }
        });
    }

    // ngZone.run(): este componente vive montado en el shell persistente del
    // portal (AdminPortal), el mismo lugar donde ya detectamos que clicks
    // dejan de repintar si la navegación previa pasó por una pantalla con
    // gráficos que corre fuera de la zona (ver sidebar/notification-bell).
    toggle(): void {
        this.ngZone.run(() => {
            this.isOpen.update((open) => !open);
            if (this.isOpen() && this.messages().length === 0) {
                this.pushGreeting();
            }
        });
    }

    close(): void {
        this.ngZone.run(() => this.isOpen.set(false));
    }

    @HostListener('document:keydown.escape')
    onEscape(): void {
        if (this.isOpen()) this.close();
    }

    updateDraft(value: string): void {
        this.draft.set(value);
    }

    onInputFocus(): void {
        this.ngZone.run(() => this.showClearHint.set(true));
    }

    onInputBlur(): void {
        this.ngZone.run(() => this.showClearHint.set(false));
    }

    send(): void {
        const text = this.draft().trim();
        if (!text) return;

        this.ngZone.run(() => {
            if (isClearCommand(text) || isClearIntent(text)) {
                this.draft.set('');
                this.clearConversation();
                return;
            }

            this.appendMessage({ id: nextMessageId++, from: 'user', text });
            this.draft.set('');

            if (isGreeting(text)) {
                this.pushGreeting();
                return;
            }

            const intent = matchIntent(text);
            if (intent) {
                this.appendMessage(this.buildBotMessage(intent));
            } else {
                this.appendFallback();
            }
        });
    }

    // Disparado al tocar un chip de tema relacionado o un acceso rápido del
    // menú: simula que el administrador preguntó por ese trámite, sin que
    // tenga que volver a escribirlo.
    askIntent(intentId: string, label: string): void {
        const intent = findIntentById(intentId);
        if (!intent) return;

        this.ngZone.run(() => {
            this.appendMessage({ id: nextMessageId++, from: 'user', text: label });
            this.appendMessage(this.buildBotMessage(intent));
        });
    }

    runAction(action: QuickAction): void {
        this.ngZone.run(() => {
            this.router.navigate([action.route]).then(() => {
                if (!action.focusSelector) return;
                // La pantalla de destino tarda un instante en renderizar el
                // input real (ej. #player en Cuotas y Pagos) — sin este
                // margen, el querySelector corre antes de que exista.
                setTimeout(() => {
                    const target = document.querySelector<HTMLElement>(action.focusSelector!);
                    target?.focus();
                    target?.scrollIntoView({ behavior: 'smooth', block: 'center' });
                }, 150);
            });
            this.isOpen.set(false);
        });
    }

    // "/clear" o "/limpiar": reinicia el historial por completo y deja
    // únicamente el saludo predeterminado, no un chat vacío.
    private clearConversation(): void {
        this.messages.set([]);
        this.pushGreeting();
    }

    private pushGreeting(): void {
        this.appendMessage({
            id: nextMessageId++,
            from: 'bot',
            text: GREETING_TEXT,
            chips: fallbackTopics(),
        });
    }

    private appendFallback(): void {
        this.appendMessage({
            id: nextMessageId++,
            from: 'bot',
            text: FALLBACK_TEXT,
            chips: fallbackTopics(),
            suggestedKeywords: SUGGESTED_KEYWORDS,
        });
    }

    private buildBotMessage(intent: BotIntent): ChatMessage {
        return {
            id: nextMessageId++,
            from: 'bot',
            title: intent.title,
            steps: intent.steps,
            action: intent.action,
            chips: relatedTopics(intent),
        };
    }

    private appendMessage(message: ChatMessage): void {
        this.messages.update((current) => [...current, message]);
        this.scrollToBottom('smooth');
    }

    // 'auto' (instantáneo) al reabrir el chat, para no ver el scroll viajar
    // desde arriba cada vez; 'smooth' para un mensaje nuevo con el chat ya
    // abierto, donde sí se nota y se agradece la animación.
    private scrollToBottom(behavior: ScrollBehavior): void {
        // El DOM del panel (y su ancla) recién termina de montarse/animarse
        // después de este ciclo de detección de cambios — sin el
        // setTimeout, scrollAnchor todavía apunta a nada o al valor viejo.
        // scrollIntoView tampoco existe en jsdom (entorno de test), de ahí
        // el feature-detect con "?.".
        setTimeout(() => {
            this.scrollAnchor?.nativeElement.scrollIntoView?.({ behavior, block: 'end' });
        }, 0);
    }

    private restoreState(): void {
        let raw: string | null = null;
        try {
            raw = sessionStorage.getItem(STORAGE_KEY);
        } catch {
            // Modo privado / storage bloqueado: seguimos sin estado persistido.
            return;
        }
        if (!raw) return;

        try {
            const parsed = JSON.parse(raw) as PersistedState;
            if (!Array.isArray(parsed.messages)) return;

            this.messages.set(parsed.messages);
            this.isOpen.set(!!parsed.isOpen);

            // Evita que un mensaje nuevo reutilice un id ya restaurado (rompería
            // el @for track de la plantilla, que asume ids únicos).
            const maxId = parsed.messages.reduce((max, m) => Math.max(max, m.id), 0);
            nextMessageId = maxId + 1;
        } catch {
            // Estado corrupto: se ignora y arranca como si no hubiera nada.
        }
    }

    private saveState(state: PersistedState): void {
        try {
            sessionStorage.setItem(STORAGE_KEY, JSON.stringify(state));
        } catch {
            // Modo privado / storage lleno: la conversación no persiste, pero
            // el chat sigue funcionando en memoria durante la sesión.
        }
    }
}
