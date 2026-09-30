import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { UsuariosPortalService } from '../../services/usuarios-portal';
import { NotificationService } from '../../shared/notifications/notification.service';
import { CustomSelect, CustomSelectOption } from '../../shared/custom-select/custom-select';
import {
  ROL_PORTAL_LABELS,
  RolPortal,
  UsuarioCandidato,
  UsuarioPortal,
} from '../../models/UsuarioPortalModel';

const ROL_OPTIONS: CustomSelectOption[] = [
  { value: 1, label: 'SuperAdmin' },
  { value: 2, label: 'Administrador' },
];

@Component({
  selector: 'app-usuarios',
  standalone: true,
  imports: [CommonModule, FormsModule, CustomSelect],
  templateUrl: './usuarios-y-permisos.html',
  styleUrl: './usuarios-y-permisos.css',
})
export class Usuarios implements OnInit {
  readonly rolOptions = ROL_OPTIONS;
  readonly rolLabels = ROL_PORTAL_LABELS;

  loading = signal(true);
  loadError = signal<string | null>(null);
  habilitados = signal<UsuarioPortal[]>([]);
  candidatos = signal<UsuarioCandidato[]>([]);
  deshabilitados = signal<UsuarioPortal[]>([]);

  // Fila de "Candidatos" cuyo desplegable de rol el SuperAdmin ya tocó, para
  // no perder la elección si el habilitar tarda un toque en confirmarse.
  rolElegido = new Map<number, RolPortal>();

  // "Editar candidato": corrige nombre/apellido/mail antes de habilitar, por
  // si el otro equipo cargó algo mal.
  dialogEditar = signal<UsuarioCandidato | null>(null);
  editarNombre = '';
  editarApellido = '';
  editarEmail = '';
  editarSubmitting = signal(false);
  editarError = signal<string | null>(null);

  // "Habilitar": popup de confirmación (muestra el mail antes de disparar el
  // correo de activación, así un error de carga del otro equipo no termina
  // mandándole acceso de administrador a un desconocido).
  dialogCandidato = signal<UsuarioCandidato | null>(null);
  dialogSubmitting = signal(false);
  dialogError = signal<string | null>(null);

  // "Deshabilitar": confirmación aparte, es la acción más destructiva de la
  // pantalla (corta la sesión de la persona en la siguiente request).
  confirmarBaja = signal<UsuarioPortal | null>(null);
  bajaSubmitting = signal(false);

  // "Reactivar": le devuelve el acceso a alguien ya deshabilitado, con su
  // mismo rol y contraseña — sin token ni mail nuevo.
  confirmarReactivar = signal<UsuarioPortal | null>(null);
  reactivarSubmitting = signal(false);

  constructor(
    private readonly usuariosService: UsuariosPortalService,
    private readonly notifications: NotificationService,
  ) {}

  ngOnInit(): void {
    this.cargar();
  }

