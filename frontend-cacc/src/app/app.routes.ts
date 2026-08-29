import { Routes } from '@angular/router';
import { Login } from './login/login';
import { Portales } from './portales/portales';
import { Dashboard } from './dashboard/dashboard';
import { AdminPortal } from './admin-portal/admin-portal';
import { adminGuard } from './services/auth';

export const routes: Routes = [
  { path: '', component: Login },
  { path: 'portales', component: Portales },
  { path: 'admin/dashboard', component: Dashboard, canActivate: [adminGuard] },
  { path: 'admin/portal', component: AdminPortal, canActivate: [adminGuard] },
  { path: '**', redirectTo: '' }
];