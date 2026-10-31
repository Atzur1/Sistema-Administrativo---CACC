// Nombre oficial de cada sección del Portal Administrativo (HU-103). Es la única fuente de esos textos: la usan el menú
// lateral (texto y tooltip), la barra superior, el h1 de cada pantalla y el asistente DT cuando manda a una sección.
// La clave es el path de la ruta hija de /admin/portal (app.routes.ts).
export const PORTAL_ADMINISTRATIVO = 'Portal administrativo';

export const SECCIONES = {
  'resumen-general': 'Resumen General',
  'cuotas-pagos': 'Cuotas y Pagos',
  'deudas-morosidad': 'Deudas y Morosidad',
  'becados-descuentos': 'Becados y Descuentos',
  'recaudacion-mensual': 'Recaudación Mensual',
  'actividad-movimientos': 'Actividad y Movimientos',
  'reportes': 'Reportes',
  'actualizacion-aranceles': 'Actualización de Aranceles',
  'usuarios': 'Usuarios y Permisos',
  'auditoria': 'Auditoría',
} as const;

export type Seccion = keyof typeof SECCIONES;

export function esSeccion(valor: unknown): valor is Seccion {
  return typeof valor === 'string' && Object.prototype.hasOwnProperty.call(SECCIONES, valor);
}
