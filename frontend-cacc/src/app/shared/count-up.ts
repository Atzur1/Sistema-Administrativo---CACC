import { NgZone, WritableSignal } from '@angular/core';

// Un número a animar: el valor final real, dónde escribir cada frame ya
// formateado, y cómo formatearlo (moneda compacta, moneda completa, "%", etc).
export interface CountUpItem {
    target: number;
    display: WritableSignal<string>;
    format?: (value: number) => string;
}

// Cuenta uno o más números de 0 hasta su valor final en paralelo, con una
// curva ease-out cúbica (~800ms). Corre afuera de la zona de Angular para que
// zone.js no dispare detección de cambios en cada frame — como `display` es
// un signal, Angular igual repinta la vista solo cuando el valor realmente
// cambia, sin depender de en qué zona haya quedado la navegación (el mismo
// motivo por el que el <select> propio necesitó ngZone.run: acá directamente
// no hace falta, los signals se notifican solos).
//
// Devuelve una función para cancelar la animación (llamarla en el
// DestroyRef.onDestroy del componente si puede desmontarse a mitad de camino).
export function animateCountUp(zone: NgZone, items: CountUpItem[], durationMs = 800): () => void {
    if (items.length === 0) {
        return () => {};
    }

    let rafId = 0;
    let cancelled = false;
    const startTime = performance.now();

    zone.runOutsideAngular(() => {
        const tick = (now: number) => {
            if (cancelled) {
                return;
            }
            const progress = Math.min((now - startTime) / durationMs, 1);
            const eased = 1 - Math.pow(1 - progress, 3);

            for (const item of items) {
                const current = Math.round(item.target * eased);
                item.display.set(item.format ? item.format(current) : String(current));
            }

            if (progress < 1) {
                rafId = requestAnimationFrame(tick);
            }
        };

        rafId = requestAnimationFrame(tick);
    });

    return () => {
        cancelled = true;
        cancelAnimationFrame(rafId);
    };
}
