import { NgZone } from '@angular/core';

import { prefiereMovimientoReducido } from './movimiento-reducido';

// Variables de tema que usan los paneles flotantes. Cada pantalla puede redefinirlas en su
// propio contenedor (ej. Becados y Descuentos usa otra paleta), así que se copian desde el
// elemento ancla al sacar el panel de ahí.
const THEME_VARS = ['--color-brand', '--color-brand-deep', '--color-brand-tint', '--color-text', '--color-text-muted'];

export interface FloatingPanelOptions {
  // true: el panel toma el ancho del ancla (desplegables). false: conserva su ancho natural (calendario).
  matchAnchorWidth: boolean;
  // Alto máximo en px (con scroll interno). Sin valor, el panel mide lo que mide su contenido.
  maxHeight?: number;
  // Separación entre el ancla y el panel, en px.
  gap?: number;
}

// Muestra un panel flotante (menú de opciones, calendario) pegado a un elemento ancla.
//
// El panel NO se queda dentro del componente: se mueve a <body> y se posiciona con coordenadas
// fijas. Dentro de un panel de la pantalla quedaba tapado por el siguiente (las tarjetas animadas
// con fadeSlideUp crean su propio contexto de apilamiento, y la que viene después en el HTML se
// pinta encima aunque el menú tenga z-index más alto) o recortado por su overflow.
//
// Mientras está abierto, la posición se recalcula en cada cuadro: así acompaña al ancla aunque la
// pantalla se mueva sin que haya scroll ni resize (por ejemplo, cuando el menú lateral se expande
// al pasar el mouse y corre el contenido).
//
// Con movimiento reducido (HU-098) no hay nada que corra cuadro por cuadro: el panel se ubica al
// abrirse y se vuelve a ubicar solo cuando hay scroll, cambia el tamaño de la ventana o el del ancla.
export class FloatingPanel {
  private panel: HTMLElement | null = null;
  private frame = 0;
  private lastPlacement = '';
  private dejarDeEscuchar: (() => void) | null = null;

  constructor(
    private zone: NgZone,
    private anchor: () => HTMLElement | null | undefined,
    private options: FloatingPanelOptions,
  ) {}

  attach(panel: HTMLElement): void {
    this.detach();
    this.panel = panel;

    const anchor = this.anchor();
    if (anchor) {
      const style = getComputedStyle(anchor);
      // Fuera de su pantalla el panel hereda la tipografía de <body> (serif por defecto),
      // así que se la copiamos al ancla, que sí tiene la del sistema.
      panel.style.fontFamily = style.fontFamily;
      for (const name of THEME_VARS) {
        const value = style.getPropertyValue(name).trim();
        if (value) {
          panel.style.setProperty(name, value);
        }
      }
    }

    document.body.appendChild(panel);
    this.position();

    if (prefiereMovimientoReducido()) {
      this.seguirSinAnimar(anchor);
      return;
    }

    if (typeof requestAnimationFrame === 'function') {
      // Fuera de la zona de Angular: solo se tocan estilos, no hace falta detección de cambios por cuadro.
      this.zone.runOutsideAngular(() => {
        const tick = () => {
          this.position();
          this.frame = requestAnimationFrame(tick);
        };
        this.frame = requestAnimationFrame(tick);
      });
    }
  }

  // Con movimiento reducido: se reubica solo ante scroll (de la página o de un panel con scroll propio),
  // cambio de tamaño de la ventana o del ancla. Fuera de la zona de Angular: solo se tocan estilos.
  private seguirSinAnimar(anchor: HTMLElement | null | undefined): void {
    this.zone.runOutsideAngular(() => {
      const reubicar = () => this.position();
      window.addEventListener('scroll', reubicar, { capture: true, passive: true });
      window.addEventListener('resize', reubicar, { passive: true });
      const observador = typeof ResizeObserver === 'function' && anchor ? new ResizeObserver(reubicar) : null;
      if (observador && anchor) {
        observador.observe(anchor);
      }
      this.dejarDeEscuchar = () => {
        window.removeEventListener('scroll', reubicar, { capture: true });
        window.removeEventListener('resize', reubicar);
        observador?.disconnect();
      };
    });
  }

  // Angular quita el nodo al cerrarse; el remove() cubre el caso de que el componente se destruya abierto.
  detach(): void {
    if (this.frame && typeof cancelAnimationFrame === 'function') {
      cancelAnimationFrame(this.frame);
    }
    this.frame = 0;
    this.dejarDeEscuchar?.();
    this.dejarDeEscuchar = null;
    this.panel?.remove();
    this.panel = null;
    this.lastPlacement = '';
  }

  contains(node: Node | null): boolean {
    return !!node && (this.panel?.contains(node) ?? false);
  }

  // Debajo del ancla; si no entra y arriba hay más lugar, se abre hacia arriba.
  private position(): void {
    const panel = this.panel;
    const anchor = this.anchor();
    if (!panel || !anchor) {
      return;
    }

    const rect = anchor.getBoundingClientRect();
    const gap = this.options.gap ?? 6;
    const margin = 8;
    const maxAllowed = this.options.maxHeight ?? Number.POSITIVE_INFINITY;

    const spaceBelow = window.innerHeight - rect.bottom - gap;
    const spaceAbove = rect.top - gap;
    const naturalHeight = Math.min(panel.scrollHeight, maxAllowed);
    const openUp = naturalHeight > spaceBelow && spaceAbove > spaceBelow;
    const available = openUp ? spaceAbove : spaceBelow;
    const maxHeight = Math.max(100, Math.min(maxAllowed, available - margin));
    const height = Math.min(naturalHeight, maxHeight);

    const width = this.options.matchAnchorWidth ? rect.width : panel.offsetWidth;
    const left = this.options.matchAnchorWidth
      ? rect.left
      : Math.max(margin, Math.min(rect.left, window.innerWidth - width - margin));
    const top = openUp ? rect.top - gap - height : rect.bottom + gap;

    // Solo se escriben estilos cuando algo cambió: en reposo no hay trabajo por cuadro.
    const placement = `${left}|${top}|${width}|${maxHeight}`;
    if (placement === this.lastPlacement) {
      return;
    }
    this.lastPlacement = placement;

    panel.style.left = `${left}px`;
    panel.style.top = `${top}px`;
    if (this.options.matchAnchorWidth) {
      panel.style.width = `${width}px`;
    }
    if (Number.isFinite(maxAllowed)) {
      panel.style.maxHeight = `${maxHeight}px`;
    }
  }
}
