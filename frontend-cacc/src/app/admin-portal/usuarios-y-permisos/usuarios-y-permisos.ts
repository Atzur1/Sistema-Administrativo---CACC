import { Component, DestroyRef, HostListener, OnInit, computed, effect, inject, signal, untracked } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule, NgModel } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { UsuariosPortalService } from '../../services/usuarios-portal';
import { AuthService } from '../../services/auth';
import { PendingActionsService } from '../../services/pending-actions';
import { RoleChangesService, RoleState } from '../../services/role-changes';
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
const PESTANAS: readonly PestanaUsuarios[] = ['habilitadas', 'candidatos', 'deshabilitadas'];

// Un cambio de rol pendiente de confirmación: todavía no se aplicó, se aplica recién al confirmar.
export interface CambioDeRol {
  usuario: PortalUserModel;
  rol: RolPortal;
  // El desplegable de la fila: si el cambio no se aplica, vuelve a mostrar el rol real y recibe el foco.
  modelo?: NgModel;
  selector?: CustomSelect;
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
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  // HU-104: los candidatos son un pendiente de la campanita. Se leen del estado compartido (la misma consulta que usa
  // la campanita, como máximo una por minuto) y se actualizan ahí al habilitar o corregir a alguien.
  private readonly pendientes = inject(PendingActionsService);
  // HU-105: cambios de rol en curso o que todavía se pueden deshacer, y el último estado confirmado de cada cuenta.
  readonly cambiosDeRol = inject(RoleChangesService);
  // La relectura que ya estaba al abrir la pantalla es vieja: la pantalla carga su propia lista.
  private relecturaVista = this.cambiosDeRol.snapshot();

  // Lista que se muestra: una por vez, con el contador de cada una en su pestaña. Va también en la dirección
  // (?pestana=candidatos) para poder entrar directo a una pestaña desde la campanita.
  pestana = signal<PestanaUsuarios>('habilitadas');

  loading = signal(true);
  // Errores de las listas que carga esta pantalla (habilitadas y deshabilitadas).
  private errorListas = signal<string | null>(null);
  habilitados = signal<PortalUserModel[]>([]);
  readonly candidatos = computed<PortalUserCandidateModel[]>(() => this.pendientes.candidates() ?? []);
  deshabilitados = signal<PortalUserModel[]>([]);

  readonly loadError = computed<string | null>(() => {
    if (this.errorListas()) return this.errorListas();
    if (this.pendientes.candidatesStatus() !== 'error') return null;
    return this.pendientes.candidates() === null
      ? 'No se pudo cargar la lista de candidatos.'
      : 'No se pudo actualizar la lista de candidatos: se muestran los últimos datos obtenidos.';
  });

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

