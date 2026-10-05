import { HttpErrorResponse } from '@angular/common/http';
import { FINANCIAL_ACCESS_DENIED } from '../services/auth';

// HU-022: mensaje claro para un error al leer datos del panel financiero.
// El 401 no llega a mostrarse: el authInterceptor cierra la sesión y vuelve al login.
export function financialLoadErrorMessage(error: unknown): string {
  const status = error instanceof HttpErrorResponse ? error.status : -1;

  if (status === 0) {
    return 'No se pudo conectar con el servidor. Verificá tu conexión e intentá de nuevo.';
  }
  if (status === 403) {
    return FINANCIAL_ACCESS_DENIED;
  }
  return 'No se pudieron cargar los datos de cuotas. Intentá de nuevo en unos minutos.';
}
