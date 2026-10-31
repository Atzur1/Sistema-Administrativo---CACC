import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { Usuarios } from './usuarios-y-permisos';
import { UsuariosPortalService } from '../../services/usuarios-portal';
import { NotificationService } from '../../shared/notifications/notification.service';
import { AuthService } from '../../services/auth';
import { PortalUserCandidateModel, PortalUserModel } from '../../models/PortalUserModel';

// Botón "Resetear contraseña" del SuperAdmin: le manda a una cuenta habilitada el enlace para elegir una contraseña
// nueva. El enlace llega al correo de la propia cuenta; el SuperAdmin no ve ni fija la contraseña.

const ADMIN: PortalUserModel = {
  idUsuario: 7, nombre: 'Camila', apellido: 'Candidata', dni: '30111222',
  email: 'camila@cacc.test', rolPortal: 2, activo: true, activacionPendiente: false,
};
const SIN_ACTIVAR: PortalUserModel = { ...ADMIN, idUsuario: 8, nombre: 'Nuevo', email: 'nuevo@cacc.test', activacionPendiente: true };
const INACTIVO: PortalUserModel = { ...ADMIN, idUsuario: 9, nombre: 'Baja', email: 'baja@cacc.test', activo: false };

const CANDIDATO: PortalUserCandidateModel = {
  idUsuario: 20, nombre: 'Carla', apellido: 'Candidata', dni: '30.555.666', email: 'carla@cacc.test', fkIdRolSugerido: 2,
};

// Opciones extra para armar la pantalla: la cuenta con la que se inició sesión y el doble de "cambiar rol".
interface OpcionesCreate {
  yo?: { email: string; dni?: string } | null;
  cambiarRol?: ReturnType<typeof vi.fn>;
}

async function create(
  resetear = vi.fn(() => of({ mensaje: 'Se envió el enlace.' })),
  editarCandidato = vi.fn(() => of({ mensaje: 'Datos corregidos.' })),
  opciones: OpcionesCreate = {},
) {
  const notify = vi.fn();
  const cambiarRol = opciones.cambiarRol ?? vi.fn(() => of({ mensaje: 'Rol actualizado.' }));
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
          cambiarRol,
        },
      },
      { provide: NotificationService, useValue: { notify } },
      { provide: AuthService, useValue: { getUsuario: () => opciones.yo ?? null } },
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
    expect(candidato.dni).toBe('30555777');
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
// Cambiar el rol da o quita permisos: no se aplica al elegirlo en el desplegable sino al confirmar.
describe('Usuarios - cambiar el rol con confirmación', () => {
  it('elegir otro rol no lo aplica: abre la confirmación y no llama al servidor', async () => {
    const { component, cambiarRol } = await create();

    component.onRolHabilitadoChange(ADMIN, 1);

    expect(component.cambioDeRol()).toEqual({ usuario: ADMIN, rol: 1 });
    expect(cambiarRol).not.toHaveBeenCalled();
    expect(ADMIN.rolPortal).toBe(2); // sigue siendo Administrador hasta que se confirme
  });

  it('elegir el mismo rol que ya tiene no pide nada', async () => {
    const { component } = await create();

    component.onRolHabilitadoChange(ADMIN, 2);

    expect(component.cambioDeRol()).toBeNull();
  });

  it('confirmar aplica el cambio, avisa y cierra la confirmación', async () => {
    const usuario = { ...ADMIN, idUsuario: 70 };
    const { component, cambiarRol, notify } = await create();

    component.onRolHabilitadoChange(usuario, 1);
    component.confirmarCambioDeRol();

    expect(cambiarRol).toHaveBeenCalledWith(70, { rolPortal: 1 });
    expect(usuario.rolPortal).toBe(1);
    expect(notify).toHaveBeenCalledWith('Rol actualizado.', 'success');
    expect(component.cambioDeRol()).toBeNull();
  });

  it('cancelar no cambia nada y vuelve a dibujar el desplegable con el rol real', async () => {
    const { component, cambiarRol } = await create();
    const antes = component.refrescoRoles();

    component.onRolHabilitadoChange(ADMIN, 1);
    component.cancelarCambioDeRol();

    expect(cambiarRol).not.toHaveBeenCalled();
    expect(component.cambioDeRol()).toBeNull();
    expect(component.refrescoRoles()).toBe(antes + 1);
  });

  it('si el servidor lo rechaza muestra su motivo y deja el rol como estaba', async () => {
    const rechazo = vi.fn(() => throwError(() => ({ error: { mensaje: 'Tiene que quedar al menos un SuperAdmin.' } })));
    const usuario = { ...ADMIN, idUsuario: 71, rolPortal: 1 as const };
    const { component, notify } = await create(undefined, undefined, { cambiarRol: rechazo });

    component.onRolHabilitadoChange(usuario, 2);
    component.confirmarCambioDeRol();

    expect(notify).toHaveBeenCalledWith('Tiene que quedar al menos un SuperAdmin.', 'error');
    expect(usuario.rolPortal).toBe(1);
  });

  it('la confirmación explica qué cambia: dar acceso total o quitarlo', async () => {
    const { component } = await create();

    component.onRolHabilitadoChange(ADMIN, 1);
    expect(component.textoCambioDeRol!.titulo).toContain('acceso total');
    expect(component.textoCambioDeRol!.cuerpo).toContain('todo el portal');

    component.cancelarCambioDeRol();
    component.onRolHabilitadoChange({ ...ADMIN, rolPortal: 1 }, 2);
    expect(component.textoCambioDeRol!.cuerpo).toContain('solo va a ver Cobranza');
  });

  it('si es la propia cuenta y se quita el acceso total, lo advierte', async () => {
    const { component } = await create(undefined, undefined, { yo: { email: 'camila@cacc.test', dni: '30111222' } });
    const propia = { ...ADMIN, rolPortal: 1 as const };

    component.onRolHabilitadoChange(propia, 2);

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
