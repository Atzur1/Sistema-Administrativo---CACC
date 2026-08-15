import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Router } from '@angular/router';

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

  // Método para alternar el estado del ícono del ojo
  togglePasswordVisibility() {
    this.mostrarPassword = !this.mostrarPassword;
  }

  // Lista simulada de usuarios registrados por el Administrador (Mock Data)
  private usuariosRegistrados = [
    { usuario: 'admin', password: '123' },
    { usuario: 'cmonelli', password: 'cacc123' },
    { usuario: 'staff', password: 'password' }
  ];

  constructor(private fb: FormBuilder, private router: Router) {
    this.loginForm = this.fb.group({
      usuario: ['', [Validators.required]],
      password: ['', [Validators.required]]
    });
  }

  onSubmit() {
    if (this.loginForm.invalid) {
      this.loginForm.markAllAsTouched();
      this.mensajeError = 'Deberá completar los campos para continuar.';
      return;
    }

    const { usuario, password } = this.loginForm.value;

    // Validación estricta contra nuestra lista de usuarios registrados
    const usuarioValido = this.usuariosRegistrados.find(
      u => u.usuario === usuario && u.password === password
    );

    if (usuarioValido) {
      this.mensajeExito = `Bienvenido ${usuario} al sistema`;
      this.mensajeError = '';
      
      // Redirección al panel de control
      setTimeout(() => {
        this.router.navigate(['/portales']); 
      }, 1500);
    } else {
      this.mensajeError = 'Usuario o contraseña incorrectos.';
      this.mensajeExito = '';
    }
  }
}