import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { NEVER, Observable, Subject, of, throwError } from 'rxjs';

import { ROLE_CHANGE_TIMEOUT_MS, RoleChangesService } from './role-changes';
import { UsuariosPortalService } from './usuarios-portal';
import { AuthService } from './auth';
import { NotificationService } from '../shared/notifications/notification.service';
import { UNDO_WINDOW_MS, UndoNoticesService } from '../shared/undo-notices/undo-notices';
import { ChangeRoleRequestModel, ChangeRoleResultModel, PortalUserModel } from '../models/PortalUserModel';

// HU-105: coordinador de los cambios de rol de otras cuentas. El cambio se aplica en el acto, se puede deshacer
// durante 5000 ms con una escritura compensatoria condicionada a la revisión, y cada cuenta acepta una sola operación
// a la vez.

const ANA: PortalUserModel = {
  idUsuario: 7, nombre: 'Ana', apellido: 'Pérez', dni: '30111222', email: 'ana@cacc.test',
  rolPortal: 2, activo: true, activacionPendiente: false, revision: 4,
};
const LUIS: PortalUserModel = { ...ANA, idUsuario: 8, nombre: 'Luis', apellido: 'Gómez', email: 'luis@cacc.test', revision: 10 };

const resultado = (u: PortalUserModel, rolPortal: 1 | 2, revision: number): ChangeRoleResultModel =>
  ({ idUsuario: u.idUsuario, rolAnterior: u.rolPortal, rolPortal, revision, mensaje: 'Rol actualizado.' });
const conflicto = (codigo: string, mensaje: string) =>
  throwError(() => new HttpErrorResponse({ status: 409, error: { codigo, mensaje } }));

interface Opciones {
  cambiarRol?: (id: number, req: ChangeRoleRequestModel) => Observable<ChangeRoleResultModel>;
  habilitados?: () => Observable<PortalUserModel[]>;
}

function crear(opciones: Opciones = {}) {
  let token: string | null = 't1';
  const notify = vi.fn();
  // Por defecto, el servidor acepta: el rol pedido con la revisión siguiente.
  const cambiarRol = vi.fn(opciones.cambiarRol ?? ((id: number, req: ChangeRoleRequestModel) => of<ChangeRoleResultModel>({
    idUsuario: id, rolAnterior: req.rolPortal === 1 ? 2 : 1, rolPortal: req.rolPortal, revision: req.revisionEsperada + 1, mensaje: 'Rol actualizado.',
  })));
  const getHabilitados = vi.fn(opciones.habilitados ?? (() => of([ANA, LUIS])));
  TestBed.configureTestingModule({
    providers: [
      { provide: UsuariosPortalService, useValue: { cambiarRol, getHabilitados } },
      { provide: AuthService, useValue: { getToken: () => token } },
      { provide: NotificationService, useValue: { notify } },
    ],
  });
  return {
    cambios: TestBed.inject(RoleChangesService),
    avisos: TestBed.inject(UndoNoticesService),
    cambiarRol,
    getHabilitados,
    notify,
    cambiarSesion: (nuevo: string | null) => { token = nuevo; },
  };
}

