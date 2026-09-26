import { ChangeDetectorRef, Component, NgZone, OnInit } from '@angular/core';
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
  isSubmitting: boolean = false;

  constructor(
    private fb: FormBuilder,
    private router: Router,
    private authService: AuthService,
    private ngZone: NgZone,
    private cdr: ChangeDetectorRef
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
    // Reenvío bloqueado mientras hay una petición en curso (doble click,
    // Enter repetido). La validación de formulario sigue su propio camino
    // debajo para no perder el mensaje de "completá los campos".
    if (this.isSubmitting) return;

    if (this.loginForm.invalid) {
      this.loginForm.markAllAsTouched();
      this.mensajeError = 'Deberá completar los campos para continuar.';
      return;
    }

    const { usuario, contrasena, recordarme } = this.loginForm.value;
    this.isSubmitting = true;
    this.mensajeError = '';

    this.authService.login(usuario, contrasena).subscribe({
      // ngZone.run() + cdr.detectChanges(): la respuesta de
      // authService.login() (HttpClient) no está disparando change
      // detection en este proyecto — confirmado inspeccionando el
      // componente en vivo: el campo se actualiza (mensajeError/
      // isSubmitting quedan bien) pero la vista se queda congelada hasta
      // el próximo evento que sí corra dentro de la zona. NgZone.run() solo
      // no alcanzó acá (a diferencia del sidebar/notificaciones), así que
      // se fuerza detectChanges() explícitamente como red de seguridad.
      next: () => {
        this.ngZone.run(() => {
          // Only persisted on a successful login, so a wrong attempt never
          // saves a username that turned out not to exist
          if (recordarme) {
            localStorage.setItem(REMEMBERED_USER_KEY, usuario);
          } else {
            localStorage.removeItem(REMEMBERED_USER_KEY);
          }

          this.mensajeExito = `¡Bienvenido al sistema del CACC!`;
          this.mensajeError = '';
          this.cdr.detectChanges();

          // isSubmitting se mantiene en true durante estos 1.5s: la
          // navegación es inminente, así que soltar el overlay acá solo
          // generaría un parpadeo (el form reaparece un instante y
          // enseguida esta pantalla se destruye al navegar).
          setTimeout(() => {
            this.ngZone.run(() => this.router.navigate(['/portales']));
          }, 1500);
        });
      },
      error: (err) => {
        this.ngZone.run(() => {
          console.error('Error de autenticación:', err);
          this.isSubmitting = false;
          this.mensajeError = 'Usuario o contraseña incorrectos en la base de datos.';
          this.mensajeExito = '';
          this.cdr.detectChanges();
        });
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