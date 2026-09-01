import { Routes } from '@angular/router';
import { Login } from './login/login';
import { Portales } from './portales/portales';
import { Dashboard } from './dashboard/dashboard';
import { AdminPortal } from './admin-portal/admin-portal';
import { ResumenGeneral } from './admin-portal/resumen-general/resumen-general';
import { adminGuard } from './services/auth';
import { ActividadMovimientos } from './admin-portal/actividad-movimientos/actividad-movimientos';
import { DeudasMorosidad } from './admin-portal/deudas-morosidad/deudas-morosidad';
import { Usuarios } from './admin-portal/usuarios-y-permisos/usuarios-y-permisos';
import { CuotasPagos } from './admin-portal/cuotas-pagos/cuotas-pagos';
import { Reportes } from './admin-portal/reportes/reportes';

export const routes: Routes = [
  { path: '', component: Login },
  { path: 'portales', component: Portales },
  { path: 'admin/dashboard', component: Dashboard, canActivate: [adminGuard] },

  // Admin portal (parent) with its dashboards as children
  {
    path: 'admin/portal',
    component: AdminPortal,
    canActivate: [adminGuard],
    children: [
      { path: '', redirectTo: 'resumen-general', pathMatch: 'full' },
      { path: 'resumen-general', component: ResumenGeneral },
      { path: 'actividad-movimientos', component: ActividadMovimientos },
      { path: 'deudas-morosidad', component: DeudasMorosidad },
      { path: 'usuarios', component: Usuarios },
      { path: 'cuotas-pagos', component: CuotasPagos },
      { path: 'reportes', component: Reportes },
    ],
  },

  { path: '**', redirectTo: '' }
];    