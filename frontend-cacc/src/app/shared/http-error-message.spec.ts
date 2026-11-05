import { HttpErrorResponse } from '@angular/common/http';

import { MENSAJE_ERROR_GENERICO, mensajeDeError } from './http-error-message';

// HU-090: la API responde { mensaje } en español. La pantalla lo muestra tal cual y, si no viene, un texto propio.
// Nunca muestra un cuerpo crudo (HTML de un proxy, un stack trace) ni "[object Object]".
describe('mensajeDeError', () => {
  const error = (status: number, cuerpo: unknown) => new HttpErrorResponse({ status, error: cuerpo });

  it('muestra el mensaje de la API tal cual, sin interpretarlo', () => {
    const mensaje = 'El jugador ya tiene la bonificación "Beca Completa" vigente del 01/10/2026 al 31/12/2026. Ajustá las fechas o cancelá la existente.';

    expect(mensajeDeError(error(409, { exito: false, mensaje }))).toBe(mensaje);
  });

  it('usa el mensaje de la API en cualquier status (400, 404, 409, 422, 500)', () => {
    for (const status of [400, 404, 409, 422, 500]) {
      expect(mensajeDeError(error(status, { mensaje: `Texto ${status}` }), 'otro')).toBe(`Texto ${status}`);
    }
  });

  it('sin mensaje usa el texto propio de la pantalla', () => {
    expect(mensajeDeError(error(400, {}), 'No se pudo guardar.')).toBe('No se pudo guardar.');
  });

  it('sin mensaje ni texto propio usa el genérico en español', () => {
    expect(MENSAJE_ERROR_GENERICO).toBe('Ocurrió un error al procesar la solicitud.');
    expect(mensajeDeError(error(500, null))).toBe(MENSAJE_ERROR_GENERICO);
  });

  it('nunca muestra un cuerpo crudo ni un objeto', () => {
    const casos: unknown[] = [
      '<html><body>502 Bad Gateway</body></html>',
      'System.NullReferenceException: Object reference not set',
      { mensaje: { detalle: 'x' } },
      { mensaje: '   ' },
      { message: 'Player not found' },
      { title: 'One or more validation errors occurred.', errors: { Reason: ['x'] } },
    ];
    for (const cuerpo of casos) {
      const texto = mensajeDeError(error(400, cuerpo), 'Texto propio.');
      expect(texto).toBe('Texto propio.');
    }
  });

  it('sin conexión con el servidor lo dice en español', () => {
    expect(mensajeDeError(error(0, new ProgressEvent('error')), 'Texto propio.')).toBe(
      'No se pudo conectar con el servidor. Verificá tu conexión e intentá de nuevo.',
    );
  });

  it('un error que no es HTTP usa el texto propio', () => {
    expect(mensajeDeError(new Error('boom'), 'Texto propio.')).toBe('Texto propio.');
    expect(mensajeDeError(undefined)).toBe(MENSAJE_ERROR_GENERICO);
  });
});
