import { ChangeDetectorRef, Component, NgZone, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../services/auth';

// Key used to persist the remembered username across visits, independent
// from the 'usuario' key AuthService uses to store the active session
const REMEMBERED_USER_KEY = 'usuarioRecordado';

// Cuánto tiempo queda el mensaje "¡Bienvenido!" solo en pantalla, sin el overlay
// tapándolo, para que se alcance a leer antes de que vuelva la pantalla de carga.
// Exportadas (no solo locales) para que login.spec.ts verifique la secuencia real
// en vez de hardcodear los mismos números por separado.
export const LECTURA_MENSAJE_EXITO_MS = 1400;
// Cuánto dura la segunda pasada del overlay (la de "ya confirmé, ahora te llevo a
// portales") antes de navegar. Corta a propósito: es una transición, no una espera real.
export const TRANSICION_A_PORTALES_MS = 700;

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
  // Bloquea el botón/el form durante todo el flujo (llamada + lectura del mensaje +
  // transición). No controla el overlay directamente — ver mostrarOverlay.
  isSubmitting: boolean = false;
  // Visibilidad del overlay de carga, independiente de isSubmitting: se apaga a
  // propósito mientras se lee el mensaje de éxito, para que se vea limpio sin el
  // blur encima, y se prende de nuevo un instante antes de navegar a portales.
  mostrarOverlay: boolean = false;

  // Modal "¿Olvidaste tu contraseña?" — reemplaza el window.prompt()/alert()
  // nativos por una tarjeta con la misma identidad visual que el resto del login.
  mostrarModalOlvido: boolean = false;
  forgotForm: FormGroup;
  forgotEnviando: boolean = false;
  forgotMensaje: string = '';
  forgotError: string = '';

  constructor(
    private fb: FormBuilder,
    private router: Router,
    private authService: AuthService,
    private ngZone: NgZone,
    private cdr: ChangeDetectorRef,
  ) {
    this.loginForm = this.fb.group({
      usuario: ['', [Validators.required]],
      contrasena: ['', [Validators.required]],
      recordarme: [false],
    });

    this.forgotForm = this.fb.group({
      usuario: ['', [Validators.required]],
    });
  }

  ngOnInit() {
    if (new URLSearchParams(window.location.search).get('sesion') === 'vencida') {
      this.mensajeError = 'Tu sesión venció o fue revocada. Iniciá sesión nuevamente.';
    }

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
    this.mostrarOverlay = true;
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
          // Se apaga el overlay acá a propósito: el mensaje de éxito tiene que
          // verse limpio, sin el blur encima, antes de que vuelva la pantalla
          // de carga y recién ahí se navegue a portales.
          this.mostrarOverlay = false;
          this.cdr.detectChanges();

          setTimeout(() => {
            this.ngZone.run(() => {
              this.mostrarOverlay = true;
              this.cdr.detectChanges();

              setTimeout(() => {
                this.ngZone.run(() => this.authService.irAlInicio(this.router));
              }, TRANSICION_A_PORTALES_MS);
            });
          }, LECTURA_MENSAJE_EXITO_MS);
        });
      },
      error: (err) => {
        this.ngZone.run(() => {
          console.error('Error de autenticación:', err);
          this.isSubmitting = false;
          this.mostrarOverlay = false;
          this.mensajeError =
            err.status === 401
              ? 'El DNI o la contraseña no coinciden con una cuenta activa.'
              : err.status === 429
                ? (err.error?.mensaje ?? 'Demasiados intentos. Esperá unos minutos antes de volver a intentar.')
                : err.status === 0
                  ? 'No se pudo conectar con la API. Verificá que el backend esté iniciado con HTTPS.'
                  : 'No se pudo completar el inicio de sesión. Revisá la configuración de la API o la base de datos.';
          this.mensajeExito = '';
          this.cdr.detectChanges();
        });
      },
    });
  }

  // El backend emite el enlace para cualquier cuenta con acceso al portal (SuperAdmin o Administrador) y responde
  // igual exista o no, para no revelar qué DNI o correos están registrados.
  onForgotPassword(event: Event) {
    event.preventDefault();
    this.forgotForm.reset();
    this.forgotMensaje = '';
    this.forgotError = '';
    this.forgotEnviando = false;
    this.mostrarModalOlvido = true;
  }

  cerrarModalOlvido() {
    if (this.forgotEnviando) return;
    this.mostrarModalOlvido = false;
  }

  enviarRecuperacion() {
    if (this.forgotEnviando) return;

    if (this.forgotForm.invalid) {
      this.forgotForm.markAllAsTouched();
      return;
    }

    const { usuario } = this.forgotForm.value;
    this.forgotEnviando = true;
    this.forgotError = '';
    this.forgotMensaje = '';

    this.authService.requestPasswordReset(usuario).subscribe({
      next: (response) => {
        this.ngZone.run(() => {
          this.forgotEnviando = false;
          this.forgotMensaje = response.mensaje;
          this.cdr.detectChanges();
        });
      },
      error: () => {
        this.ngZone.run(() => {
          this.forgotEnviando = false;
          this.forgotError = 'No se pudo procesar la solicitud. Intentá nuevamente más tarde.';
          this.cdr.detectChanges();
        });
      },
    });
  }
}