  cargar(): void {
    this.loading.set(true);
    this.loadError.set(null);

    this.usuariosService.getHabilitados().subscribe({
      next: (rows) => this.habilitados.set(rows),
      error: () => this.loadError.set('No se pudo cargar la lista de usuarios habilitados.'),
    });

    this.usuariosService.getCandidatos().subscribe({
      next: (rows) => this.candidatos.set(rows),
      error: () => this.loadError.set('No se pudo cargar la lista de candidatos.'),
    });

    this.usuariosService
      .getDeshabilitados()
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (rows) => this.deshabilitados.set(rows),
        error: () => this.loadError.set('No se pudo cargar la lista de cuentas deshabilitadas.'),
      });
  }

  get totalHabilitados(): number {
    return this.habilitados().length;
  }

  get totalSuperAdmins(): number {
    return this.habilitados().filter((u) => u.rolPortal === 1).length;
  }

  get totalAdministradores(): number {
    return this.habilitados().filter((u) => u.rolPortal === 2).length;
  }

  rolParaCandidato(candidato: UsuarioCandidato): RolPortal {
    return this.rolElegido.get(candidato.idUsuario) ?? candidato.fkIdRolSugerido;
  }

  onRolCandidatoChange(candidato: UsuarioCandidato, valor: string | number | null): void {
    if (valor === 1 || valor === 2) {
      this.rolElegido.set(candidato.idUsuario, valor);
    }
  }

  // ===== Editar candidato (nombre / apellido / mail) =====

  abrirEditar(candidato: UsuarioCandidato): void {
    this.editarNombre = candidato.nombre;
    this.editarApellido = candidato.apellido;
    this.editarEmail = candidato.email;
    this.editarError.set(null);
    this.dialogEditar.set(candidato);
  }

  cerrarEditar(): void {
    if (this.editarSubmitting()) return;
    this.dialogEditar.set(null);
  }

  guardarEdicion(): void {
    const candidato = this.dialogEditar();
    if (!candidato || this.editarSubmitting()) return;

    const nombre = this.editarNombre.trim();
    const apellido = this.editarApellido.trim();
    const email = this.editarEmail.trim();
    if (!nombre || !apellido || !email) {
      this.editarError.set('Completá nombre, apellido y mail.');
      return;
    }

    this.editarSubmitting.set(true);
    this.editarError.set(null);

    this.usuariosService
      .editarCandidato(candidato.idUsuario, { nombre, apellido, email })
      .pipe(finalize(() => this.editarSubmitting.set(false)))
      .subscribe({
        next: () => {
          candidato.nombre = nombre;
          candidato.apellido = apellido;
          candidato.email = email;
          this.dialogEditar.set(null);
          this.notifications.notify('Datos corregidos.', 'success');
        },
        error: (error) => {
          this.editarError.set(error?.error?.mensaje ?? 'No se pudieron corregir los datos.');
        },
      });
  }

  // ===== Habilitar =====

  abrirHabilitar(candidato: UsuarioCandidato): void {
    this.dialogError.set(null);
    this.dialogCandidato.set(candidato);
  }

  cerrarHabilitar(): void {
    if (this.dialogSubmitting()) return;
    this.dialogCandidato.set(null);
  }

  confirmarHabilitar(): void {
    const candidato = this.dialogCandidato();
    if (!candidato || this.dialogSubmitting()) return;

    const rolPortal = this.rolParaCandidato(candidato);
    this.dialogSubmitting.set(true);
    this.dialogError.set(null);

    this.usuariosService
      .habilitar(candidato.idUsuario, { email: candidato.email, rolPortal })
      .pipe(finalize(() => this.dialogSubmitting.set(false)))
      .subscribe({
        next: (respuesta) => {
          this.notifications.notify(respuesta.mensaje, 'success');
          this.dialogCandidato.set(null);
          this.rolElegido.delete(candidato.idUsuario);
          this.cargar();
        },
        error: (error) => {
          this.dialogError.set(error?.error?.mensaje ?? 'No se pudo habilitar el acceso.');
        },
      });
  }

  // ===== Cambiar rol de alguien ya habilitado =====

  onRolHabilitadoChange(usuario: UsuarioPortal, valor: string | number | null): void {
    if (valor !== 1 && valor !== 2) return;
    if (valor === usuario.rolPortal) return;

    const anterior = usuario.rolPortal;
    usuario.rolPortal = valor;

    this.usuariosService.cambiarRol(usuario.idUsuario, { rolPortal: valor }).subscribe({
      next: (respuesta) => this.notifications.notify(respuesta.mensaje, 'success'),
      error: (error) => {
        usuario.rolPortal = anterior;
        this.notifications.notify(
          error?.error?.mensaje ?? 'No se pudo cambiar el rol. Verificá que quede al menos un SuperAdmin.',
          'error',
        );
      },
    });
  }

  // ===== Deshabilitar =====

  pedirBaja(usuario: UsuarioPortal): void {
    this.confirmarBaja.set(usuario);
  }

  cancelarBaja(): void {
    if (this.bajaSubmitting()) return;
    this.confirmarBaja.set(null);
  }

  confirmarBajaAccion(): void {
    const usuario = this.confirmarBaja();
    if (!usuario || this.bajaSubmitting()) return;

    this.bajaSubmitting.set(true);
    this.usuariosService
      .deshabilitar(usuario.idUsuario)
      .pipe(finalize(() => this.bajaSubmitting.set(false)))
      .subscribe({
        next: (respuesta) => {
          this.notifications.notify(respuesta.mensaje, 'success');
          this.confirmarBaja.set(null);
          this.cargar();
        },
        error: (error) => {
          this.notifications.notify(
            error?.error?.mensaje ?? 'No se pudo deshabilitar el acceso.',
            'error',
          );
          this.confirmarBaja.set(null);
        },
      });
  }

  // ===== Reactivar =====

  pedirReactivar(usuario: UsuarioPortal): void {
    this.confirmarReactivar.set(usuario);
  }

  cancelarReactivar(): void {
    if (this.reactivarSubmitting()) return;
    this.confirmarReactivar.set(null);
  }

  confirmarReactivarAccion(): void {
    const usuario = this.confirmarReactivar();
    if (!usuario || this.reactivarSubmitting()) return;

    this.reactivarSubmitting.set(true);
    this.usuariosService
      .reactivar(usuario.idUsuario)
      .pipe(finalize(() => this.reactivarSubmitting.set(false)))
      .subscribe({
        next: (respuesta) => {
          this.notifications.notify(respuesta.mensaje, 'success');
          this.confirmarReactivar.set(null);
          this.cargar();
        },
        error: (error) => {
          this.notifications.notify(
            error?.error?.mensaje ?? 'No se pudo reactivar el acceso.',
            'error',
          );
          this.confirmarReactivar.set(null);
        },
      });
  }
}