describe('RoleChangesService - cambiar y deshacer', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => {
    TestBed.inject(RoleChangesService).clear();
    vi.useRealTimers();
  });

  it('manda el rol nuevo con la revisión que vio la pantalla y guarda el estado canónico que devuelve el servidor', async () => {
    const { cambios, cambiarRol } = crear();

    const desenlace = await cambios.change(ANA, 1, { undoable: true });

    expect(desenlace).toBe('applied');
    expect(cambiarRol).toHaveBeenCalledWith(7, { rolPortal: 1, revisionEsperada: 4 });
    expect(cambios.states().get(7)).toEqual({ idUsuario: 7, rolPortal: 1, revision: 5 });
  });

  it('recién con la respuesta ofrece "Deshacer" durante 5000 ms, y mientras tanto la cuenta queda ocupada', async () => {
    const respuesta = new Subject<ChangeRoleResultModel>();
    const { cambios, avisos } = crear({ cambiarRol: () => respuesta });

    const desenlace = cambios.change(ANA, 1, { undoable: true });
    expect(cambios.phase(7)).toBe('saving');
    expect(avisos.notices()).toEqual([]);

    vi.advanceTimersByTime(3000); // lo que tarde el servidor no se descuenta del plazo para deshacer
    respuesta.next(resultado(ANA, 1, 5));
    respuesta.complete();
    await desenlace;

    expect(cambios.phase(7)).toBe('undoable');
    expect(avisos.notices()).toEqual([expect.objectContaining({ message: 'Ana Pérez ahora es SuperAdmin.', state: 'available' })]);
    expect(avisos.notices()[0].undoLabel).toBe('Deshacer el cambio de rol de Ana Pérez');

    vi.advanceTimersByTime(UNDO_WINDOW_MS - 1);
    expect(cambios.isBusy(7)).toBe(true);
    vi.advanceTimersByTime(1);
    expect(cambios.isBusy(7)).toBe(false);
    expect(avisos.notices()).toEqual([]);
  });

  it('el aviso no pasa por el toast ni por el historial de la campanita', async () => {
    const { cambios, notify } = crear();

    await cambios.change(ANA, 1, { undoable: true });

    expect(notify).not.toHaveBeenCalled();
  });

  it('deshacer es otra escritura con el rol anterior y la revisión devuelta; al terminar avisa y libera la cuenta', async () => {
    const { cambios, avisos, cambiarRol, notify } = crear();
    await cambios.change(ANA, 1, { undoable: true });

    avisos.undo(avisos.notices()[0].id);
    await vi.runAllTimersAsync();

    expect(cambiarRol).toHaveBeenLastCalledWith(7, { rolPortal: 2, revisionEsperada: 5 });
    expect(cambios.states().get(7)).toEqual({ idUsuario: 7, rolPortal: 2, revision: 6 });
    expect(avisos.notices()).toEqual([]);
    expect(cambios.isBusy(7)).toBe(false);
    expect(notify).toHaveBeenCalledWith('Se deshizo el cambio: Ana Pérez volvió a ser Administrador.', 'success');
  });

  it('mientras se guarda, se deshace o se puede deshacer, otro cambio de la misma cuenta no sale; otra cuenta sí', async () => {
    const respuesta = new Subject<ChangeRoleResultModel>();
    const { cambios, cambiarRol, avisos } = crear({
      cambiarRol: (id, req) => (id === 7 ? respuesta : of(resultado(LUIS, req.rolPortal, 11))),
    });

    const primero = cambios.change(ANA, 1, { undoable: true });
    expect(await cambios.change(ANA, 1, { undoable: true })).toBe('blocked');
    respuesta.next(resultado(ANA, 1, 5));
    respuesta.complete();
    await primero;
    expect(await cambios.change({ ...ANA, rolPortal: 1, revision: 5 }, 2, { undoable: true })).toBe('blocked');

    expect(await cambios.change(LUIS, 1, { undoable: true })).toBe('applied');
    expect(cambiarRol).toHaveBeenCalledTimes(2);
    expect(avisos.notices()).toHaveLength(2);
  });

  it('sin deshacer (la propia cuenta) avisa con el toast de siempre y no deja la cuenta ocupada', async () => {
    const { cambios, avisos, notify } = crear();

    await cambios.change(ANA, 1, { undoable: false });

    expect(avisos.notices()).toEqual([]);
    expect(cambios.isBusy(7)).toBe(false);
    expect(notify).toHaveBeenCalledWith('Rol actualizado.', 'success');
  });
});

