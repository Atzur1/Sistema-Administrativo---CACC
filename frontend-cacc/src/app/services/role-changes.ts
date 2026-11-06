import { Injectable, OnDestroy, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable, Subscription, TimeoutError, timeout } from 'rxjs';
import { AuthService } from './auth';
import { UsuariosPortalService } from './usuarios-portal';
import { NotificationService } from '../shared/notifications/notification.service';
import { UndoNoticesService } from '../shared/undo-notices/undo-notices';
import { ChangeRoleResultModel, PortalUserModel, ROL_PORTAL_LABELS, RolPortal } from '../models/PortalUserModel';

// HU-105: si el servidor no responde en este plazo, no se sabe si el cambio se aplicó: se relee la lista.
export const ROLE_CHANGE_TIMEOUT_MS = 15_000;

// 'saving' el cambio va al servidor; 'undoable' se aplicó y se puede deshacer; 'undoing' se está deshaciendo.
export type RoleChangePhase = 'saving' | 'undoable' | 'undoing';

// 'applied' quedó con el rol pedido; 'rejected' no (el rol sigue como estaba); 'unknown' no se pudo saber;
// 'blocked' la cuenta ya tenía una operación en curso y no se mandó nada; 'discarded' se cerró la sesión antes.
export type RoleChangeOutcome = 'applied' | 'rejected' | 'unknown' | 'blocked' | 'discarded';

// Rol y revisión de una cuenta tal como los confirmó el servidor.
export interface RoleState {
  idUsuario: number;
  rolPortal: RolPortal;
  revision: number;
}

interface Contexto {
  generation: number;
  token: string | null;
}

