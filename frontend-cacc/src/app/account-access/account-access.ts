import { ChangeDetectorRef, Component, NgZone, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../services/auth';

// Cuánto se muestra el mensaje de éxito antes de volver al login — el mismo
// criterio de "que se alcance a leer" que usa login.ts tras un inicio de sesión.
export const LECTURA_MENSAJE_EXITO_MS = 1800;

const PASSWORD_MIN_LENGTH = 8;

@Component({
  selector: 'app-account-access',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  templateUrl: './account-access.html',
  styleUrl: './account-access.css',
})
export class AccountAccess implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  private readonly fb = inject(FormBuilder);
  private readonly ngZone = inject(NgZone);
  private readonly cdr = inject(ChangeDetectorRef);

  readonly passwordMinLength = PASSWORD_MIN_LENGTH;

  token = '';
  tokenInvalido = false;
  mensajeExito = '';
  mensajeError = '';
  submitting = false;
  mostrarPassword = false;

  readonly form = this.fb.group({
    password: ['', [Validators.required, Validators.minLength(PASSWORD_MIN_LENGTH), Validators.maxLength(128)]],
  });

  ngOnInit(): void {
    this.token = this.route.snapshot.queryParamMap.get('token') ?? '';
    if (!this.token) {
      this.tokenInvalido = true;
      this.mensajeError = 'El enlace no contiene un token válido. Solicitá uno nuevo desde el inicio de sesión.';
    }
  }

  togglePasswordVisibility(): void {
    this.mostrarPassword = !this.mostrarPassword;
  }

  get longitudActual(): number {
    return (this.form.controls.password.value ?? '').length;
  }

  get longitudCumplida(): boolean {
    return this.longitudActual >= this.passwordMinLength;
  }

  submit(): void {
    if (this.submitting || this.tokenInvalido) return;

    const password = this.form.controls.password.value;
    if (this.form.invalid || !password) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting = true;
    this.mensajeError = '';

    this.auth.completeAccountAccess(this.token, password).subscribe({
      next: (response) => {
        this.ngZone.run(() => {
          this.submitting = false;
          this.mensajeExito = response.mensaje || 'Tu contraseña fue restablecida correctamente.';
          this.form.disable();
          this.cdr.detectChanges();

          setTimeout(() => {
            this.ngZone.run(() => this.router.navigate(['/']));
          }, LECTURA_MENSAJE_EXITO_MS);
        });
      },
      error: (error) => {
        this.ngZone.run(() => {
          this.submitting = false;
          this.mensajeError =
            error.status === 400
              ? 'El enlace no es válido o venció. Solicitá uno nuevo desde el inicio de sesión.'
              : 'No se pudo completar la operación. Intentá nuevamente.';
          this.cdr.detectChanges();
        });
      },
    });
  }
}
