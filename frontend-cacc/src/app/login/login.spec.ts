import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of } from 'rxjs';

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
  function create(): {
    fixture: ComponentFixture<Login>;
    component: Login;
    navigate: ReturnType<typeof vi.fn>;
  } {
    const navigate = vi.fn();
    TestBed.configureTestingModule({
      imports: [Login],
      providers: [
        {
          provide: AuthService,
          useValue: { login: vi.fn(() => of({ email: 'admin@cacc.com', rol: 1, token: 'tok' })) },
        },
        { provide: Router, useValue: { navigate } },
      ],
    });

    const fixture = TestBed.createComponent(Login);
    const component = fixture.componentInstance;
    fixture.detectChanges();
    return { fixture, component, navigate };
  }

  afterEach(() => {
    vi.useRealTimers();
  });

  it('hides the overlay and shows the success message cleanly right after the response arrives', () => {
    const { component, navigate } = create();
    component.loginForm.setValue({ usuario: 'admin', contrasena: '1234', recordarme: false });

    component.onSubmit();

    expect(component.mostrarOverlay).toBe(false);
    expect(component.mensajeExito).toContain('Bienvenido');
    expect(navigate).not.toHaveBeenCalled();
  });

  it('brings the overlay back only after the reading pause, then navigates only after the transition beat', () => {
    vi.useFakeTimers();
    const { component, navigate } = create();
    component.loginForm.setValue({ usuario: 'admin', contrasena: '1234', recordarme: false });

    component.onSubmit();

    vi.advanceTimersByTime(LECTURA_MENSAJE_EXITO_MS - 1);
    expect(component.mostrarOverlay).toBe(false); // todavía en la ventana de lectura

    vi.advanceTimersByTime(1);
    expect(component.mostrarOverlay).toBe(true); // pausa de lectura terminada, overlay de vuelta
    expect(navigate).not.toHaveBeenCalled(); // pero todavía no navegó

    vi.advanceTimersByTime(TRANSICION_A_PORTALES_MS);
    expect(navigate).toHaveBeenCalledWith(['/portales']);
  });
});
