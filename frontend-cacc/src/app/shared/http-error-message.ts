import { HttpErrorResponse } from '@angular/common/http';
import { FINANCIAL_ACCESS_DENIED } from '../services/auth';

// HU-090: texto cuando la API no manda un mensaje utilizable y la pantalla no tiene uno propio.
export const MENSAJE_ERROR_GENERICO = 'Ocurrió un error al procesar la solicitud.';

const SIN_CONEXION = 'No se pudo conectar con el servidor. Verificá tu conexión e intentá de nuevo.';

// El { mensaje } que manda la API, si es un texto con contenido. No interpreta nada más del cuerpo.
function mensajeDeLaApi(error: unknown): string | null {
  if (!(error instanceof HttpErrorResponse)) {
    return null;
  }
  const cuerpo: unknown = error.error;
  const mensaje = cuerpo !== null && typeof cuerpo === 'object' ? (cuerpo as { mensaje?: unknown }).mensaje : null;
  return typeof mensaje === 'string' && mensaje.trim() ? mensaje : null;
}

// HU-090: único punto donde una pantalla convierte un error de la API en el texto que ve el usuario. La API responde
// { exito, mensaje } en español (también en las validaciones y en los conflictos, con las fechas ya en dd/MM/yyyy):
// se muestra tal cual, sin parsearlo. Si no viene, se usa el texto propio de la pantalla. Nunca se muestra un cuerpo
// crudo (HTML de un proxy, un stack trace) ni "[object Object]".
export function mensajeDeError(error: unknown, fallback = MENSAJE_ERROR_GENERICO): string {
  if (error instanceof HttpErrorResponse && error.status === 0) {
    return SIN_CONEXION;
  }
  return mensajeDeLaApi(error) ?? fallback;
}

// HU-022: mensaje claro para un error al leer datos del panel financiero.
// El 401 no llega a mostrarse: el authInterceptor cierra la sesión y vuelve al login.
// fallback: el texto para cualquier otro error. Un 4xx con { mensaje } (ej. validación
// del término de búsqueda, HU-023) muestra el del backend; un 5xx nunca, porque son
// textos internos ("Error interno al...").
export function financialLoadErrorMessage(
  error: unknown,
  fallback = 'No se pudieron cargar los datos de cuotas. Intentá de nuevo en unos minutos.',
): string {
  const status = error instanceof HttpErrorResponse ? error.status : -1;

  if (status === 0) {
    return SIN_CONEXION;
  }
  if (status === 403) {
    return FINANCIAL_ACCESS_DENIED;
  }
  const mensaje = status >= 400 && status < 500 ? mensajeDeLaApi(error) : null;
  return mensaje ?? fallback;
}
