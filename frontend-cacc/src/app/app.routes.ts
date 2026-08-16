import { Routes } from '@angular/router';
import { Login } from './login/login';
import { Portales } from './portales/portales';

export const routes: Routes = [
  { path: '', component: Login },
  { path: 'portales', component: Portales },
  { path: '**', redirectTo: '' }
];