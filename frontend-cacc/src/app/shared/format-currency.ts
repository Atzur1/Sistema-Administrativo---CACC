// Formatea montos "compactos" a mano ($93,7 M / $850 k) en vez de depender de
// Intl.NumberFormat({ notation: 'compact', style: 'currency' }): esa combinación no está
// soportada de forma consistente en todos los navegadores para ARS y en algunos cae a la
// forma en palabras ("93,7 millones de dólares" — con la moneda equivocada) en vez de
// abreviarlo. Esto siempre da el mismo resultado sin importar dónde se abra la página.
export function formatCompactCurrency(value: number): string {
  const sign = value < 0 ? '-' : '';
  const abs = Math.abs(value);

  if (abs >= 1_000_000) {
    return `${sign}$${(abs / 1_000_000).toLocaleString('es-AR', { maximumFractionDigits: 1 })} M`;
  }
  if (abs >= 1_000) {
    return `${sign}$${(abs / 1_000).toLocaleString('es-AR', { maximumFractionDigits: 1 })} k`;
  }
  return `${sign}$${abs.toLocaleString('es-AR', { maximumFractionDigits: 0 })}`;
}
