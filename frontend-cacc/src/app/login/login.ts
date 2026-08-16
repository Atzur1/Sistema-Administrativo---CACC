import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './login.html',
  styleUrl: './login.css',
})
export class Login {
  loginForm: FormGroup;
  mensajeError: string = '';
  mensajeExito: string = '';
  
  // Propiedad para controlar la visibilidad de la contraseña
  mostrarPassword: boolean = false;

  // URL de tu API en C# (ajustá el puerto según el que te devuelva `dotnet run`)
  private apiUrl = 'http://localhost:5118/api/auth/login';

  constructor(
    private fb: FormBuilder, 
    private router: Router,
    private http: HttpClient
  ) {
    this.loginForm = this.fb.group({
      usuario: ['', [Validators.required]],
      contrasena: ['', [Validators.required]]
    });
  }

  togglePasswordVisibility() {
    this.mostrarPassword = !this.mostrarPassword;
  }

  onSubmit() {
    if (this.loginForm.invalid) {
      this.loginForm.markAllAsTouched();
      this.mensajeError = 'Deberá completar los campos para continuar.';
      return;
    }

    const credentials = this.loginForm.value;

    // Petición HTTP POST hacia el backend para validar contra la Base de Datos
    this.http.post<any>(this.apiUrl, credentials).subscribe({
      next: (response) => {
        this.mensajeExito = `¡Bienvenido al sistema del CACC!`;
        this.mensajeError = '';
        
        // Redirección exitosa a la pantalla de portales
        setTimeout(() => {
          this.router.navigate(['/portales']); 
        }, 1500);
      },
      error: (err) => {
        console.error('Error de autenticación:', err);
        this.mensajeError = 'Usuario o contraseña incorrectos en la base de datos.';
        this.mensajeExito = '';
      }
    });
  }
}