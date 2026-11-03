import { ChangeDetectorRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Observable, Subject, of, throwError } from 'rxjs';

import { Usuarios } from './usuarios-y-permisos';
import { UsuariosPortalService } from '../../services/usuarios-portal';
import { NotificationService } from '../../shared/notifications/notification.service';
import { AuthService } from '../../services/auth';
import { ChangeRoleRequestModel, ChangeRoleResultModel, PortalUserCandidateModel, PortalUserModel } from '../../models/PortalUserModel';
import { CuotasFaltantesService } from '../../services/cuotas-faltantes';
import { PendingActionsService } from '../../services/pending-actions';
import { RoleChangesService } from '../../services/role-changes';
import { UndoNoticesService } from '../../shared/undo-notices/undo-notices';

// Botón "Resetear contraseña" del SuperAdmin: le manda a una cuenta habilitada el enlace para elegir una contraseña
// nueva. El enlace llega al correo de la propia cuenta; el SuperAdmin no ve ni fija la contraseña.

const ADMIN: PortalUserModel = {
  idUsuario: 7, nombre: 'Camila', apellido: 'Candidata', dni: '30111222',
  email: 'camila@cacc.test', rolPortal: 2, activo: true, activacionPendiente: false, revision: 3,
};
const SIN_ACTIVAR: PortalUserModel = { ...ADMIN, idUsuario: 8, nombre: 'Nuevo', email: 'nuevo@cacc.test', activacionPendiente: true };
const INACTIVO: PortalUserModel = { ...ADMIN, idUsuario: 9, nombre: 'Baja', email: 'baja@cacc.test', activo: false };

const CANDIDATO: PortalUserCandidateModel = {
  idUsuario: 20, nombre: 'Carla', apellido: 'Candidata', dni: '30.555.666', email: 'carla@cacc.test', fkIdRolSugerido: 2,
};

// Opciones extra para armar la pantalla: la cuenta con la que se inició sesión, el doble de "cambiar rol" y la lista
// de habilitadas.
interface OpcionesCreate {
  yo?: { email: string; dni?: string } | null;
  cambiarRol?: ReturnType<typeof vi.fn>;
  getHabilitados?: () => Observable<PortalUserModel[]>;
}

// Por defecto el servidor acepta el cambio: el rol pedido con la revisión siguiente (HU-105).
const aceptarCambio = (id: number, req: ChangeRoleRequestModel) => of<ChangeRoleResultModel>({
  idUsuario: id, rolAnterior: req.rolPortal === 1 ? 2 : 1, rolPortal: req.rolPortal, revision: req.revisionEsperada + 1, mensaje: 'Rol actualizado.',
});

