// HU-098: la misma preferencia que respeta la regla global de CSS (@media (prefers-reduced-motion: reduce) en
// styles.css), para lo que se mueve con JavaScript: los números que cuentan hasta su valor (count-up.ts) y los
// paneles flotantes que siguen a su ancla (floating-panel.ts). Si el sistema operativo pide reducir el movimiento,
// no se anima nada.
export function prefiereMovimientoReducido(): boolean {
  return (
    typeof window !== 'undefined' &&
    typeof window.matchMedia === 'function' &&
    window.matchMedia('(prefers-reduced-motion: reduce)').matches
  );
}
