import { Routes } from '@angular/router';
import { Login } from './login/login';
import { Portales } from './portales/portales';
import { AdminPortal } from './admin-portal/admin-portal';
import { ResumenGeneral } from './admin-portal/resumen-general/resumen-general';
import { adminGuard, portalesGuard, superAdminGuard, adminHomeRedirect, FINANCIAL_ACCESS_DENIED } from './services/auth';
import { ActividadMovimientos } from './admin-portal/actividad-movimientos/actividad-movimientos';
import { DeudasMorosidad } from './admin-portal/deudas-morosidad/deudas-morosidad';
import { Usuarios } from './admin-portal/usuarios-y-permisos/usuarios-y-permisos';
import { CuotasPagos } from './admin-portal/cuotas-pagos/cuotas-pagos';
import { Reportes } from './admin-portal/reportes/reportes';
import { JugadorPerfil } from './admin-portal/jugador-perfil/jugador-perfil';
import { ActualizacionAranceles } from './admin-portal/actualizacion-aranceles/actualizacion-aranceles';
import { DeudaJugador } from './admin-portal/deuda-jugador/deuda-jugador';
import { BecadosDescuentos } from './admin-portal/becados-descuentos/becados-descuentos';
import { AccountAccess } from './account-access/account-access';
import { Auditoria } from './admin-portal/auditoria/auditoria';

export const routes: Routes = [
  { path: '', component: Login },
  // Mismo componente, dos pantallas: la ruta decide si es el primer ingreso (correo "Crea tu contraseña")
  // o la recuperación (correo "Reestablece tu contraseña").
  { path: 'crear-contrasena', component: AccountAccess, data: { modo: 'crear' } },
  { path: 'restablecer-contrasena', component: AccountAccess, data: { modo: 'restablecer' } },
  // Elección de portal: solo la ve el SuperAdmin (portalesGuard); el resto entra directo a su portal.
  { path: 'portales', component: Portales, canActivate: [portalesGuard] },

  // Admin portal (parent) with its dashboards as children
  {
    path: 'admin/portal',
    component: AdminPortal,
    canActivate: [adminGuard],
    children: [
      { path: '', component: ResumenGeneral, canActivate: [adminHomeRedirect] },
      { path: 'resumen-general', component: ResumenGeneral, canActivate: [superAdminGuard] },
      { path: 'actividad-movimientos', component: ActividadMovimientos, canActivate: [superAdminGuard] },
      { path: 'auditoria', component: Auditoria, canActivate: [superAdminGuard] },
      { path: 'deudas-morosidad', component: DeudasMorosidad },
      { path: 'usuarios', component: Usuarios, canActivate: [superAdminGuard] },
      // HU-022: panel financiero. Lo protege adminGuard (rol 1 o 2) del padre; el
      // dato accesoDenegado es el mensaje que ve quien fuerza la URL sin ese rol.
      { path: 'cuotas-pagos', component: CuotasPagos, data: { accesoDenegado: FINANCIAL_ACCESS_DENIED } },
      { path: 'jugadores/:id', component: JugadorPerfil, canActivate: [superAdminGuard] },
      // Sin superAdminGuard: es el drill-down al que navega Deudas y Morosidad
      // (rol 1 y 2) al tocar un deudor.
      { path: 'jugadores/:id/deuda', component: DeudaJugador },
      { path: 'reportes', component: Reportes, canActivate: [superAdminGuard] },
      { path: 'actualizacion-aranceles', component: ActualizacionAranceles, canActivate: [superAdminGuard] },
      { path: 'becados-descuentos', component: BecadosDescuentos },
    ],
  },

  { path: '**', redirectTo: '' }
];
