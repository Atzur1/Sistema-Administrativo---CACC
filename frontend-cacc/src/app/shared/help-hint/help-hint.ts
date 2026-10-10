import {
  Component,
  ElementRef,
  HostListener,
  Input,
  NgZone,
  OnDestroy,
  ViewChild,
  signal,
} from '@angular/core';
import { FloatingPanel } from '../floating-panel';

// Lo que tarda en aparecer la ayuda al apoyar el mouse: evita que se abra al pasar por encima
// camino a otro botón.
const HOVER_DELAY_MS = 250;

let nextId = 0;

// Botón chico "?" que explica para qué sirve la acción que tiene al lado (Anular, Deshabilitar…).
// Se abre al dejar el mouse encima, al enfocarlo con el teclado o al tocarlo (pantallas táctiles,
// donde no hay hover). Va como hermano del botón que explica, nunca adentro: un botón dentro de
// otro no es HTML válido y el clic dispararía la acción.
@Component({
  selector: 'app-help-hint',
  standalone: true,
  templateUrl: './help-hint.html',
  styleUrl: './help-hint.css',
})
export class HelpHint implements OnDestroy {
  @Input({ required: true }) text = '';
  // Nombre corto de la acción para lectores de pantalla ("Ayuda: Anular").
  @Input() label = '';

  readonly open = signal(false);
  readonly tooltipId = `help-hint-${++nextId}`;

  @ViewChild('trigger') private triggerRef?: ElementRef<HTMLButtonElement>;

  // Igual que los desplegables: el texto se muestra en <body> pegado al "?", para que las
  // tarjetas y los diálogos con overflow no lo recorten.
  private readonly panel: FloatingPanel;
  private hoverTimer: ReturnType<typeof setTimeout> | null = null;

  @ViewChild('tooltip')
  set tooltipRef(ref: ElementRef<HTMLElement> | undefined) {
    if (ref) {
      this.panel.attach(ref.nativeElement);
    } else {
      this.panel.detach();
    }
  }

  constructor(
    private elementRef: ElementRef<HTMLElement>,
    ngZone: NgZone,
  ) {
    this.panel = new FloatingPanel(ngZone, () => this.triggerRef?.nativeElement, {
      matchAnchorWidth: false,
      gap: 8,
    });
  }

  ngOnDestroy(): void {
    this.clearTimer();
    this.panel.detach();
  }

  onMouseEnter(): void {
    this.clearTimer();
    this.hoverTimer = setTimeout(() => this.open.set(true), HOVER_DELAY_MS);
  }

  onMouseLeave(): void {
    this.clearTimer();
    this.open.set(false);
  }

  show(): void {
    this.clearTimer();
    this.open.set(true);
  }

  hide(): void {
    this.clearTimer();
    this.open.set(false);
  }

  // El clic no tiene que llegar a la fila o tarjeta que lo contiene (algunas son clickeables).
  toggle(event: Event): void {
    event.stopPropagation();
    this.clearTimer();
    this.open.update((value) => !value);
  }

  // Un toque fuera del "?" cierra la ayuda que se abrió tocando.
  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (this.open() && !this.elementRef.nativeElement.contains(event.target as Node)) {
      this.open.set(false);
    }
  }

  private clearTimer(): void {
    if (this.hoverTimer !== null) {
      clearTimeout(this.hoverTimer);
      this.hoverTimer = null;
    }
  }
}