describe('RoleChangesService - rechazos, demoras y sesión', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => {
    TestBed.inject(RoleChangesService).clear();
    vi.useRealTimers();
  });

  it('si queda un solo SuperAdmin, muestra el motivo del servidor y no cambia nada', async () => {
    const { cambios, notify, getHabilitados } = crear({
      cambiarRol: () => conflicto('ultimo-superadmin', 'No se puede: tiene que quedar al menos otro SuperAdmin que pueda ingresar.'),
    });

    expect(await cambios.change({ ...ANA, rolPortal: 1 }, 2, { undoable: true })).toBe('rejected');

    expect(notify).toHaveBeenCalledWith('No se puede: tiene que quedar al menos otro SuperAdmin que pueda ingresar.', 'error');
    expect(getHabilitados).not.toHaveBeenCalled();
    expect(cambios.states().has(7)).toBe(false);
    expect(cambios.isBusy(7)).toBe(false);
  });

  it('si la cuenta cambió mientras tanto (revisión vieja), relee la lista y no ofrece deshacer', async () => {
    const actual = { ...ANA, rolPortal: 1 as const, revision: 6 };
    const { cambios, avisos, notify, getHabilitados } = crear({
      cambiarRol: () => conflicto('revision-desactualizada', 'La cuenta cambió desde que se cargó la pantalla: revisá su rol actual.'),
      habilitados: () => of([actual, LUIS]),
    });

    expect(await cambios.change(ANA, 1, { undoable: true })).toBe('rejected');

    expect(getHabilitados).toHaveBeenCalledTimes(1);
    expect(cambios.snapshot()).toEqual([actual, LUIS]);
    expect(notify).toHaveBeenCalledWith('La cuenta cambió desde que se cargó la pantalla: revisá su rol actual.', 'error');
    expect(avisos.notices()).toEqual([]);
  });

  it('si no llega respuesta a tiempo, relee la lista: si el rol sí cambió lo dice, sin ofrecer deshacer', async () => {
    const { cambios, avisos, notify, getHabilitados } = crear({
      cambiarRol: () => NEVER,
      habilitados: () => of([{ ...ANA, rolPortal: 1, revision: 5 }, LUIS]),
    });

    const desenlace = cambios.change(ANA, 1, { undoable: true });
    await vi.advanceTimersByTimeAsync(ROLE_CHANGE_TIMEOUT_MS - 1);
    expect(getHabilitados).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(1);

    expect(await desenlace).toBe('applied');
    expect(getHabilitados).toHaveBeenCalledTimes(1);
    expect(notify).toHaveBeenCalledWith('La respuesta tardó, pero se comprobó que Ana Pérez ya es SuperAdmin.', 'success');
    expect(avisos.notices()).toEqual([]);
    expect(cambios.isBusy(7)).toBe(false);
  });

  it('si no llega respuesta a tiempo y el rol no cambió, lo dice y queda el rol real', async () => {
    const { cambios, notify } = crear({ cambiarRol: () => NEVER, habilitados: () => of([ANA, LUIS]) });

    const desenlace = cambios.change(ANA, 1, { undoable: true });
    await vi.advanceTimersByTimeAsync(ROLE_CHANGE_TIMEOUT_MS);

    expect(await desenlace).toBe('rejected');
    expect(notify).toHaveBeenCalledWith('No se pudo confirmar el cambio: Ana Pérez sigue como Administrador.', 'error');
  });

  it('si tampoco se puede releer, no afirma nada sobre el rol', async () => {
    const { cambios, notify } = crear({
      cambiarRol: () => throwError(() => new HttpErrorResponse({ status: 0 })),
      habilitados: () => throwError(() => new HttpErrorResponse({ status: 0 })),
    });

    expect(await cambios.change(ANA, 1, { undoable: true })).toBe('unknown');
    expect(notify).toHaveBeenCalledWith('No se pudo confirmar si el rol de Ana Pérez cambió. Actualizá la pantalla antes de volver a intentarlo.', 'error');
    expect(cambios.isBusy(7)).toBe(false);
  });

  it('si deshacer choca con un cambio posterior, no lo pisa: relee y avisa', async () => {
    let llamadas = 0;
    const { cambios, avisos, notify, getHabilitados } = crear({
      cambiarRol: () => (++llamadas === 1
        ? of(resultado(ANA, 1, 5))
        : conflicto('revision-desactualizada', 'La cuenta cambió desde que se cargó la pantalla: revisá su rol actual.')),
    });
    await cambios.change(ANA, 1, { undoable: true });

    avisos.undo(avisos.notices()[0].id);
    await vi.runAllTimersAsync();

    expect(getHabilitados).toHaveBeenCalledTimes(1);
    expect(notify).toHaveBeenCalledWith('No se pudo deshacer: La cuenta cambió desde que se cargó la pantalla: revisá su rol actual.', 'error');
    expect(avisos.notices()).toEqual([]);
    expect(cambios.isBusy(7)).toBe(false);
  });

  it('al cerrar sesión se borran los avisos y una respuesta que llega después se descarta', async () => {
    const respuesta = new Subject<ChangeRoleResultModel>();
    const { cambios, avisos, notify } = crear({ cambiarRol: (id, req) => (id === 7 ? respuesta : of(resultado(LUIS, req.rolPortal, 11))) });
    await cambios.change(LUIS, 1, { undoable: true });
    const enCurso = cambios.change(ANA, 1, { undoable: true });
    expect(avisos.notices()).toHaveLength(1);

    cambios.clear();
    respuesta.next(resultado(ANA, 1, 5));
    respuesta.complete();

    expect(await enCurso).toBe('discarded');
    expect(avisos.notices()).toEqual([]);
    expect(cambios.states().size).toBe(0);
    expect(cambios.isBusy(7)).toBe(false);
    expect(notify).not.toHaveBeenCalled();
  });

  it('si la sesión cambió, "Deshacer" no manda nada con la sesión nueva', async () => {
    const { cambios, avisos, cambiarRol, cambiarSesion } = crear();
    await cambios.change(ANA, 1, { undoable: true });

    cambiarSesion('t2');
    avisos.undo(avisos.notices()[0].id);
    await vi.runAllTimersAsync();

    expect(cambiarRol).toHaveBeenCalledTimes(1);
    expect(avisos.notices()).toEqual([]);
  });
});