async function create(
  resetear = vi.fn(() => of({ mensaje: 'Se envió el enlace.' })),
  editarCandidato = vi.fn(() => of({ mensaje: 'Datos corregidos.' })),
  opciones: OpcionesCreate = {},
) {
  const notify = vi.fn();
  const cambiarRol = opciones.cambiarRol ?? vi.fn(aceptarCambio);
  await TestBed.configureTestingModule({
    imports: [Usuarios],
    providers: [
      provideRouter([]),
      // HU-104: los candidatos llegan del coordinador de pendientes (que usa estas mismas consultas).
      { provide: CuotasFaltantesService, useValue: { getMeses: () => of([]) } },
      {
        provide: UsuariosPortalService,
        useValue: {
          getCandidatos: () => of([{ ...CANDIDATO }]),
          getHabilitados: opciones.getHabilitados ?? (() => of([ADMIN, SIN_ACTIVAR, INACTIVO])),
          getDeshabilitados: () => of([]),
          resetearContrasena: resetear,
          editarCandidato,
          cambiarRol,
        },
      },
      { provide: NotificationService, useValue: { notify } },
      { provide: AuthService, useValue: { getUsuario: () => opciones.yo ?? null, isSuperAdmin: () => true, getToken: () => 't' } },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(Usuarios);
  fixture.detectChanges();
  return { fixture, component: fixture.componentInstance, element: fixture.nativeElement as HTMLElement, resetear, editarCandidato, notify, cambiarRol };
}

describe('Usuarios - Resetear contraseña', () => {
  it('el botón dice "Resetear contraseña", o "Reenviar activación" si la cuenta nunca creó la suya', async () => {
    const { component } = await create();

    expect(component.textoAccionEnlace(ADMIN)).toBe('Resetear contraseña');
    expect(component.textoAccionEnlace(SIN_ACTIVAR)).toBe('Reenviar activación');
  });

  // Las acciones de cada fila están en un menú "⋯": se abre la fila y se leen sus opciones.
  const opcionesDelMenu = (element: HTMLElement, fixture: { detectChanges(): void }, fila: number) => {
    element.querySelectorAll<HTMLElement>('.panel-users .kebab')[fila].click();
    fixture.detectChanges();
    const titulos = Array.from(element.querySelectorAll('.menu .menu-title')).map((t) => t.textContent?.trim());
    document.body.click(); // cierra el menú
    fixture.detectChanges();
    return titulos;
  };

  it('aparece en las cuentas activas y no en la que está inactiva en la base del otro equipo', async () => {
    const { element, fixture } = await create();

    expect(opcionesDelMenu(element, fixture, 0)).toEqual(['Resetear contraseña', 'Deshabilitar']); // ADMIN
    expect(opcionesDelMenu(element, fixture, 1)).toEqual(['Reenviar activación', 'Deshabilitar']); // SIN_ACTIVAR
    expect(opcionesDelMenu(element, fixture, 2)).toEqual(['Deshabilitar']); // INACTIVO: no recibe correos
  });

  it('pide confirmación primero: hacer clic no manda ningún correo', async () => {
    const { component, resetear } = await create();

    component.pedirEnlace(ADMIN);

    expect(component.confirmarEnlace()).toBe(ADMIN);
    expect(resetear).not.toHaveBeenCalled();
  });

  it('la confirmación muestra a qué correo va el enlace', async () => {
    const { fixture, element, component } = await create();

    component.pedirEnlace(ADMIN);
    fixture.detectChanges();

    expect(element.querySelector('[role="dialog"]')?.textContent).toContain('camila@cacc.test');
  });

  it('confirmar manda el correo, avisa con el mensaje del servidor y cierra la confirmación', async () => {
    const { component, resetear, notify } = await create();

    component.pedirEnlace(ADMIN);
    component.confirmarEnlaceAccion();

    expect(resetear).toHaveBeenCalledWith(7);
    expect(notify).toHaveBeenCalledWith('Se envió el enlace.', 'success');
    expect(component.confirmarEnlace()).toBeNull();
  });

  it('si el servidor lo rechaza, muestra su motivo', async () => {
    const resetear = vi.fn(() => throwError(() => ({ error: { mensaje: 'No se pudo enviar el correo.' } })));
    const { component, notify } = await create(resetear);

    component.pedirEnlace(ADMIN);
    component.confirmarEnlaceAccion();

    expect(notify).toHaveBeenCalledWith('No se pudo enviar el correo.', 'error');
    expect(component.confirmarEnlace()).toBeNull();
  });

  it('cancelar cierra la confirmación sin mandar nada', async () => {
    const { component, resetear } = await create();

    component.pedirEnlace(ADMIN);
    component.cancelarEnlace();

    expect(component.confirmarEnlace()).toBeNull();
    expect(resetear).not.toHaveBeenCalled();
  });
});

// HU-094, corrección de QA: las ventanas que se abren desde el menú "⋯" le devuelven el foco a ese botón al
// cerrarse (la opción del menú ya no existe, porque el menú se cerró al elegirla).
describe('Usuarios - foco al cerrar una ventana abierta desde el menú', () => {
  const abrirDesdeMenu = (element: HTMLElement, fixture: { detectChanges(): void }, opcion: string) => {
    const kebab = element.querySelectorAll<HTMLButtonElement>('.panel-users .kebab')[0];
    kebab.click();
    fixture.detectChanges();
    const item = element.querySelector<HTMLButtonElement>(opcion)!;
    item.focus();
    item.click();
    fixture.detectChanges();
    return kebab;
  };
  const esperarFoco = () => new Promise((resolver) => setTimeout(resolver));

  it('Resetear contraseña: al volver, el foco regresa al botón "⋯" de la fila', async () => {
    const { fixture, element } = await create();
    const kebab = abrirDesdeMenu(element, fixture, '.menu-item:not(.menu-item-danger)');
    expect(element.querySelector('[role="dialog"]')).not.toBeNull();

    element.querySelector<HTMLButtonElement>('.ventana-volver')!.click();
    fixture.detectChanges();
    await esperarFoco();

    expect(element.querySelector('[role="dialog"]')).toBeNull();
    expect(document.activeElement).toBe(kebab);
  });

  it('Deshabilitar: al cerrar con Esc, el foco regresa al botón "⋯" de la fila', async () => {
    const { fixture, element } = await create();
    const kebab = abrirDesdeMenu(element, fixture, '.menu-item-danger');
    expect(element.querySelector('[role="dialog"]')).not.toBeNull();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    await esperarFoco();

    expect(element.querySelector('[role="dialog"]')).toBeNull();
    expect(document.activeElement).toBe(kebab);
  });
});

// El DNI es el usuario con el que la persona ingresa al portal: se puede corregir antes de habilitarla.
describe('Usuarios - Corregir el DNI de un candidato', () => {
  it('el formulario trae el DNI cargado y lo muestra para corregirlo', async () => {
    const { fixture, element, component } = await create();
    const candidato = component.candidatos()[0];

    component.abrirEditar(candidato);
    fixture.detectChanges();
    await fixture.whenStable(); // ngModel escribe el valor en el input de forma asíncrona

    expect((element.querySelector('#editar-dni') as HTMLInputElement).value).toBe('30.555.666');
  });

  it('manda el DNI junto con el resto de los datos y deja el guardado solo con dígitos', async () => {
    const { component, editarCandidato } = await create();
    const candidato = component.candidatos()[0];

    component.abrirEditar(candidato);
    component.editarDni = ' 30.555.777 ';
    component.guardarEdicion();

    expect(editarCandidato).toHaveBeenCalledWith(20, {
      nombre: 'Carla', apellido: 'Candidata', dni: '30.555.777',
    });
    expect(component.candidatos()[0].dni).toBe('30555777');
  });

  // El mail no se modifica desde el portal: si se pudiera, un SuperAdmin podría habilitar la cuenta de otra
  // persona con su propio correo. Se muestra para que se pueda verificar, pero no es editable ni viaja.
  it('muestra el mail como solo lectura y no lo manda al guardar', async () => {
    const { component, element, fixture, editarCandidato } = await create();

    component.abrirEditar(component.candidatos()[0]);
    fixture.detectChanges();
    await fixture.whenStable();

    const mail = element.querySelector('#editar-email') as HTMLInputElement;
    expect(mail.disabled).toBe(true);
    expect(mail.value).toBe('carla@cacc.test');

    component.guardarEdicion();
    expect(editarCandidato).toHaveBeenCalledWith(20, expect.not.objectContaining({ email: expect.anything() }));
  });

  it('no deja guardar sin DNI', async () => {
    const { component, editarCandidato } = await create();

    component.abrirEditar(component.candidatos()[0]);
    component.editarDni = '  ';
    component.guardarEdicion();

    expect(editarCandidato).not.toHaveBeenCalled();
    expect(component.editarError()).toContain('DNI');
  });

  it('si el servidor rechaza el DNI (por ejemplo, ya lo usa otra cuenta) muestra su motivo y no cambia nada', async () => {
    const editar = vi.fn(() => throwError(() => ({ error: { mensaje: 'Ese DNI ya lo usa otra cuenta con acceso al portal.' } })));
    const { component } = await create(undefined, editar);
    const candidato = component.candidatos()[0];

    component.abrirEditar(candidato);
    component.editarDni = '41522398';
    component.guardarEdicion();

    expect(component.editarError()).toBe('Ese DNI ya lo usa otra cuenta con acceso al portal.');
    expect(candidato.dni).toBe('30.555.666');
  });

  it('la lista de cuentas habilitadas muestra el DNI de cada una', async () => {
    const { element } = await create();

    expect(element.querySelector('.panel-users')?.textContent).toContain('DNI 30111222');
  });
});

// Las tres listas (habilitadas, candidatos, deshabilitadas) se ven de a una, con su contador en la pestaña.

// Como lo hace la persona: abre el desplegable de rol de Camila (Administrador) y elige otra opción.
const elegirRolDeCamila = (element: HTMLElement, fixture: { detectChanges(): void }, opcion: string) => {
  const fila = Array.from(element.querySelectorAll<HTMLElement>('.panel-users tbody tr')).find((tr) =>
    tr.textContent?.includes('camila@cacc.test'),
  )!;
  const desplegable = fila.querySelector<HTMLButtonElement>('.rol-select .cs-trigger')!;
  desplegable.focus();
  desplegable.click();
  fixture.detectChanges();
  Array.from(document.querySelectorAll<HTMLElement>('.cs-menu .cs-option'))
    .find((o) => o.textContent?.trim() === opcion)!
    .click();
  fixture.detectChanges();
  return desplegable;
};
const rolQueMuestra = (desplegable: HTMLElement) => desplegable.querySelector('.cs-value')!.textContent!.trim();
const esperar = () => new Promise((resolver) => setTimeout(resolver));
const filaDeCamila = (element: HTMLElement) =>
  Array.from(element.querySelectorAll<HTMLElement>('.panel-users tbody tr')).find((tr) => tr.textContent?.includes('camila@cacc.test'))!;
// Refresca la vista de un componente OnPush después de un cambio que llegó por una promesa.
const refrescar = (fixture: { componentRef: { injector: { get(t: typeof ChangeDetectorRef): ChangeDetectorRef } }; detectChanges(): void }) => {
  fixture.componentRef.injector.get(ChangeDetectorRef).markForCheck();
  fixture.detectChanges();
};

// HU-105: el rol de otra cuenta se cambia en el acto (sin ventana) y se puede deshacer durante 5000 ms.
describe('Usuarios - cambiar el rol de otra cuenta y deshacerlo (HU-105)', () => {
  afterEach(() => vi.useRealTimers());

  it('elegir otro rol lo aplica en el acto, sin ventana, con la revisión que trajo la lista', async () => {
    const { fixture, element, component, cambiarRol } = await create();

    const desplegable = elegirRolDeCamila(element, fixture, 'SuperAdmin');
    await esperar();
    refrescar(fixture);

    expect(element.querySelector('[role="dialog"]')).toBeNull();
    expect(component.cambioDeRol()).toBeNull();
    expect(cambiarRol).toHaveBeenCalledWith(7, { rolPortal: 1, revisionEsperada: 3 });
    expect(rolQueMuestra(desplegable)).toBe('SuperAdmin');
    expect(component.habilitados().find((u) => u.idUsuario === 7)).toEqual(expect.objectContaining({ rolPortal: 1, revision: 4 }));
    expect(ADMIN.rolPortal).toBe(2); // la lista original no se toca: la fila se reemplaza con el estado confirmado
    expect(TestBed.inject(UndoNoticesService).notices()).toEqual([
      expect.objectContaining({ message: 'Camila Candidata ahora es SuperAdmin.', undoLabel: 'Deshacer el cambio de rol de Camila Candidata' }),
    ]);
  });

  it('"Deshacer" manda el rol anterior con la revisión nueva y la fila vuelve a mostrarlo', async () => {
    const { fixture, element, cambiarRol, notify } = await create();
    const desplegable = elegirRolDeCamila(element, fixture, 'SuperAdmin');
    await esperar();
    const avisos = TestBed.inject(UndoNoticesService);

    avisos.undo(avisos.notices()[0].id);
    await esperar();
    refrescar(fixture);

    expect(cambiarRol).toHaveBeenLastCalledWith(7, { rolPortal: 2, revisionEsperada: 4 });
    expect(rolQueMuestra(desplegable)).toBe('Administrador');
    expect(avisos.notices()).toEqual([]);
    expect(notify).toHaveBeenCalledWith('Se deshizo el cambio: Camila Candidata volvió a ser Administrador.', 'success');
  });

  it('mientras se puede deshacer, otro cambio de la misma cuenta no sale; vencidos los 5000 ms, sí', async () => {
    const { fixture, element, cambiarRol, notify } = await create();
    vi.useFakeTimers();
    const desplegable = elegirRolDeCamila(element, fixture, 'SuperAdmin');
    await vi.advanceTimersByTimeAsync(0);
    refrescar(fixture);
    expect(filaDeCamila(element).querySelector('.rol-estado')!.textContent!.trim()).toBe('Se puede deshacer');

    elegirRolDeCamila(element, fixture, 'Administrador');
    await vi.advanceTimersByTimeAsync(0);
    refrescar(fixture);

    expect(cambiarRol).toHaveBeenCalledTimes(1);
    expect(rolQueMuestra(desplegable)).toBe('SuperAdmin');
    expect(notify).toHaveBeenCalledWith(
      'Esperá a que termine el cambio anterior de Camila Candidata (o a que venza el plazo para deshacerlo).', 'cancelled');

    await vi.advanceTimersByTimeAsync(5000);
    refrescar(fixture);
    expect(filaDeCamila(element).querySelector('.rol-estado')).toBeNull();

    elegirRolDeCamila(element, fixture, 'Administrador');
    await vi.advanceTimersByTimeAsync(0);
    expect(cambiarRol).toHaveBeenCalledTimes(2);
    expect(cambiarRol).toHaveBeenLastCalledWith(7, { rolPortal: 2, revisionEsperada: 4 });
  });

  it('mientras se guarda, la fila lo dice', async () => {
    const respuesta = new Subject<ChangeRoleResultModel>();
    const { fixture, element } = await create(undefined, undefined, { cambiarRol: vi.fn(() => respuesta) });

    elegirRolDeCamila(element, fixture, 'SuperAdmin');
    refrescar(fixture);

    expect(filaDeCamila(element).querySelector('.rol-estado')!.textContent!.trim()).toBe('Guardando…');
    expect(TestBed.inject(UndoNoticesService).notices()).toEqual([]); // el plazo para deshacer no empieza todavía
  });

  it('si el servidor lo rechaza, muestra su motivo y el desplegable vuelve al rol real', async () => {
    const rechazo = vi.fn(() => throwError(() => new HttpErrorResponse({
      status: 409, error: { codigo: 'ultimo-superadmin', mensaje: 'No se puede: tiene que quedar al menos otro SuperAdmin que pueda ingresar.' },
    })));
    const { fixture, element, notify } = await create(undefined, undefined, { cambiarRol: rechazo });

    const desplegable = elegirRolDeCamila(element, fixture, 'SuperAdmin');
    await esperar();
    refrescar(fixture);

    expect(rolQueMuestra(desplegable)).toBe('Administrador');
    expect(notify).toHaveBeenCalledWith('No se puede: tiene que quedar al menos otro SuperAdmin que pueda ingresar.', 'error');
    expect(TestBed.inject(UndoNoticesService).notices()).toEqual([]);
  });

  it('si la cuenta cambió en el medio, la fila pasa a mostrar lo que relee del servidor', async () => {
    let lecturas = 0;
    const getHabilitados = () => of(++lecturas === 1 ? [ADMIN, SIN_ACTIVAR, INACTIVO] : [{ ...ADMIN, rolPortal: 1 as const, revision: 9 }, SIN_ACTIVAR, INACTIVO]);
    const conflicto = vi.fn(() => throwError(() => new HttpErrorResponse({
      status: 409, error: { codigo: 'revision-desactualizada', mensaje: 'La cuenta cambió desde que se cargó la pantalla: revisá su rol actual.' },
    })));
    const { fixture, element, component } = await create(undefined, undefined, { cambiarRol: conflicto, getHabilitados });

    const desplegable = elegirRolDeCamila(element, fixture, 'SuperAdmin');
    await esperar();
    refrescar(fixture);

    expect(component.habilitados().find((u) => u.idUsuario === 7)).toEqual(expect.objectContaining({ rolPortal: 1, revision: 9 }));
    expect(rolQueMuestra(desplegable)).toBe('SuperAdmin');
  });

  it('el aviso sigue al salir de la pantalla, y deshacer desde ahí también funciona', async () => {
    const { fixture, element, cambiarRol } = await create();
    elegirRolDeCamila(element, fixture, 'SuperAdmin');
    await esperar();

    fixture.destroy();
    const avisos = TestBed.inject(UndoNoticesService);
    expect(avisos.notices()).toHaveLength(1);
    avisos.undo(avisos.notices()[0].id);
    await esperar();

    expect(cambiarRol).toHaveBeenLastCalledWith(7, { rolPortal: 2, revisionEsperada: 4 });
    expect(TestBed.inject(RoleChangesService).states().get(7)).toEqual({ idUsuario: 7, rolPortal: 2, revision: 5 });
  });

  it('al volver a la pantalla, la fila muestra el último estado confirmado aunque la lista llegue vieja', async () => {
    const { fixture, element } = await create();
    elegirRolDeCamila(element, fixture, 'SuperAdmin');
    await esperar();
    fixture.destroy();

    const otra = TestBed.createComponent(Usuarios);
    otra.detectChanges();

    expect(otra.componentInstance.habilitados().find((u) => u.idUsuario === 7)).toEqual(expect.objectContaining({ rolPortal: 1, revision: 4 }));
  });
});

// La propia cuenta: quitarse el acceso total no se puede deshacer desde la misma sesión, así que sigue pidiendo
// confirmación, con su advertencia.
describe('Usuarios - cambiar el rol de la propia cuenta con confirmación', () => {
  const YO = { email: 'camila@cacc.test', dni: '30111222' };

  it('elegir otro rol no lo aplica: abre la confirmación y no llama al servidor', async () => {
    const { component, cambiarRol } = await create(undefined, undefined, { yo: YO });

    component.onRolHabilitadoChange(ADMIN, 1);

    expect(component.cambioDeRol()).toEqual({ usuario: ADMIN, rol: 1 });
    expect(cambiarRol).not.toHaveBeenCalled();
  });

  it('elegir el mismo rol que ya tiene no pide nada', async () => {
    const { component, cambiarRol } = await create(undefined, undefined, { yo: YO });

    component.onRolHabilitadoChange(ADMIN, 2);

    expect(component.cambioDeRol()).toBeNull();
    expect(cambiarRol).not.toHaveBeenCalled();
  });

  it('confirmar aplica el cambio con la revisión, avisa con el toast y no ofrece deshacer', async () => {
    const { component, cambiarRol, notify } = await create(undefined, undefined, { yo: YO });

    component.onRolHabilitadoChange(ADMIN, 1);
    await component.confirmarCambioDeRol();

    expect(cambiarRol).toHaveBeenCalledWith(7, { rolPortal: 1, revisionEsperada: 3 });
    expect(notify).toHaveBeenCalledWith('Rol actualizado.', 'success');
    expect(component.cambioDeRol()).toBeNull();
    expect(TestBed.inject(UndoNoticesService).notices()).toEqual([]);
  });

  it('cancelar con el botón Cancelar no cambia nada: el desplegable vuelve a mostrar el rol real y recibe el foco', async () => {
    const { fixture, element, component, cambiarRol } = await create(undefined, undefined, { yo: YO });
    const desplegable = elegirRolDeCamila(element, fixture, 'SuperAdmin');
    expect(rolQueMuestra(desplegable)).toBe('SuperAdmin');
    expect(element.querySelector('[role="dialog"]')).not.toBeNull();

    element.querySelector<HTMLButtonElement>('.ventana-volver')!.click();
    fixture.detectChanges();
    await esperar();

    expect(cambiarRol).not.toHaveBeenCalled();
    expect(component.cambioDeRol()).toBeNull();
    expect(rolQueMuestra(desplegable)).toBe('Administrador');
    expect(document.activeElement).toBe(desplegable);
  });

  it('cerrar con Esc tampoco cambia nada: el desplegable vuelve al rol real y recibe el foco', async () => {
    const { fixture, element, cambiarRol } = await create(undefined, undefined, { yo: YO });
    const desplegable = elegirRolDeCamila(element, fixture, 'SuperAdmin');

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    await esperar();

    expect(cambiarRol).not.toHaveBeenCalled();
    expect(element.querySelector('[role="dialog"]')).toBeNull();
    expect(rolQueMuestra(desplegable)).toBe('Administrador');
    expect(document.activeElement).toBe(desplegable);
  });

  it('si el servidor lo rechaza, muestra su motivo y el desplegable vuelve al rol real', async () => {
    const rechazo = vi.fn(() => throwError(() => ({ error: { mensaje: 'Tiene que quedar al menos un SuperAdmin.' } })));
    const { fixture, element, notify } = await create(undefined, undefined, { yo: YO, cambiarRol: rechazo });
    const desplegable = elegirRolDeCamila(element, fixture, 'SuperAdmin');

    element.querySelector<HTMLButtonElement>('.ventana-confirmar')!.click();
    fixture.detectChanges();
    await esperar();
    refrescar(fixture);

    expect(rechazo).toHaveBeenCalled();
    expect(notify).toHaveBeenCalledWith('Tiene que quedar al menos un SuperAdmin.', 'error');
    expect(rolQueMuestra(desplegable)).toBe('Administrador');
  });

  it('la confirmación explica qué cambia: dar acceso total o quitarlo', async () => {
    const { component } = await create(undefined, undefined, { yo: YO });

    component.onRolHabilitadoChange(ADMIN, 1);
    expect(component.textoCambioDeRol!.titulo).toContain('acceso total');
    expect(component.textoCambioDeRol!.cuerpo).toContain('todo el portal');

    component.cancelarCambioDeRol();
    component.onRolHabilitadoChange({ ...ADMIN, rolPortal: 1 }, 2);
    expect(component.textoCambioDeRol!.cuerpo).toContain('solo va a ver Cobranza');
  });

  it('si se quita el acceso total, lo advierte', async () => {
    const { component } = await create(undefined, undefined, { yo: YO });

    component.onRolHabilitadoChange({ ...ADMIN, rolPortal: 1 }, 2);

    expect(component.textoCambioDeRol!.advertencia).toContain('tu propia cuenta');
  });
});

describe('Usuarios - cuenta propia, búsqueda y aviso de pendientes', () => {
  it('reconoce la cuenta propia por DNI o por correo y la marca con "Vos"', async () => {
    const { component, fixture, element } = await create(undefined, undefined, {
      yo: { email: 'otro@correo.test', dni: '30.111.222' }, // el DNI llega con puntos
    });

    expect(component.esMiCuenta(ADMIN)).toBe(true);
    expect(component.esMiCuenta(SIN_ACTIVAR)).toBe(true); // mismo DNI en los datos de prueba
    expect(component.esMiCuenta({ email: 'admin@cacc.test', dni: '1' })).toBe(false);
    fixture.detectChanges();
    expect(element.querySelector('.self-tag')!.textContent).toBe('Vos');
  });

  it('no se puede deshabilitar la propia cuenta desde el menú', async () => {
    const { element, fixture } = await create(undefined, undefined, { yo: { email: 'camila@cacc.test' } });

    element.querySelectorAll<HTMLElement>('.panel-users .kebab')[0].click();
    fixture.detectChanges();

    const deshabilitar = element.querySelector<HTMLButtonElement>('.menu-item-danger')!;
    expect(deshabilitar.disabled).toBe(true);
    expect(deshabilitar.textContent).toContain('tu propia cuenta');
  });

  it('el buscador filtra la lista por nombre, DNI o correo sin cambiar los contadores', async () => {
    const { component, fixture, element } = await create();

    component.alEscribirBusqueda('nuevo@');
    fixture.detectChanges();
    expect(component.habilitadosVisibles().map((u) => u.idUsuario)).toEqual([8]);
    expect(element.querySelectorAll('.panel-users tbody tr')).toHaveLength(1);

    component.alEscribirBusqueda('30.111.222'); // por DNI, con puntos
    expect(component.habilitadosVisibles()).toHaveLength(3);

    component.alEscribirBusqueda('zzzz');
    fixture.detectChanges();
    expect(element.querySelector('.panel-users .cell-muted')!.textContent).toContain('Ninguna cuenta habilitada coincide con “zzzz”');
    expect(element.querySelector('#tab-habilitadas .tab-count')!.textContent).toContain('3'); // el total, no el filtro

    component.limpiarBusqueda();
    expect(component.habilitadosVisibles()).toHaveLength(3);
  });

  it('avisa desde cualquier pestaña que hay personas esperando acceso, y lleva a revisarlas', async () => {
    const { component, fixture, element } = await create();

    expect(element.querySelector('.pending-banner')!.textContent).toContain('1');
    expect(element.querySelector('.pending-banner')!.textContent).toContain('espera');

    element.querySelector<HTMLElement>('.pending-action')!.click();
    fixture.detectChanges();

    expect(component.pestana()).toBe('candidatos');
    expect(element.querySelector('.pending-banner')).toBeNull(); // ya está en esa pestaña: no hace falta el aviso
  });

  it('el encabezado muestra cuántos esperan acceso en lugar de repetir el total de la pestaña', async () => {
    const { component, element } = await create();

    expect(component.totalPendientes).toBe(1);
    const etiquetas = Array.from(element.querySelectorAll('.header-metric-label')).map((e) => e.textContent?.trim());
    expect(etiquetas).toEqual(['SuperAdmin', 'Administrador', 'Esperan acceso']);
    expect(element.querySelector('.header-metric-alert')).not.toBeNull();
  });

  it('el menú de una fila se cierra con Esc o al tocar afuera', async () => {
    const { component, fixture, element } = await create();

    element.querySelectorAll<HTMLElement>('.panel-users .kebab')[0].click();
    fixture.detectChanges();
    expect(component.menuAbierto()).toBe(ADMIN.idUsuario);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    expect(component.menuAbierto()).toBeNull();
  });
});

// Hay cuentas cargadas solo con el correo (sin nombre ni apellido): se muestran igual, con el correo en su lugar.
describe('Usuarios - nombre y avatar de cada persona', () => {
  it('arma el nombre y las iniciales con nombre y apellido', async () => {
    const { component } = await create();

    expect(component.nombreMostrar(ADMIN)).toBe('Camila Candidata');
    expect(component.iniciales(ADMIN)).toBe('CC');
    expect(component.tieneNombre(ADMIN)).toBe(true);
  });

  it('si la cuenta no tiene nombre, muestra el correo y usa su inicial', async () => {
    const { component } = await create();
    const sinNombre = { nombre: '', apellido: '', email: 'admin@cacc.com' };

    expect(component.tieneNombre(sinNombre)).toBe(false);
    expect(component.nombreMostrar(sinNombre)).toBe('admin@cacc.com');
    expect(component.iniciales(sinNombre)).toBe('A');
  });

  it('un nombre nulo tampoco rompe la fila', async () => {
    const { component } = await create();
    const nulo = { nombre: null, apellido: null, email: 'x@cacc.test' };

    expect(component.nombreMostrar(nulo)).toBe('x@cacc.test');
    expect(component.iniciales(nulo)).toBe('X');
  });

  it('las tres listas muestran un avatar por persona', async () => {
    const { component, fixture, element } = await create();

    expect(element.querySelectorAll('.panel-users .user-avatar')).toHaveLength(3);
    component.cambiarPestana('candidatos');
    fixture.detectChanges();
    expect(element.querySelectorAll('.panel-candidatos .user-avatar')).toHaveLength(1);
  });

  it('cada pestaña lleva su ícono', async () => {
    const { element } = await create();

    expect(element.querySelectorAll('.tabs .tab .tab-icon')).toHaveLength(3);
  });
});

describe('Usuarios - Pestañas', () => {
  const tab = (element: HTMLElement, id: string) => element.querySelector(`#${id}`) as HTMLButtonElement;

  it('arranca en las cuentas habilitadas y solo muestra esa lista', async () => {
    const { element } = await create();

    expect(element.querySelector('.panel-users')).not.toBeNull();
    expect(element.querySelector('.panel-candidatos')).toBeNull();
    expect(element.querySelector('.panel-deshabilitados')).toBeNull();
    expect(tab(element, 'tab-habilitadas').getAttribute('aria-selected')).toBe('true');
  });

  it('al elegir otra pestaña cambia la lista que se muestra', async () => {
    const { fixture, element } = await create();

    tab(element, 'tab-candidatos').click();
    fixture.detectChanges();

    expect(element.querySelector('.panel-users')).toBeNull();
    expect(element.querySelector('.panel-candidatos')?.textContent).toContain('Carla');
    expect(tab(element, 'tab-candidatos').getAttribute('aria-selected')).toBe('true');
    expect(tab(element, 'tab-habilitadas').getAttribute('aria-selected')).toBe('false');
  });

  it('cada pestaña muestra cuántas cuentas tiene', async () => {
    const { element } = await create();

    expect(tab(element, 'tab-habilitadas').textContent).toContain('3');
    expect(tab(element, 'tab-candidatos').textContent).toContain('1');
    expect(tab(element, 'tab-deshabilitadas').textContent).toContain('0');
  });

  it('resalta el contador de candidatos cuando hay alguien esperando habilitación', async () => {
    const { element } = await create();

    expect(tab(element, 'tab-candidatos').querySelector('.tab-count-alert')).not.toBeNull();
    expect(tab(element, 'tab-habilitadas').querySelector('.tab-count-alert')).toBeNull();
  });
});

// Reactivar: el SuperAdmin elige el rol con el que vuelve la cuenta; arranca seleccionado el que tenía.
describe('Usuarios - Reactivar con rol', () => {
  async function crearConReactivar() {
    const reactivar = vi.fn(() => of({ mensaje: 'Acceso restablecido.' }));
    const notify = vi.fn();
    await TestBed.configureTestingModule({
      imports: [Usuarios],
      providers: [
        provideRouter([]),
        {
          provide: UsuariosPortalService,
          useValue: {
            getCandidatos: () => of([]),
            getHabilitados: () => of([]),
            getDeshabilitados: () => of([INACTIVO]),
            reactivar,
          },
        },
        { provide: NotificationService, useValue: { notify } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(Usuarios);
    fixture.detectChanges();
    return { component: fixture.componentInstance, reactivar };
  }

  it('propone el rol que tenía', async () => {
    const { component } = await crearConReactivar();

    component.pedirReactivar({ ...INACTIVO, rolPortal: 2 });

    expect(component.rolReactivar()).toBe(2);
  });

  it('reactiva con el rol elegido', async () => {
    const { component, reactivar } = await crearConReactivar();

    component.pedirReactivar({ ...INACTIVO, rolPortal: 2 });
    component.rolReactivar.set(1);
    component.confirmarReactivarAccion();

    expect(reactivar).toHaveBeenCalledWith(INACTIVO.idUsuario, 1);
  });
});

// HU-104: los candidatos son un pendiente de la campanita. La pantalla los lee del estado compartido (sin otra
// consulta) y se puede entrar directo a la pestaña Candidatos con ?pestana=candidatos.
describe('Usuarios - pendientes compartidos con la campanita (HU-104)', () => {
  const OTRO: PortalUserCandidateModel = { ...CANDIDATO, idUsuario: 21, nombre: 'Luis', email: 'luis@cacc.test' };

  async function crearConRuta(url: string, habilitar = vi.fn(() => of({ mensaje: 'Cuenta habilitada. Se envió un correo para que active su acceso.' }))) {
    const getCandidatos = vi.fn(() => of([{ ...CANDIDATO }, { ...OTRO }]));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'admin/portal/usuarios', component: Usuarios }, { path: 'admin/portal/resumen-general', children: [] }]),
        { provide: CuotasFaltantesService, useValue: { getMeses: () => of([]) } },
        {
          provide: UsuariosPortalService,
          useValue: {
            getCandidatos,
            getHabilitados: () => of([ADMIN]),
            getDeshabilitados: () => of([]),
            habilitar,
          },
        },
        { provide: NotificationService, useValue: { notify: vi.fn() } },
        { provide: AuthService, useValue: { getUsuario: () => null, isSuperAdmin: () => true, getToken: () => 't' } },
      ],
    });
    const harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl(url, Usuarios);
    return { harness, component, getCandidatos, habilitar, pendientes: TestBed.inject(PendingActionsService) };
  }

  afterEach(() => TestBed.inject(PendingActionsService).stop());

  it('entrar con ?pestana=candidatos abre directamente la pestaña Candidatos', async () => {
    const { component } = await crearConRuta('/admin/portal/usuarios?pestana=candidatos');

    expect(component.pestana()).toBe('candidatos');
  });

  it('si ya está en Usuarios, el enlace de la campanita también cambia la pestaña', async () => {
    const { harness, component } = await crearConRuta('/admin/portal/usuarios');
    expect(component.pestana()).toBe('habilitadas');

    await harness.navigateByUrl('/admin/portal/usuarios?pestana=candidatos');

    expect(component.pestana()).toBe('candidatos');
  });

  it('elegir otra pestaña la deja en la dirección, así el enlace funciona aunque se haya cambiado a mano', async () => {
    const { harness, component } = await crearConRuta('/admin/portal/usuarios?pestana=candidatos');

    component.cambiarPestana('habilitadas');
    await harness.fixture.whenStable();
    await harness.navigateByUrl('/admin/portal/usuarios?pestana=candidatos');

    expect(component.pestana()).toBe('candidatos');
  });

  it('un valor desconocido deja una pestaña válida', async () => {
    const { component } = await crearConRuta('/admin/portal/usuarios?pestana=cualquiera');

    expect(component.pestana()).toBe('habilitadas');
  });

  it('usa los candidatos del estado compartido: no hace otra consulta propia', async () => {
    const { component, getCandidatos } = await crearConRuta('/admin/portal/usuarios');

    expect(component.candidatos().map((c) => c.idUsuario)).toEqual([20, 21]);
    expect(getCandidatos).toHaveBeenCalledTimes(1);
  });

  it('habilitar confirmado quita solo a esa persona, al instante y sin volver a pedir candidatos', async () => {
    const { component, getCandidatos, pendientes } = await crearConRuta('/admin/portal/usuarios?pestana=candidatos');

    component.abrirHabilitar(component.candidatos()[0]);
    component.confirmarHabilitar();

    expect(component.candidatos().map((c) => c.idUsuario)).toEqual([21]);
    expect(pendientes.total()).toBe(1);
    expect(getCandidatos).toHaveBeenCalledTimes(1);
  });

  it('si el servidor habilitó pero no pudo mandar el correo (200), igual deja de ser candidata', async () => {
    const habilitar = vi.fn(() => of({ mensaje: 'Se habilitó el acceso, pero no se pudo enviar el correo de activación. Reintentá desde Usuarios y Permisos.' }));
    const { component, pendientes } = await crearConRuta('/admin/portal/usuarios?pestana=candidatos', habilitar);

    component.abrirHabilitar(component.candidatos()[0]);
    component.confirmarHabilitar();

    expect(pendientes.items().map((p) => p.key)).toEqual(['candidato:21']);
  });

  it('abrir, cancelar o un error al habilitar no descuentan', async () => {
    const habilitar = vi.fn(() => throwError(() => ({ error: { mensaje: 'No se encontró a esa persona.' } })));
    const { component, pendientes } = await crearConRuta('/admin/portal/usuarios?pestana=candidatos', habilitar);

    component.abrirHabilitar(component.candidatos()[0]);
    component.cerrarHabilitar();
    component.abrirHabilitar(component.candidatos()[0]);
    component.confirmarHabilitar();

    expect(pendientes.total()).toBe(2);
    expect(component.dialogError()).toBe('No se encontró a esa persona.');
  });
});
