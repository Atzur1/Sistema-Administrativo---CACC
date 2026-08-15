import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Router } from '@angular/router';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule
  ],
  templateUrl: './login.html',
  styleUrl: './login.css',
})
export class Login {
  loginForm: FormGroup;
  mensajeError: string = '';
  mensajeExito: string = '';

  constructor(private fb: FormBuilder, private router: Router) {
    this.loginForm = this.fb.group({
      usuario: ['', [Validators.required]],
      password: ['', [Validators.required]]
    });
  }

  onSubmit() {
    // Campos vacíos e indicación al usuario
    if (this.loginForm.invalid) {
      this.loginForm.markAllAsTouched();
      this.mensajeError = 'Deberá completar los campos para continuar.';
      this.mensajeExito = '';
      return;
    }

    const { usuario, password } = this.loginForm.value;

    // Validación de acceso 
    if (usuario === 'admin' && password === '1234') {
      this.mensajeExito = `Bienvenido @${usuario} al sistema`;
      this.mensajeError = '';
      
      // Requerimiento y Redirección al panel de control respectivo
      setTimeout(() => {
        this.router.navigate(['/dashboard']);
      }, 1500);
    } else {
      // Manejo de errores claros por credenciales incorrectas
      this.mensajeError = 'Usuario o contraseña incorrectos.';
      this.mensajeExito = '';
    }
  }
}