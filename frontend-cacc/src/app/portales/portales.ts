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

    alert('Ingresando al Portal Administrativo...');
    this.router.navigate(['/admin/dashboard']);
  }

  ingresarPortalDeportivo() {
    alert('Ingresando al Portal Deportivo...');
    // this.router.navigate(['/deportivo/dashboard']);
  }

  ingresarPortalPersonas() {
    alert('Ingresando al Portal de Personas (Alta de Usuarios)...');
    // this.router.navigate(['/personas/alta']); // Ajusta la ruta cuando esté lista
  }

  cerrarSesion() {
    this.authService.logout();
    this.router.navigate(['/']);
  }
}