// Coordinador de los cambios de rol de Usuarios y Permisos. Vive fuera de la pantalla para que el "Deshacer" siga
// funcionando aunque se navegue a otra del portal; la pantalla lee de acá el estado confirmado de cada cuenta. Una
// cuenta acepta una sola operación a la vez: mientras se guarda, se deshace o todavía se puede deshacer, otro cambio
// de esa misma cuenta no se manda.
@Injectable({ providedIn: 'root' })
export class RoleChangesService implements OnDestroy {
  private readonly usuarios = inject(UsuariosPortalService);
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);
  private readonly avisos = inject(UndoNoticesService);

  private readonly fases = signal<ReadonlyMap<number, RoleChangePhase>>(new Map());
  private readonly estados = signal<ReadonlyMap<number, RoleState>>(new Map());
  private readonly lista = signal<PortalUserModel[] | null>(null);
  // Último estado confirmado de cada cuenta tocada en esta sesión (por un cambio o por deshacerlo).
  readonly states = this.estados.asReadonly();
  // La última lectura de cuentas habilitadas hecha para reconciliar (tras una demora o un conflicto).
  readonly snapshot = this.lista.asReadonly();

  // Pedidos en camino, con lo que hay que hacer si se descartan al cerrar sesión.
  private readonly pendientes = new Map<Subscription, () => void>();
  private generation = 0;

  phase(idUsuario: number): RoleChangePhase | null {
    return this.fases().get(idUsuario) ?? null;
  }

  isBusy(idUsuario: number): boolean {
    return this.fases().has(idUsuario);
  }

  // undoable: false para la propia cuenta (ya se confirmó en una ventana y, al cambiarse el rol, su sesión se corta).
  change(usuario: PortalUserModel, rol: RolPortal, opciones: { undoable: boolean }): Promise<RoleChangeOutcome> {
    const id = usuario.idUsuario;
    if (this.isBusy(id)) return Promise.resolve('blocked');
    const contexto = this.contexto();
    this.fijarFase(id, 'saving');

    return new Promise((resolver) => {
      this.pedir(
        this.usuarios.cambiarRol(id, { rolPortal: rol, revisionEsperada: usuario.revision }),
        contexto,
        () => resolver('discarded'),
        (resultado) => {
          this.registrar(resultado);
          if (opciones.undoable) {
            this.fijarFase(id, 'undoable');
            this.ofrecerDeshacer(usuario, resultado, contexto);
          } else {
            this.liberar(id);
            this.notifications.notify(resultado.mensaje, 'success');
          }
          resolver('applied');
        },
        (error) => void this.alFallarCambio(usuario, rol, error, contexto).then(resolver),
      );
    });
  }

  // Al salir del portal (cierre de sesión o sesión vencida): se borran los avisos y se descarta lo que esté en camino.
  clear(): void {
    this.generation++;
    for (const [suscripcion, descartar] of this.pendientes) {
      suscripcion.unsubscribe();
      descartar();
    }
    this.pendientes.clear();
    this.fases.set(new Map());
    this.estados.set(new Map());
    this.lista.set(null);
    this.avisos.clear();
  }

  ngOnDestroy(): void {
    this.clear();
  }

  private ofrecerDeshacer(usuario: PortalUserModel, cambio: ChangeRoleResultModel, contexto: Contexto): void {
    const nombre = nombreDe(usuario);
    const aviso: number = this.avisos.offer({
      message: `${nombre} ahora es ${ROL_PORTAL_LABELS[cambio.rolPortal]}.`,
      undoLabel: `Deshacer el cambio de rol de ${nombre}`,
      onUndo: () => this.deshacer(usuario, cambio, contexto, aviso),
      onExpire: () => this.liberar(cambio.idUsuario),
    });
  }

  // Deshacer es otra escritura real: el rol anterior, condicionado a la revisión que devolvió el cambio. Si la cuenta
  // cambió en el medio (otro SuperAdmin, otra pestaña), el servidor lo rechaza en vez de pisarlo.
  private deshacer(usuario: PortalUserModel, cambio: ChangeRoleResultModel, contexto: Contexto, aviso: number): void {
    const id = cambio.idUsuario;
    const terminar = () => {
      this.avisos.dismiss(aviso);
      this.liberar(id);
    };
    if (!this.vigente(contexto)) {
      terminar();
      return;
    }
    this.fijarFase(id, 'undoing');
    const nombre = nombreDe(usuario);

    this.pedir(
      this.usuarios.cambiarRol(id, { rolPortal: cambio.rolAnterior, revisionEsperada: cambio.revision }),
      contexto,
      () => undefined,
      (deshecho) => {
        this.registrar(deshecho);
        terminar();
        this.notifications.notify(`Se deshizo el cambio: ${nombre} volvió a ser ${ROL_PORTAL_LABELS[deshecho.rolPortal]}.`, 'success');
      },
      (error) => {
        if (esIncierto(error)) {
          void this.reconciliar(contexto).then((lista) => {
            if (lista === 'discarded') return;
            terminar();
            const actual = lista?.find((u) => u.idUsuario === id);
            if (!lista) {
              this.notifications.notify(`No se pudo confirmar si se deshizo el cambio de rol de ${nombre}. Actualizá la pantalla.`, 'error');
            } else if (actual && actual.rolPortal === cambio.rolAnterior && actual.revision > cambio.revision) {
              this.notifications.notify(`Se deshizo el cambio: ${nombre} volvió a ser ${ROL_PORTAL_LABELS[actual.rolPortal]}.`, 'success');
            } else {
              this.notifications.notify(`No se pudo deshacer: ${nombre} sigue como ${actual ? ROL_PORTAL_LABELS[actual.rolPortal] : 'cuenta sin acceso'}.`, 'error');
            }
          });
          return;
        }
        const avisar = () => {
          terminar();
          if (!esSinSesion(error)) this.notifications.notify(`No se pudo deshacer: ${mensajeDe(error)}`, 'error');
        };
        if (requiereRelectura(error)) void this.reconciliar(contexto).then((lista) => lista !== 'discarded' && avisar());
        else avisar();
      },
    );
  }

  private async alFallarCambio(usuario: PortalUserModel, rol: RolPortal, error: unknown, contexto: Contexto): Promise<RoleChangeOutcome> {
    const id = usuario.idUsuario;
    const nombre = nombreDe(usuario);

    if (esIncierto(error)) {
      const lista = await this.reconciliar(contexto);
      if (lista === 'discarded') return 'discarded';
      this.liberar(id);
      if (!lista) {
        this.notifications.notify(`No se pudo confirmar si el rol de ${nombre} cambió. Actualizá la pantalla antes de volver a intentarlo.`, 'error');
        return 'unknown';
      }
      const actual = lista.find((u) => u.idUsuario === id);
      if (!actual) {
        this.notifications.notify(`No se pudo confirmar el cambio: la cuenta de ${nombre} ya no está habilitada.`, 'error');
        return 'rejected';
      }
      // Ya tiene el rol pedido y una revisión posterior: se aplicó (sin "Deshacer": no se puede asegurar que el
      // cambio que se ve sea este y no uno de otra persona).
      if (actual.rolPortal === rol && actual.revision > usuario.revision) {
        this.notifications.notify(`La respuesta tardó, pero se comprobó que ${nombre} ya es ${ROL_PORTAL_LABELS[rol]}.`, 'success');
        return 'applied';
      }
      this.notifications.notify(`No se pudo confirmar el cambio: ${nombre} sigue como ${ROL_PORTAL_LABELS[actual.rolPortal]}.`, 'error');
      return 'rejected';
    }

    if (requiereRelectura(error) && (await this.reconciliar(contexto)) === 'discarded') return 'discarded';
    this.liberar(id);
    if (!esSinSesion(error)) this.notifications.notify(mensajeDe(error), 'error');
    return 'rejected';
  }

  // Lectura autorizada de las cuentas habilitadas (la misma consulta de la pantalla) para saber cómo quedó todo.
  private reconciliar(contexto: Contexto): Promise<PortalUserModel[] | null | 'discarded'> {
    return new Promise((resolver) => {
      this.pedir(
        this.usuarios.getHabilitados(),
        contexto,
        () => resolver('discarded'),
        (lista) => {
          this.lista.set(lista);
          resolver(lista);
        },
        () => resolver(null),
      );
    });
  }

  // Suscribe con plazo y descarta la respuesta si mientras tanto se cerró la sesión o cambió de cuenta.
  private pedir<T>(
    fuente: Observable<T>,
    contexto: Contexto,
    alDescartar: () => void,
    alResponder: (valor: T) => void,
    alFallar: (error: unknown) => void,
  ): void {
    let suscripcion: Subscription | undefined;
    let terminado = false;
    const terminar = () => {
      terminado = true;
      if (suscripcion) this.pendientes.delete(suscripcion);
    };
    suscripcion = fuente.pipe(timeout({ first: ROLE_CHANGE_TIMEOUT_MS })).subscribe({
      next: (valor) => {
        terminar();
        if (this.vigente(contexto)) alResponder(valor);
        else alDescartar();
      },
      error: (error: unknown) => {
        terminar();
        if (this.vigente(contexto)) alFallar(error);
        else alDescartar();
      },
    });
    if (!terminado) this.pendientes.set(suscripcion, alDescartar);
  }

  private contexto(): Contexto {
    return { generation: this.generation, token: this.auth.getToken() };
  }

  private vigente(contexto: Contexto): boolean {
    return contexto.generation === this.generation && this.auth.getToken() === contexto.token;
  }

  // Las revisiones nunca bajan: un estado más viejo que el que ya se conoce no lo reemplaza.
  private registrar(resultado: ChangeRoleResultModel): void {
    const conocido = this.estados().get(resultado.idUsuario);
    if (conocido && conocido.revision >= resultado.revision) return;
    const estados = new Map(this.estados());
    estados.set(resultado.idUsuario, { idUsuario: resultado.idUsuario, rolPortal: resultado.rolPortal, revision: resultado.revision });
    this.estados.set(estados);
  }

  private fijarFase(idUsuario: number, fase: RoleChangePhase): void {
    const fases = new Map(this.fases());
    fases.set(idUsuario, fase);
    this.fases.set(fases);
  }

  private liberar(idUsuario: number): void {
    if (!this.fases().has(idUsuario)) return;
    const fases = new Map(this.fases());
    fases.delete(idUsuario);
    this.fases.set(fases);
  }
}

function nombreDe(usuario: PortalUserModel): string {
  return `${usuario.nombre ?? ''} ${usuario.apellido ?? ''}`.trim() || usuario.email;
}

// Sin respuesta, o con un error del servidor o de la red: el cambio pudo haberse aplicado o no.
function esIncierto(error: unknown): boolean {
  return error instanceof TimeoutError || (error instanceof HttpErrorResponse && (error.status === 0 || error.status >= 500));
}

// La cuenta cambió o ya no está habilitada: lo que muestra la pantalla está viejo.
function requiereRelectura(error: unknown): boolean {
  return error instanceof HttpErrorResponse
    && (error.status === 404 || (error.status === 409 && error.error?.codigo === 'revision-desactualizada'));
}

// Sesión vencida: el interceptor ya cierra la sesión y lleva al inicio.
function esSinSesion(error: unknown): boolean {
  return error instanceof HttpErrorResponse && error.status === 401;
}

function mensajeDe(error: unknown): string {
  const mensaje = error instanceof HttpErrorResponse ? error.error?.mensaje : (error as { error?: { mensaje?: string } } | null)?.error?.mensaje;
  return typeof mensaje === 'string' && mensaje ? mensaje : 'No se pudo cambiar el rol.';
}
