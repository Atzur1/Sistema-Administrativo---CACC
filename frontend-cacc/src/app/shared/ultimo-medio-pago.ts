// HU-100: el último medio de pago usado en un cobro, para proponerlo en el siguiente. Vive en sessionStorage (dura lo
// que la sesión del navegador). Si el almacenamiento no está disponible (modo privado, bloqueado), simplemente no se
// recuerda: el cobro funciona igual.
export const MEDIOS_DE_PAGO = ['Efectivo', 'Transferencia'] as const;
export type MedioDePago = (typeof MEDIOS_DE_PAGO)[number];

const CLAVE = 'cacc-ultimo-medio-pago';

export function leerUltimoMedioPago(): MedioDePago | '' {
  try {
    const valor = sessionStorage.getItem(CLAVE);
    return MEDIOS_DE_PAGO.includes(valor as MedioDePago) ? (valor as MedioDePago) : '';
  } catch {
    return '';
  }
}

export function guardarUltimoMedioPago(medio: string): void {
  if (!MEDIOS_DE_PAGO.includes(medio as MedioDePago)) return;
  try {
    sessionStorage.setItem(CLAVE, medio);
  } catch {
    // Sin almacenamiento disponible: el próximo cobro arranca sin medio elegido.
  }
}
