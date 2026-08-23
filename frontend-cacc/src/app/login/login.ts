import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../services/auth';

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
  mostrarPassword: boolean = false;

  constructor(
    private fb: FormBuilder,
    private router: Router,
    private authService: AuthService
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

    const { usuario, contrasena } = this.loginForm.value;

    this.authService.login(usuario, contrasena).subscribe({
      next: () => {
        this.mensajeExito = `¡Bienvenido al sistema del CACC!`;
        this.mensajeError = '';
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