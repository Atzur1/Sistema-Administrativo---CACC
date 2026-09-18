import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../services/auth';

// Key used to persist the remembered username across visits, independent
// from the 'usuario' key AuthService uses to store the active session
const REMEMBERED_USER_KEY = 'usuarioRecordado';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './login.html',
  styleUrl: './login.css',
})
export class Login implements OnInit {
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
      contrasena: ['', [Validators.required]],
      recordarme: [false],
    });
  }

  ngOnInit() {
    // Pre-fill the username and tick the checkbox if a previous login left one saved
    const rememberedUser = localStorage.getItem(REMEMBERED_USER_KEY);
    if (rememberedUser) {
      this.loginForm.patchValue({ usuario: rememberedUser, recordarme: true });
    }
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

    const { usuario, contrasena, recordarme } = this.loginForm.value;

    this.authService.login(usuario, contrasena).subscribe({
      next: () => {
        // Only persisted on a successful login, so a wrong attempt never
        // saves a username that turned out not to exist
        if (recordarme) {
          localStorage.setItem(REMEMBERED_USER_KEY, usuario);
        } else {
          localStorage.removeItem(REMEMBERED_USER_KEY);
        }

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

  // No password-reset flow exists yet; this tells the user how to proceed
  // instead of the link silently doing nothing
  onForgotPassword(event: Event) {
    event.preventDefault();
    alert('Para restablecer tu contraseña, contactá al administrador del sistema.');
  }
}