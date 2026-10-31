import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of, throwError } from 'rxjs';

import { Login, LECTURA_MENSAJE_EXITO_MS, TRANSICION_A_PORTALES_MS } from './login';
import { AuthService } from '../services/auth';

describe('Login', () => {
  let component: Login;
  let fixture: ComponentFixture<Login>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Login],
    }).compileComponents();

    fixture = TestBed.createComponent(Login);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

// El pedido puntual acá: que el mensaje de éxito se vea limpio (overlay apagado)
// un rato antes de que vuelva la pantalla de carga y se navegue a portales — no
// las tres cosas encimadas, que era como se veía antes de este fix.
describe('Login - secuencia de carga tras un login correcto', () => {
  function create(respuesta: object = { email: 'admin@cacc.com', role: 1, token: 'tok', portales: ['administrativo', 'deportivo', 'personas'] }): {
    fixture: ComponentFixture<Login>;
    component: Login;
    navigate: ReturnType<typeof vi.fn>;
    auth: { irAlInicio: ReturnType<typeof vi.fn>; logout: ReturnType<typeof vi.fn> };
  } {
    const navigate = vi.fn();
    const auth = {
      login: vi.fn(() => ('status' in respuesta ? throwError(() => respuesta) : of(respuesta))),
      // Desde el ingreso por rol, el login navega con irAlInicio: la cuenta del test es SuperAdmin (rol 1).
      irAlInicio: vi.fn((router: Router) => router.navigate(['/portales'])),
      logout: vi.fn(),
    };
    TestBed.configureTestingModule({
      imports: [Login],
      providers: [
        {
          provide: AuthService,
          useValue: auth,
        },
        { provide: Router, useValue: { navigate } },
      ],
    });

    const fixture = TestBed.createComponent(Login);
    const component = fixture.componentInstance;
    fixture.detectChanges();
    return { fixture, component, navigate, auth };
  }

  afterEach(() => {
    vi.useRealTimers();
  });

  it('hides the overlay and shows the success message cleanly right after the response arrives', () => {
    const { component, navigate } = create();
    component.loginForm.setValue({ username: 'admin', password: '1234', recordarme: false });

    component.onSubmit();

    expect(component.mostrarOverlay).toBe(false);
    expect(component.mensajeExito).toContain('Bienvenido');
    expect(navigate).not.toHaveBeenCalled();
  });

  it('brings the overlay back only after the reading pause, then navigates only after the transition beat', () => {
    vi.useFakeTimers();
    const { component, navigate } = create();
    component.loginForm.setValue({ username: 'admin', password: '1234', recordarme: false });

    component.onSubmit();

    vi.advanceTimersByTime(LECTURA_MENSAJE_EXITO_MS - 1);
    expect(component.mostrarOverlay).toBe(false); // todavía en la ventana de lectura

    vi.advanceTimersByTime(1);
    expect(component.mostrarOverlay).toBe(true); // pausa de lectura terminada, overlay de vuelta
    expect(navigate).not.toHaveBeenCalled(); // pero todavía no navegó

    vi.advanceTimersByTime(TRANSICION_A_PORTALES_MS);
    expect(navigate).toHaveBeenCalledWith(['/portales']);
  });

  // HU-007: una cuenta sin ningún portal no ve "¡Bienvenido!" ni se la manda a ningún lado.
  it('sin portales en la respuesta no da la bienvenida ni navega: cierra la sesión y avisa', () => {
    vi.useFakeTimers();
    const { component, navigate, auth } = create({ email: 'x@cacc.com', role: 0, token: 'tok', portales: [] });
    component.loginForm.setValue({ username: '20111222', password: '1234', recordarme: false });

    component.onSubmit();
    vi.advanceTimersByTime(LECTURA_MENSAJE_EXITO_MS + TRANSICION_A_PORTALES_MS);

    expect(component.mensajeExito).toBe('');
    expect(component.mensajeError).toBe('Tu cuenta no tiene accesos habilitados.');
    expect(auth.logout).toHaveBeenCalled();
    expect(auth.irAlInicio).not.toHaveBeenCalled();
    expect(navigate).not.toHaveBeenCalled();
    expect(component.isSubmitting).toBe(false);
  });

  it('muestra el aviso del backend cuando el login responde 403 (cuenta sin accesos)', () => {
    const { component, auth } = create({ status: 403, error: { mensaje: 'Tu cuenta no tiene accesos habilitados.' } });
    component.loginForm.setValue({ username: '20111222', password: '1234', recordarme: false });

    component.onSubmit();

    expect(component.mensajeError).toBe('Tu cuenta no tiene accesos habilitados.');
    expect(auth.irAlInicio).not.toHaveBeenCalled();
  });
});