  // "Cambiar rol" de la propia cuenta: no se aplica al elegirlo en el desplegable sino después de confirmarlo. Si se
  // cancela (o el servidor lo rechaza), el desplegable vuelve a mostrar el rol que la cuenta realmente tiene (ver
  // restaurarDesplegable). El de otra cuenta se aplica en el acto y se puede deshacer (HU-105).
  cambioDeRol = signal<CambioDeRol | null>(null);
  rolSubmitting = signal(false);

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
  ) {
    // Un cambio o un "Deshacer" confirmado por el servidor (aunque se haya hecho desde otra pantalla) se refleja en la
    // fila si es más nuevo que lo que la fila muestra.
    effect(() => {
      const estados = this.cambiosDeRol.states();
      untracked(() => this.habilitados.update((filas) => aplicarEstados(filas, estados)));
    });
    // Tras una demora o un conflicto, el coordinador relee las cuentas habilitadas: esa lectura reemplaza la lista.
    effect(() => {
      const relectura = this.cambiosDeRol.snapshot();
      if (!relectura || relectura === this.relecturaVista) return;
      this.relecturaVista = relectura;
      untracked(() => this.habilitados.set(aplicarEstados(relectura, this.cambiosDeRol.states())));
    });
  }

  ngOnInit(): void {
    // Normalmente ya lo inició el Portal Administrativo: llamarlo de nuevo no consulta otra vez.
    this.pendientes.start();
    this.cargar();

    // ?pestana=candidatos (o habilitadas / deshabilitadas). Escucha los cambios para que el enlace de la campanita
    // funcione también si ya se está en esta pantalla; un valor desconocido deja la pestaña de habilitadas.
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const pedida = params.get('pestana');
      this.pestana.set(PESTANAS.includes(pedida as PestanaUsuarios) ? (pedida as PestanaUsuarios) : 'habilitadas');
      this.menuAbierto.set(null);
    });
  }

  // Habilitadas y deshabilitadas. Los candidatos no se piden acá: vienen del estado compartido de pendientes.
  cargar(): void {
    this.loading.set(true);
    this.errorListas.set(null);

    this.usuariosService.getHabilitados().subscribe({
      next: (rows) => this.habilitados.set(aplicarEstados(rows, this.cambiosDeRol.states())),
      error: () => this.errorListas.set('No se pudo cargar la lista de usuarios habilitados.'),
    });

    this.usuariosService
      .getDeshabilitados()
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (rows) => this.deshabilitados.set(rows),
        error: () => this.errorListas.set('No se pudo cargar la lista de cuentas deshabilitadas.'),
      });
  }

  // La pestaña elegida queda en la dirección (reemplazándola, sin sumar pasos al historial del navegador).
  cambiarPestana(pestana: PestanaUsuarios): void {
    this.pestana.set(pestana);
    this.menuAbierto.set(null);
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { pestana: pestana === 'habilitadas' ? null : pestana },
      queryParamsHandling: 'merge',
      replaceUrl: true,
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
          // Sigue siendo candidata, con sus datos nuevos (el servidor guarda el DNI solo con dígitos).
          this.pendientes.updateCandidate(candidato.idUsuario, { nombre, apellido, dni });
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
          // HU-104: ya tiene acceso (aunque haya fallado el correo, la respuesta es 200): deja de ser candidata en la
          // campanita y en esta lista, sin volver a pedir candidatos. Habilitadas sí se vuelve a pedir.
          this.pendientes.resolveCandidate(candidato.idUsuario);
          this.cargar();
        },
        error: (error) => {
          this.dialogError.set(error?.error?.mensaje ?? 'No se pudo habilitar el acceso.');
        },
      });
  }

  // ===== Cambiar rol de alguien ya habilitado =====

  // HU-105: el rol de otra cuenta se cambia en el acto y se puede deshacer durante 5000 ms desde el aviso (es un
  // cambio reversible). El de la propia cuenta sigue pidiendo confirmación: quitarse el acceso total no se puede
  // deshacer desde esta misma sesión, porque el cambio la corta.
  onRolHabilitadoChange(usuario: PortalUserModel, valor: string | number | null, modelo?: NgModel, selector?: CustomSelect): void {
    if (valor !== 1 && valor !== 2) return;
    if (valor === usuario.rolPortal) return;

    this.menuAbierto.set(null);
    const cambio: CambioDeRol = { usuario, rol: valor, modelo, selector };
    if (this.esMiCuenta(usuario)) {
      this.cambioDeRol.set(cambio);
      return;
    }
    void this.aplicarCambioDeRol(cambio, true);
  }

  // Qué se ve debajo del rol mientras la cuenta tiene una operación abierta (y no acepta otro cambio).
  estadoDelRol(usuario: PortalUserModel): string | null {
    switch (this.cambiosDeRol.phase(usuario.idUsuario)) {
      case 'saving': return 'Guardando…';
      case 'undoable': return 'Se puede deshacer';
      case 'undoing': return 'Deshaciendo…';
      default: return null;
    }
  }

  private async aplicarCambioDeRol(cambio: CambioDeRol, undoable: boolean): Promise<void> {
    const desenlace = await this.cambiosDeRol.change(cambio.usuario, cambio.rol, { undoable });
    if (desenlace === 'applied') return;
    // El rol quedó como estaba (o no se sabe): el desplegable vuelve a mostrar el que la cuenta tiene confirmado.
    this.restaurarDesplegable(cambio);
    if (desenlace === 'blocked') {
      const nombre = `${cambio.usuario.nombre} ${cambio.usuario.apellido}`.trim();
      this.notifications.notify(`Esperá a que termine el cambio anterior de ${nombre} (o a que venza el plazo para deshacerlo).`, 'cancelled');
    }
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
    const cambio = this.cambioDeRol();
    this.cambioDeRol.set(null);
    if (cambio) this.restaurarDesplegable(cambio);
  }

  // El desplegable ya muestra el rol elegido, pero como la cuenta sigue con el suyo, [ngModel] no ve ningún cambio
  // y no lo corrige solo (por eso volver a dibujar la fila no alcanzaba: QA, HU-094). Se le vuelve a escribir el
  // rol real directamente, sin recrear la fila, así el foco puede volver a ese mismo desplegable.
  private restaurarDesplegable(cambio: CambioDeRol): void {
    const confirmado = this.habilitados().find((u) => u.idUsuario === cambio.usuario.idUsuario) ?? cambio.usuario;
    cambio.modelo?.control.setValue(confirmado.rolPortal);
  }

  // Solo para la propia cuenta (las demás no pasan por la confirmación). Sin "Deshacer": el aviso es el toast de siempre.
  async confirmarCambioDeRol(): Promise<void> {
    const cambio = this.cambioDeRol();
    if (!cambio || this.rolSubmitting()) return;

    this.rolSubmitting.set(true);
    try {
      await this.aplicarCambioDeRol(cambio, false);
    } finally {
      this.rolSubmitting.set(false);
      this.cambioDeRol.set(null);
    }
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

// Pone en cada fila el rol confirmado por el servidor si es más nuevo (mayor revisión) que el que trae la fila.
function aplicarEstados(filas: PortalUserModel[], estados: ReadonlyMap<number, RoleState>): PortalUserModel[] {
  if (estados.size === 0) return filas;
  let cambio = false;
  const nuevas = filas.map((fila) => {
    const estado = estados.get(fila.idUsuario);
    if (!estado || estado.revision <= fila.revision) return fila;
    cambio = true;
    return { ...fila, rolPortal: estado.rolPortal, revision: estado.revision };
  });
  return cambio ? nuevas : filas;
}
