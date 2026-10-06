import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { Usuarios } from './usuarios-y-permisos';
import { UsuariosPortalService } from '../../services/usuarios-portal';
import { NotificationService } from '../../shared/notifications/notification.service';
import { UsuarioCandidato, UsuarioPortal } from '../../models/UsuarioPortalModel';

// Botón "Resetear contraseña" del SuperAdmin: le manda a una cuenta habilitada el enlace para elegir una contraseña
// nueva. El enlace llega al correo de la propia cuenta; el SuperAdmin no ve ni fija la contraseña.

const ADMIN: UsuarioPortal = {
  idUsuario: 7, nombre: 'Camila', apellido: 'Candidata', dni: '30111222',
  email: 'camila@cacc.test', rolPortal: 2, activo: true, activacionPendiente: false,
};
const SIN_ACTIVAR: UsuarioPortal = { ...ADMIN, idUsuario: 8, nombre: 'Nuevo', email: 'nuevo@cacc.test', activacionPendiente: true };
const INACTIVO: UsuarioPortal = { ...ADMIN, idUsuario: 9, nombre: 'Baja', email: 'baja@cacc.test', activo: false };

const CANDIDATO: UsuarioCandidato = {
  idUsuario: 20, nombre: 'Carla', apellido: 'Candidata', dni: '30.555.666', email: 'carla@cacc.test', fkIdRolSugerido: 2,
};

async function create(
  resetear = vi.fn(() => of({ mensaje: 'Se envió el enlace.' })),
  editarCandidato = vi.fn(() => of({ mensaje: 'Datos corregidos.' })),
) {
  const notify = vi.fn();
  await TestBed.configureTestingModule({
    imports: [Usuarios],
    providers: [
      {
        provide: UsuariosPortalService,
        useValue: {
          getCandidatos: () => of([{ ...CANDIDATO }]),
          getHabilitados: () => of([ADMIN, SIN_ACTIVAR, INACTIVO]),
          getDeshabilitados: () => of([]),
          resetearContrasena: resetear,
          editarCandidato,
        },
      },
      { provide: NotificationService, useValue: { notify } },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(Usuarios);
  fixture.detectChanges();
  return { fixture, component: fixture.componentInstance, element: fixture.nativeElement as HTMLElement, resetear, editarCandidato, notify };
}

describe('Usuarios - Resetear contraseña', () => {
  it('el botón dice "Resetear contraseña", o "Reenviar activación" si la cuenta nunca creó la suya', async () => {
    const { component } = await create();

    expect(component.textoAccionEnlace(ADMIN)).toBe('Resetear contraseña');
    expect(component.textoAccionEnlace(SIN_ACTIVAR)).toBe('Reenviar activación');
  });

  it('aparece en las cuentas activas y no en la que está inactiva en la base del otro equipo', async () => {
    const { element } = await create();

    const botones = Array.from(element.querySelectorAll('.panel-users .cell-actions button'))
      .map((b) => b.textContent?.trim())
      .filter((t) => t !== 'Deshabilitar');

    expect(botones).toEqual(['Resetear contraseña', 'Reenviar activación']); // 2 de las 3 filas
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

    expect(element.querySelector('.dialog')?.textContent).toContain('camila@cacc.test');
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
      nombre: 'Carla', apellido: 'Candidata', dni: '30.555.777', email: 'carla@cacc.test',
    });
    expect(candidato.dni).toBe('30555777');
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
