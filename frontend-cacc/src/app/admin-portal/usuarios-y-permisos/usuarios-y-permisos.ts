import { Component, HostListener, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { UsuariosPortalService } from '../../services/usuarios-portal';
import { AuthService } from '../../services/auth';
import { NotificationService } from '../../shared/notifications/notification.service';
import { CustomSelect, CustomSelectOption } from '../../shared/custom-select/custom-select';
import { normalizeText } from '../../shared/normalize-text';
import { Ventana } from '../../shared/ventana/ventana';
import { PortalUserCandidateModel, PortalUserModel, ROL_PORTAL_LABELS, RolPortal } from '../../models/PortalUserModel';

const ROL_OPTIONS: CustomSelectOption[] = [
  { value: 1, label: 'SuperAdmin' },
  { value: 2, label: 'Administrador' },
];

export type PestanaUsuarios = 'habilitadas' | 'candidatos' | 'deshabilitadas';

// Un cambio de rol pendiente de confirmación: todavía no se aplicó, se aplica recién al confirmar.
export interface CambioDeRol {
  usuario: PortalUserModel;
  rol: RolPortal;
}

@Component({
  selector: 'app-usuarios',
  standalone: true,
  imports: [CommonModule, FormsModule, CustomSelect, Ventana],
  templateUrl: './usuarios-y-permisos.html',
  styleUrl: './usuarios-y-permisos.css',
})
export class Usuarios implements OnInit {
  readonly rolOptions = ROL_OPTIONS;
  readonly rolLabels = ROL_PORTAL_LABELS;

  private readonly authService = inject(AuthService);

  // Lista que se muestra: una por vez, con el contador de cada una en su pestaña.
  pestana = signal<PestanaUsuarios>('habilitadas');

  loading = signal(true);
  loadError = signal<string | null>(null);
  habilitados = signal<PortalUserModel[]>([]);
  candidatos = signal<PortalUserCandidateModel[]>([]);
  deshabilitados = signal<PortalUserModel[]>([]);

  // Búsqueda sobre la lista que se está viendo (nombre, apellido, DNI o correo). Los contadores de las pestañas
  // siguen mostrando el total, no el resultado de la búsqueda.
  busqueda = signal('');
  readonly habilitadosVisibles = computed(() => this.filtrar(this.habilitados()));
  readonly candidatosVisibles = computed(() => this.filtrar(this.candidatos()));
  readonly deshabilitadosVisibles = computed(() => this.filtrar(this.deshabilitados()));

  // Fila de "Candidatos" cuyo desplegable de rol el SuperAdmin ya tocó, para
  // no perder la elección si el habilitar tarda un toque en confirmarse.
  rolElegido = new Map<number, RolPortal>();

  // Menú "⋯" de una fila de Habilitadas abierto (id de la cuenta), o null.
  menuAbierto = signal<number | null>(null);
  // El botón "⋯" que abrió el menú. Las ventanas que se abren desde una opción del menú le devuelven el foco al
  // cerrarse: la opción en sí desaparece al cerrarse el menú (HU-094, corrección de QA).
  botonMenu: HTMLElement | null = null;

  // "Cambiar rol": un cambio de rol da o quita permisos, así que no se aplica al elegirlo en el desplegable sino
  // después de confirmarlo. `refrescoRoles` vuelve a dibujar los desplegables cuando se cancela, para que
  // muestren de nuevo el rol que la cuenta realmente tiene.
  cambioDeRol = signal<CambioDeRol | null>(null);
  rolSubmitting = signal(false);
  refrescoRoles = signal(0);

  // "Editar candidato": corrige nombre/apellido/DNI (no el mail) antes de habilitar, por
  // si hubo un error de carga. El DNI es el usuario con el que va a ingresar.
  dialogEditar = signal<PortalUserCandidateModel | null>(null);
  editarNombre = '';
  editarApellido = '';
  editarDni = '';
  editarSubmitting = signal(false);
  editarError = signal<string | null>(null);

  // "Habilitar": popup de confirmación (muestra el mail antes de disparar el
  // correo de activación, así un error de carga del otro equipo no termina
  // mandándole acceso de administrador a un desconocido).
  dialogCandidato = signal<PortalUserCandidateModel | null>(null);
  dialogSubmitting = signal(false);
  dialogError = signal<string | null>(null);

  // "Deshabilitar": confirmación aparte, es la acción más destructiva de la
  // pantalla (corta la sesión de la persona en la siguiente request).
  confirmarBaja = signal<PortalUserModel | null>(null);
  bajaSubmitting = signal(false);

  // "Reactivar": le devuelve el acceso a alguien ya deshabilitado, con su misma contraseña y el rol que
  // elija el SuperAdmin (arranca en el que tenía) — sin token ni mail nuevo.
  confirmarReactivar = signal<PortalUserModel | null>(null);
  reactivarSubmitting = signal(false);
  rolReactivar = signal<RolPortal>(2);

  // "Resetear contraseña": el SuperAdmin le manda a la cuenta el enlace para elegir una nueva. Va siempre al
  // correo de la propia cuenta: el SuperAdmin no ve ni fija la contraseña de nadie. Si la cuenta todavía no
  // había creado su contraseña, la misma acción reenvía el correo de "Crea tu contraseña".
  confirmarEnlace = signal<PortalUserModel | null>(null);
  enlaceSubmitting = signal(false);

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

  cambiarPestana(pestana: PestanaUsuarios): void {
    this.pestana.set(pestana);
    this.menuAbierto.set(null);
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

  // Personas que esperan que se las habilite: es lo único de la pantalla que pide una acción, por eso se destaca
  // en el encabezado, en la pestaña y en un aviso.
  get totalPendientes(): number {
    return this.candidatos().length;
  }

  // ===== Búsqueda =====

  alEscribirBusqueda(texto: string): void {
    this.busqueda.set(texto);
  }

  limpiarBusqueda(): void {
    this.busqueda.set('');
  }

  private filtrar<T extends { nombre: string; apellido: string; dni?: string | null; email: string }>(filas: T[]): T[] {
    const termino = normalizeText(this.busqueda().trim());
    if (!termino) return filas;
    const digitos = termino.replace(/\D/g, '');
    return filas.filter(
      (fila) =>
        normalizeText(`${fila.nombre} ${fila.apellido} ${fila.email}`).includes(termino) ||
        (digitos.length > 0 && (fila.dni ?? '').replace(/\D/g, '').includes(digitos)),
    );
  }

  // ===== Cómo se nombra cada persona =====

  // Hay cuentas sin nombre cargado (solo correo): se muestra el correo en su lugar, y su avatar sale de él.
  tieneNombre(persona: { nombre?: string | null; apellido?: string | null }): boolean {
    return !!`${persona.nombre ?? ''}${persona.apellido ?? ''}`.trim();
  }

  nombreMostrar(persona: { nombre?: string | null; apellido?: string | null; email: string }): string {
    return this.tieneNombre(persona) ? `${persona.nombre ?? ''} ${persona.apellido ?? ''}`.trim() : persona.email;
  }

  iniciales(persona: { nombre?: string | null; apellido?: string | null; email: string }): string {
    const letras = this.tieneNombre(persona)
      ? `${(persona.nombre ?? '').trim().charAt(0)}${(persona.apellido ?? '').trim().charAt(0)}`
      : persona.email.trim().charAt(0);
    return letras.toUpperCase();
  }

  // ===== Mi cuenta =====

  // La fila es la cuenta con la que se inició sesión. Se reconoce por DNI o por correo.
  esMiCuenta(usuario: { dni?: string | null; email: string }): boolean {
    const yo = this.authService.getUsuario();
    if (!yo) return false;
    const dni = (valor?: string | null) => (valor ?? '').replace(/\D/g, '');
    return (
      (!!dni(yo.dni) && dni(yo.dni) === dni(usuario.dni)) ||
      (!!yo.email && yo.email.toLowerCase() === usuario.email.toLowerCase())
    );
  }

  // ===== Menú de acciones de una fila =====

  alternarMenu(idUsuario: number, evento: Event): void {
    evento.stopPropagation();
    this.botonMenu = evento.currentTarget as HTMLElement | null;
    this.menuAbierto.set(this.menuAbierto() === idUsuario ? null : idUsuario);
  }

  // Tocar en cualquier otro lado, o apretar Esc, cierra el menú.
  @HostListener('document:click')
  @HostListener('document:keydown.escape')
  cerrarMenu(): void {
    if (this.menuAbierto() !== null) this.menuAbierto.set(null);
  }

  rolParaCandidato(candidato: PortalUserCandidateModel): RolPortal {
    return this.rolElegido.get(candidato.idUsuario) ?? candidato.fkIdRolSugerido;
  }

  onRolCandidatoChange(candidato: PortalUserCandidateModel, valor: string | number | null): void {
    if (valor === 1 || valor === 2) {
      this.rolElegido.set(candidato.idUsuario, valor);
    }
  }

  // ===== Editar candidato (nombre / apellido / DNI; el mail se muestra pero no se modifica) =====

  abrirEditar(candidato: PortalUserCandidateModel): void {
    this.editarNombre = candidato.nombre;
    this.editarApellido = candidato.apellido;
    this.editarDni = candidato.dni;
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
    const dni = this.editarDni.trim();
    if (!nombre || !apellido || !dni) {
      this.editarError.set('Completá nombre, apellido y DNI.');
      return;
    }

    this.editarSubmitting.set(true);
    this.editarError.set(null);

    this.usuariosService
      .editarCandidato(candidato.idUsuario, { nombre, apellido, dni })
      .pipe(finalize(() => this.editarSubmitting.set(false)))
      .subscribe({
        next: () => {
          candidato.nombre = nombre;
          candidato.apellido = apellido;
          candidato.dni = dni.replace(/\D/g, ''); // el servidor lo guarda solo con dígitos
          this.dialogEditar.set(null);
          this.notifications.notify('Datos corregidos.', 'success');
        },
        error: (error) => {
          this.editarError.set(error?.error?.mensaje ?? 'No se pudieron corregir los datos.');
        },
      });
  }

  // ===== Habilitar =====

  abrirHabilitar(candidato: PortalUserCandidateModel): void {
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

  // Elegir otro rol en el desplegable NO lo aplica: abre una confirmación que explica qué cambia. Un clic de más
  // en una lista de permisos no puede dar acceso total a alguien ni dejar a un SuperAdmin sin el suyo.
  onRolHabilitadoChange(usuario: PortalUserModel, valor: string | number | null): void {
    if (valor !== 1 && valor !== 2) return;
    if (valor === usuario.rolPortal) return;

    this.menuAbierto.set(null);
    this.cambioDeRol.set({ usuario, rol: valor });
  }

  // Qué le dice la confirmación a quien está por cambiar un rol.
  get textoCambioDeRol(): { titulo: string; cuerpo: string; advertencia: string | null } | null {
    const cambio = this.cambioDeRol();
    if (!cambio) return null;

    const nombre = `${cambio.usuario.nombre} ${cambio.usuario.apellido}`.trim();
    const aSuperAdmin = cambio.rol === 1;
    return {
      titulo: aSuperAdmin ? 'Dar acceso total (SuperAdmin)' : 'Quitar el acceso total',
      cuerpo: aSuperAdmin
        ? `${nombre} va a poder ver y cambiar todo el portal: usuarios y permisos, aranceles, reportes y auditoría, además de cobranza.`
        : `${nombre} va a pasar a Administrador: solo va a ver Cobranza (cuotas y pagos, deudas y becas). Deja de ver usuarios y permisos, aranceles, reportes y auditoría.`,
      advertencia: this.esMiCuenta(cambio.usuario)
        ? aSuperAdmin
          ? null
          : 'Es tu propia cuenta: si te quitás el acceso total, no vas a poder volver a dártelo vos.'
        : null,
    };
  }

  cancelarCambioDeRol(): void {
    if (this.rolSubmitting()) return;
    this.cambioDeRol.set(null);
    // El desplegable ya muestra el rol nuevo; se vuelve a dibujar para que vuelva a mostrar el real.
    this.refrescoRoles.update((n) => n + 1);
  }

  confirmarCambioDeRol(): void {
    const cambio = this.cambioDeRol();
    if (!cambio || this.rolSubmitting()) return;

    this.rolSubmitting.set(true);
    this.usuariosService
      .cambiarRol(cambio.usuario.idUsuario, { rolPortal: cambio.rol })
      .pipe(finalize(() => this.rolSubmitting.set(false)))
      .subscribe({
        next: (respuesta) => {
          cambio.usuario.rolPortal = cambio.rol;
          this.cambioDeRol.set(null);
          this.notifications.notify(respuesta.mensaje, 'success');
        },
        error: (error) => {
          this.cambioDeRol.set(null);
          this.refrescoRoles.update((n) => n + 1);
          this.notifications.notify(
            error?.error?.mensaje ?? 'No se pudo cambiar el rol. Verificá que quede al menos un SuperAdmin.',
            'error',
          );
        },
      });
  }

  // ===== Deshabilitar =====

  pedirBaja(usuario: PortalUserModel): void {
    this.menuAbierto.set(null);
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

  // ===== Resetear contraseña =====

  textoAccionEnlace(usuario: PortalUserModel): string {
    return usuario.activacionPendiente ? 'Reenviar activación' : 'Resetear contraseña';
  }

  pedirEnlace(usuario: PortalUserModel): void {
    this.menuAbierto.set(null);
    this.confirmarEnlace.set(usuario);
  }

  cancelarEnlace(): void {
    if (this.enlaceSubmitting()) return;
    this.confirmarEnlace.set(null);
  }

  confirmarEnlaceAccion(): void {
    const usuario = this.confirmarEnlace();
    if (!usuario || this.enlaceSubmitting()) return;

    this.enlaceSubmitting.set(true);
    this.usuariosService
      .resetearContrasena(usuario.idUsuario)
      .pipe(finalize(() => this.enlaceSubmitting.set(false)))
      .subscribe({
        next: (respuesta) => {
          this.notifications.notify(respuesta.mensaje, 'success');
          this.confirmarEnlace.set(null);
        },
        error: (error) => {
          this.notifications.notify(
            error?.error?.mensaje ?? 'No se pudo enviar el correo. Intentá de nuevo.',
            'error',
          );
          this.confirmarEnlace.set(null);
        },
      });
  }

  // ===== Reactivar =====

  pedirReactivar(usuario: PortalUserModel): void {
    this.rolReactivar.set(usuario.rolPortal);
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
      .reactivar(usuario.idUsuario, this.rolReactivar())
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
