import { NgZone, signal } from '@angular/core';

import { animateCountUp } from './count-up';
import { FloatingPanel } from './floating-panel';
import { prefiereMovimientoReducido } from './movimiento-reducido';

// HU-098 (corrección de QA): lo que se mueve con JavaScript respeta prefers-reduced-motion, igual que la regla
// global de CSS.

const zona = { runOutsideAngular: <T>(fn: () => T) => fn() } as unknown as NgZone;

function simularPreferencia(reducido: boolean) {
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    writable: true,
    value: (consulta: string) => ({ matches: reducido && consulta.includes('prefers-reduced-motion: reduce'), media: consulta }),
  });
}

describe('Movimiento reducido (HU-098)', () => {
  let cuadros: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    cuadros = vi.fn(() => 1);
    vi.stubGlobal('requestAnimationFrame', cuadros);
    vi.stubGlobal('cancelAnimationFrame', vi.fn());
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    delete (window as { matchMedia?: unknown }).matchMedia;
  });

  it('lee la preferencia del sistema operativo', () => {
    simularPreferencia(true);
    expect(prefiereMovimientoReducido()).toBe(true);
    simularPreferencia(false);
    expect(prefiereMovimientoReducido()).toBe(false);
  });

  describe('números que cuentan (count-up)', () => {
    it('con movimiento reducido muestra el valor final de una, sin animar', () => {
      simularPreferencia(true);
      const total = signal('');
      const porcentaje = signal('');

      animateCountUp(zona, [
        { target: 125000, display: total },
        { target: 87.6, display: porcentaje, format: (v) => `${v}%` },
      ]);

      expect(total()).toBe('125000');
      expect(porcentaje()).toBe('88%'); // el mismo redondeo con el que termina la animación
      expect(cuadros).not.toHaveBeenCalled();
    });

    it('sin la preferencia sigue animando cuadro por cuadro', () => {
      simularPreferencia(false);
      animateCountUp(zona, [{ target: 10, display: signal('') }]);

      expect(cuadros).toHaveBeenCalled();
    });
  });

  describe('paneles flotantes (menús y calendarios)', () => {
    function armar() {
      const ancla = document.createElement('button');
      document.body.appendChild(ancla);
      let rect = { top: 100, bottom: 130, left: 50, right: 250, width: 200, height: 30 };
      ancla.getBoundingClientRect = () => ({ ...rect, x: rect.left, y: rect.top, toJSON: () => ({}) }) as DOMRect;
      const mover = (top: number) => (rect = { ...rect, top, bottom: top + 30 });
      const panel = document.createElement('ul');
      const flotante = new FloatingPanel(zona, () => ancla, { matchAnchorWidth: true });
      return { ancla, panel, flotante, mover };
    }

    it('con movimiento reducido no corre cuadro por cuadro: se reubica con el scroll', () => {
      simularPreferencia(true);
      const { ancla, panel, flotante, mover } = armar();

      flotante.attach(panel);
      expect(cuadros).not.toHaveBeenCalled();
      expect(panel.style.top).toBe('136px');

      mover(300);
      window.dispatchEvent(new Event('scroll'));
      expect(panel.style.top).toBe('336px');

      flotante.detach();
      mover(500);
      window.dispatchEvent(new Event('scroll'));
      expect(panel.style.top).toBe('336px'); // ya cerrado, deja de escuchar
      ancla.remove();
    });

    it('sin la preferencia sigue al ancla cuadro por cuadro', () => {
      simularPreferencia(false);
      const { ancla, panel, flotante } = armar();

      flotante.attach(panel);
      expect(cuadros).toHaveBeenCalled();

      flotante.detach();
      ancla.remove();
    });
  });
});
