import { ChangeDetectorRef, Component, NgZone, OnDestroy, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../services/auth';

// Cuánto se muestra el mensaje de éxito antes de volver al login: lo justo para leerlo. Además hay un
// botón para ir enseguida, así que nadie queda esperando.
export const LECTURA_MENSAJE_EXITO_MS = 2600;

// El servidor exige entre 8 y 128 caracteres (CompleteAccountAccessRequest): es lo único que bloquea.
// El resto de los criterios de abajo son recomendaciones que solo se muestran.
const PASSWORD_MIN_LENGTH = 8;
const PASSWORD_MAX_LENGTH = 128;

// Dos pantallas con el mismo mecanismo (token de un solo uso) pero situaciones distintas:
//  - 'crear':       primer ingreso, llega por el correo "Crea tu contraseña" (/crear-contrasena)
//  - 'restablecer': olvidé mi contraseña, llega por el correo "Reestablece tu contraseña" (/restablecer-contrasena)
export type ModoAcceso = 'crear' | 'restablecer';

interface TextosModo {
  etiqueta: string;
  titulo: string;
  subtitulo: string;
  boton: string;
  botonGuardando: string;
  exitoTitulo: string;
  exitoDetalle: string;
  enlaceInvalidoConsejo: string;
}

export const TEXTOS_POR_MODO: Record<ModoAcceso, TextosModo> = {
  crear: {
    etiqueta: 'Primer ingreso',
    titulo: 'Crea tu contraseña',
    subtitulo: 'Tu acceso al Portal Administrativo CACC ya está habilitado. Elegí una contraseña segura para empezar.',
    boton: 'Crear contraseña',
    botonGuardando: 'Creando…',
    exitoTitulo: '¡Contraseña creada!',
    exitoDetalle: 'Ya podés ingresar al Portal Administrativo con tu usuario y tu nueva contraseña.',
    enlaceInvalidoConsejo: 'Pedile a un administrador que vuelva a habilitar tu acceso y te va a llegar un correo nuevo.',
  },
  restablecer: {
    etiqueta: 'Recuperar acceso',
    titulo: 'Reestablece tu contraseña',
    subtitulo: 'Elegí una contraseña nueva para tu cuenta. Por seguridad, vamos a cerrar las sesiones que tengas abiertas.',
    boton: 'Reestablecer contraseña',
    botonGuardando: 'Guardando…',
    exitoTitulo: '¡Contraseña reestablecida!',
    exitoDetalle: 'Ya podés ingresar con tu nueva contraseña.',
    enlaceInvalidoConsejo: 'Solicitá uno nuevo desde el inicio de sesión, con “¿Olvidaste tu contraseña?”.',
  },
};

export interface Requisito {
  id: 'largo' | 'mayusculasMinusculas' | 'numero' | 'simbolo';
  texto: string;
  cumplido: boolean;
  // Los obligatorios bloquean el envío; los demás son recomendaciones.
  obligatorio: boolean;
}

export interface Fortaleza {
  // 0 = vacía … 4 = fuerte
  nivel: 0 | 1 | 2 | 3 | 4;
  etiqueta: string;
}

export function evaluarRequisitos(password: string): Requisito[] {
  return [
    { id: 'largo', texto: `Al menos ${PASSWORD_MIN_LENGTH} caracteres`, cumplido: password.length >= PASSWORD_MIN_LENGTH, obligatorio: true },
    { id: 'mayusculasMinusculas', texto: 'Mayúsculas y minúsculas', cumplido: /[a-záéíóúñ]/.test(password) && /[A-ZÁÉÍÓÚÑ]/.test(password), obligatorio: false },
    { id: 'numero', texto: 'Al menos un número', cumplido: /\d/.test(password), obligatorio: false },
    { id: 'simbolo', texto: 'Un símbolo (por ejemplo ! ? # $)', cumplido: /[^A-Za-z0-9áéíóúñÁÉÍÓÚÑ\s]/.test(password), obligatorio: false },
  ];
}

export function calcularFortaleza(password: string): Fortaleza {
  if (!password) return { nivel: 0, etiqueta: '' };
  if (password.length < PASSWORD_MIN_LENGTH) return { nivel: 1, etiqueta: 'Muy corta' };

  const extras = evaluarRequisitos(password).filter((r) => !r.obligatorio && r.cumplido).length + (password.length >= 12 ? 1 : 0);
  if (extras <= 1) return { nivel: 2, etiqueta: 'Aceptable' };
  if (extras === 2) return { nivel: 3, etiqueta: 'Buena' };
  return { nivel: 4, etiqueta: 'Fuerte' };
}

// Las dos contraseñas tienen que ser iguales: es lo que evita guardar una con un error de tipeo.
function coincidenContrasenas(control: AbstractControl): ValidationErrors | null {
  const password = control.get('password')?.value ?? '';
  const confirmacion = control.get('confirmacion')?.value ?? '';
  return confirmacion && password !== confirmacion ? { noCoinciden: true } : null;
}

type EstadoPantalla = 'formulario' | 'exito' | 'enlaceInvalido';

@Component({
  selector: 'app-account-access',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  templateUrl: './account-access.html',
  styleUrl: './account-access.css',
})
export class AccountAccess implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  private readonly fb = inject(FormBuilder);
  private readonly ngZone = inject(NgZone);
  private readonly cdr = inject(ChangeDetectorRef);

  readonly passwordMinLength = PASSWORD_MIN_LENGTH;

  modo: ModoAcceso = 'restablecer';
  textos: TextosModo = TEXTOS_POR_MODO.restablecer;

  token = '';
  estado: EstadoPantalla = 'formulario';
  mensajeError = '';
  submitting = false;
  mostrarPassword = false;
  mostrarConfirmacion = false;
  capsActivo = false;

  private temporizadorRedireccion: ReturnType<typeof setTimeout> | null = null;

  readonly form = this.fb.group(
    {
      password: ['', [Validators.required, Validators.minLength(PASSWORD_MIN_LENGTH), Validators.maxLength(PASSWORD_MAX_LENGTH)]],
      confirmacion: ['', [Validators.required]],
    },
    { validators: coincidenContrasenas },
  );

  ngOnInit(): void {
    // La ruta (/crear-contrasena o /restablecer-contrasena) decide qué pantalla es.
    this.modo = this.route.snapshot.data['modo'] === 'crear' ? 'crear' : 'restablecer';
    this.textos = TEXTOS_POR_MODO[this.modo];

    this.token = this.route.snapshot.queryParamMap.get('token') ?? '';
    if (!this.token) {
      this.estado = 'enlaceInvalido';
    }
  }

  ngOnDestroy(): void {
    this.cancelarRedireccion();
  }

  // ===== Estado de la contraseña que se escribe =====

  get password(): string {
    return this.form.controls.password.value ?? '';
  }

  get requisitos(): Requisito[] {
    return evaluarRequisitos(this.password);
  }

  get fortaleza(): Fortaleza {
    return calcularFortaleza(this.password);
  }

  get confirmacionNoCoincide(): boolean {
    const confirmacion = this.form.controls.confirmacion;
    return this.form.hasError('noCoinciden') && (confirmacion.dirty || confirmacion.touched);
  }

  get confirmacionCoincide(): boolean {
    const confirmacion = this.form.controls.confirmacion.value ?? '';
    return !!confirmacion && confirmacion === this.password;
  }

  // ===== Interacción =====

  togglePasswordVisibility(): void {
    this.mostrarPassword = !this.mostrarPassword;
  }

  toggleConfirmacionVisibility(): void {
    this.mostrarConfirmacion = !this.mostrarConfirmacion;
  }

  // Aviso de Bloq Mayús activado: en un campo de contraseña es la causa más común de "no me deja".
  detectarMayusculas(event: KeyboardEvent): void {
    this.capsActivo = event.getModifierState?.('CapsLock') ?? false;
  }

  submit(): void {
    if (this.submitting || this.estado !== 'formulario') return;

    const password = this.form.controls.password.value;
    if (this.form.invalid || !password) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting = true;
    this.mensajeError = '';

    this.auth.completeAccountAccess(this.token, password).subscribe({
      next: () => {
        this.ngZone.run(() => {
          this.submitting = false;
          this.estado = 'exito';
          this.form.disable();
          this.cdr.detectChanges();

          this.temporizadorRedireccion = setTimeout(() => this.irAlLogin(), LECTURA_MENSAJE_EXITO_MS);
        });
      },
      error: (error) => {
        this.ngZone.run(() => {
          this.submitting = false;
          if (error.status === 400) {
            // El token no sirve (vencido, ya usado o inexistente): no tiene sentido seguir escribiendo.
            this.estado = 'enlaceInvalido';
          } else {
            this.mensajeError = 'No se pudo completar la operación. Intentá nuevamente.';
          }
          this.cdr.detectChanges();
        });
      },
    });
  }

  irAlLogin(): void {
    this.cancelarRedireccion();
    this.ngZone.run(() => this.router.navigate(['/']));
  }

  private cancelarRedireccion(): void {
    if (this.temporizadorRedireccion !== null) {
      clearTimeout(this.temporizadorRedireccion);
      this.temporizadorRedireccion = null;
    }
  }
}
