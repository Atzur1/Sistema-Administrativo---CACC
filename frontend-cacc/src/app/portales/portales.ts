import { Component } from '@angular/core';
import { Router } from '@angular/router';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-portales',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './portales.html',
  styleUrl: './portales.css',
})
export class Portales {
  constructor(private router: Router) {}

  ingresarPortalAdministrativo() {
    alert('Ingresando al Portal Administrativo...');
    // this.router.navigate(['/admin/dashboard']);
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
    this.router.navigate(['/']);
  }
}