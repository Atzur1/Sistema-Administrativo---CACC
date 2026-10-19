import { Component } from '@angular/core';
import { Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { AuthService } from '../services/auth';

@Component({
  selector: 'app-portales',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './portales.html',
  styleUrl: './portales.css',
})
export class Portales {
  constructor(
    private router: Router,
    private authService: AuthService
  ) {}

  ingresarPortalAdministrativo() {
    if (!this.authService.isAuthenticated()) {
      alert('Debes iniciar sesión para acceder al Portal Administrativo.');
      this.router.navigate(['/']);
      return;
    }

    if (!this.authService.isAdmin()) {
      alert('No tienes permisos para acceder al Portal Administrativo.');
      return;
    }

    this.router.navigate(['/admin/portal']);
  }

  ingresarPortalDeportivo() {
    if (!this.authService.abrirPortalDeportivo()) {
      alert('Debes iniciar sesión para acceder al Portal Deportivo.');
      this.router.navigate(['/']);
    }
  }

  // El alta de personas (jugadores y staff) la hace el Portal Deportivo: se entra con la misma sesión, directo a esa pantalla.
  ingresarPortalPersonas() {
    if (!this.authService.abrirPortalDeportivo('/admin/registro-personas')) {
      alert('Debes iniciar sesión para acceder al Portal de Personas.');
      this.router.navigate(['/']);
    }
  }

  cerrarSesion() {
    this.authService.cerrarSesion();
    this.router.navigate(['/']);
  }
}