describe('Login - aviso al volver sin accesos', () => {
  afterEach(() => window.history.replaceState({}, '', '/'));

  it('al volver con ?sesion=sin-acceso muestra el aviso', async () => {
    window.history.replaceState({}, '', '/?sesion=sin-acceso');
    await TestBed.configureTestingModule({ imports: [Login] }).compileComponents();
    const fixture = TestBed.createComponent(Login);
    fixture.detectChanges();

    expect(fixture.componentInstance.mensajeError).toBe('Tu cuenta no tiene accesos habilitados.');
  });
});

// HU-109: el campo de contraseña del login se puede mostrar, avisa Bloq Mayús y el error no revela cuentas.
describe('Login - campo de contraseña y mensajes de error', () => {
  function crear(login = vi.fn(() => throwError(() => ({ status: 401 })))) {
    TestBed.configureTestingModule({
      imports: [Login],
      providers: [
        { provide: AuthService, useValue: { login, requestPasswordReset: vi.fn(() => of({ mensaje: 'Si los datos corresponden a una cuenta activa, el enlace llegará al correo registrado en esa cuenta.' })) } },
        { provide: Router, useValue: { navigate: vi.fn() } },
      ],
    });
    const fixture = TestBed.createComponent(Login);
    fixture.detectChanges();
    return { fixture, component: fixture.componentInstance, element: fixture.nativeElement as HTMLElement };
  }

  it('el ojo es un botón que muestra y oculta lo escrito', () => {
    const { fixture, element } = crear();
    const input = element.querySelector<HTMLInputElement>('#TituloContraseña')!;
    const ojo = element.querySelector<HTMLButtonElement>('button.eye-icon')!;

    expect(ojo.type).toBe('button');
    expect(input.type).toBe('password');

    ojo.click();
    fixture.detectChanges();
    expect(input.type).toBe('text');
    expect(ojo.getAttribute('aria-pressed')).toBe('true');
    expect(ojo.getAttribute('aria-label')).toBe('Ocultar contraseña');
  });

  it('los gestores de contraseñas reconocen el usuario y la contraseña', () => {
    const { element } = crear();

    expect(element.querySelector('#TituloContraseña')!.getAttribute('autocomplete')).toBe('current-password');
    expect(element.querySelector('[formControlName="username"]')!.getAttribute('autocomplete')).toBe('username');
  });

  it('avisa cuando Bloq Mayús está activado y lo saca cuando se desactiva', () => {
    const { fixture, element } = crear();
    const input = element.querySelector<HTMLInputElement>('#TituloContraseña')!;
    const tecla = (caps: boolean) => {
      const evento = new KeyboardEvent('keydown', { key: 'a' });
      Object.defineProperty(evento, 'getModifierState', { value: () => caps });
      input.dispatchEvent(evento);
      fixture.detectChanges();
    };

    tecla(true);
    expect(element.querySelector('.caps-warning')?.textContent).toContain('Bloq Mayús está activado');

    tecla(false);
    expect(element.querySelector('.caps-warning')).toBeNull();
  });

  it('un ingreso rechazado no dice si la cuenta existe', () => {
    const { component } = crear();
    component.loginForm.setValue({ username: '12345678', password: 'cualquiera1', recordarme: false });

    component.onSubmit();

    expect(component.mensajeError).toBe('El DNI o la contraseña no coinciden con una cuenta activa.');
    expect(component.mensajeError.toLowerCase()).not.toMatch(/no existe|no está registrad|no encontr/);
  });

  it('"Olvidé mi contraseña" muestra la respuesta que es igual para cualquier cuenta', () => {
    const { component } = crear();
    component.forgotForm.setValue({ usuario: '12345678' });

    component.enviarRecuperacion();

    expect(component.forgotMensaje).toContain('Si los datos corresponden a una cuenta activa');
  });
});
