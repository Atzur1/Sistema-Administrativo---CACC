import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import { AccountAccess, LECTURA_MENSAJE_EXITO_MS, calcularFortaleza, evaluarRequisitos } from './account-access';
import { AuthService } from '../services/auth';

// Dos pantallas con el mismo mecanismo: "Crea tu contraseña" (primer ingreso) y "Reestablece tu
// contraseña" (olvidé mi contraseña). Antes las dos eran la misma pantalla "Restablecer contraseña".

type Modo = 'crear' | 'restablecer';

async function create(
  modo: Modo,
  options: { token?: string | null; complete?: ReturnType<typeof vi.fn> } = {},
) {
  const token = options.token === undefined ? 'token-de-prueba' : options.token;
  const complete = options.complete ?? vi.fn(() => of({ mensaje: 'ok' }));

  await TestBed.configureTestingModule({
    imports: [AccountAccess],
    providers: [
      provideRouter([]),
      { provide: AuthService, useValue: { completeAccountAccess: complete } },
      {
        provide: ActivatedRoute,
        useValue: { snapshot: { data: { modo }, queryParamMap: convertToParamMap(token ? { token } : {}) } },
      },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(AccountAccess);
  fixture.detectChanges();
  const element = fixture.nativeElement as HTMLElement;
  return { fixture, element, complete, component: fixture.componentInstance, router: TestBed.inject(Router) };
}

function texto(element: HTMLElement): string {
  return (element.textContent ?? '').replace(/\s+/g, ' ');
}

function llenar(component: AccountAccess, password: string, confirmacion: string) {
  component.form.controls.password.setValue(password);
  component.form.controls.confirmacion.setValue(confirmacion);
  component.form.controls.confirmacion.markAsDirty();
}

describe('AccountAccess - cada modo tiene su propia pantalla', () => {
  it('"crear" es el primer ingreso: Crea tu contraseña', async () => {
    const { element } = await create('crear');

    expect(element.querySelector('h1')?.textContent).toContain('Crea tu contraseña');
    expect(texto(element)).toContain('Primer ingreso');
    expect(element.querySelector('button[type="submit"]')?.textContent).toContain('Crear contraseña');
    expect(texto(element)).not.toContain('Reestablece');
    // No es una recuperación: no puede decir que el enlace es del superadministrador.
    expect(texto(element)).not.toContain('superadministrador');
  });

  it('"restablecer" es la recuperación: Reestablece tu contraseña', async () => {
    const { element } = await create('restablecer');

    expect(element.querySelector('h1')?.textContent).toContain('Reestablece tu contraseña');
    expect(texto(element)).toContain('Recuperar acceso');
    expect(element.querySelector('button[type="submit"]')?.textContent).toContain('Reestablecer contraseña');
    expect(texto(element)).not.toContain('Crea tu contraseña');
  });

  it('marca el modo en la pantalla para darle un acento distinto a cada una', async () => {
    const crear = await create('crear');
    expect(crear.element.querySelector('main')?.getAttribute('data-modo')).toBe('crear');
    TestBed.resetTestingModule();

    const restablecer = await create('restablecer');
    expect(restablecer.element.querySelector('main')?.getAttribute('data-modo')).toBe('restablecer');
  });
});

describe('AccountAccess - formulario', () => {
  it('no deja enviar si las dos contraseñas no coinciden y lo avisa', async () => {
    const { fixture, element, complete, component } = await create('crear');

    llenar(component, 'Abcdef1!x', 'otra-distinta');
    fixture.detectChanges();

    expect(texto(element)).toContain('Las contraseñas no coinciden.');
    expect((element.querySelector('button[type="submit"]') as HTMLButtonElement).disabled).toBe(true);

    component.submit();
    expect(complete).not.toHaveBeenCalled();
  });

  it('no deja enviar una contraseña de menos de 8 caracteres', async () => {
    const { fixture, element, complete, component } = await create('crear');

    llenar(component, 'corta', 'corta');
    fixture.detectChanges();

    expect((element.querySelector('button[type="submit"]') as HTMLButtonElement).disabled).toBe(true);
    component.submit();
    expect(complete).not.toHaveBeenCalled();
  });

  it('envía el token del enlace y la contraseña, y muestra el éxito del modo', async () => {
    const { fixture, element, complete, component } = await create('crear');

    llenar(component, 'Abcdef1!x', 'Abcdef1!x');
    component.submit();
    fixture.detectChanges();

    expect(complete).toHaveBeenCalledWith('token-de-prueba', 'Abcdef1!x');
    expect(texto(element)).toContain('¡Contraseña creada!');
    expect(texto(element)).toContain('Ir al inicio de sesión');
  });

  it('en la recuperación el éxito dice que la contraseña fue reestablecida', async () => {
    const { fixture, element, component } = await create('restablecer');

    llenar(component, 'Abcdef1!x', 'Abcdef1!x');
    component.submit();
    fixture.detectChanges();

    expect(texto(element)).toContain('¡Contraseña reestablecida!');
  });

  it('después del éxito vuelve al inicio de sesión solo, pasado el tiempo de lectura', async () => {
    vi.useFakeTimers();
    try {
      const { component, router } = await create('crear');
      const navegar = vi.spyOn(router, 'navigate').mockResolvedValue(true);

      llenar(component, 'Abcdef1!x', 'Abcdef1!x');
      component.submit();

      vi.advanceTimersByTime(LECTURA_MENSAJE_EXITO_MS - 1);
      expect(navegar).not.toHaveBeenCalled();
      vi.advanceTimersByTime(1);
      expect(navegar).toHaveBeenCalledWith(['/']);
    } finally {
      vi.useRealTimers();
    }
  });

  it('el botón "Ir al inicio de sesión" no espera y cancela la redirección automática', async () => {
    vi.useFakeTimers();
    try {
      const { fixture, element, component, router } = await create('crear');
      const navegar = vi.spyOn(router, 'navigate').mockResolvedValue(true);

      llenar(component, 'Abcdef1!x', 'Abcdef1!x');
      component.submit();
      fixture.detectChanges();

      (Array.from(element.querySelectorAll('button')).find((b) => b.textContent?.includes('Ir al inicio de sesión')) as HTMLButtonElement).click();
      expect(navegar).toHaveBeenCalledTimes(1);

      vi.advanceTimersByTime(LECTURA_MENSAJE_EXITO_MS * 2);
      expect(navegar).toHaveBeenCalledTimes(1); // no navega una segunda vez
    } finally {
      vi.useRealTimers();
    }
  });
});

describe('AccountAccess - enlace inválido', () => {
  it('sin token muestra enlace inválido, con el consejo del primer ingreso', async () => {
    const { element } = await create('crear', { token: null });

    expect(texto(element)).toContain('Este enlace ya no es válido');
    expect(texto(element)).toContain('administrador');
    expect(element.querySelector('form')).toBeNull();
  });

  it('sin token en la recuperación el consejo es pedir otro desde "¿Olvidaste tu contraseña?"', async () => {
    const { element } = await create('restablecer', { token: null });

    expect(texto(element)).toContain('Este enlace ya no es válido');
    expect(texto(element)).toContain('¿Olvidaste tu contraseña?');
  });

  it('si el servidor rechaza el token (400) pasa a enlace inválido en lugar de seguir pidiendo la contraseña', async () => {
    const complete = vi.fn(() => throwError(() => ({ status: 400 })));
    const { fixture, element, component } = await create('restablecer', { complete });

    llenar(component, 'Abcdef1!x', 'Abcdef1!x');
    component.submit();
    fixture.detectChanges();

    expect(texto(element)).toContain('Este enlace ya no es válido');
    expect(element.querySelector('form')).toBeNull();
  });

  it('un error de red (no 400) deja el formulario y muestra un aviso para reintentar', async () => {
    const complete = vi.fn(() => throwError(() => ({ status: 500 })));
    const { fixture, element, component } = await create('crear', { complete });

    llenar(component, 'Abcdef1!x', 'Abcdef1!x');
    component.submit();
    fixture.detectChanges();

    expect(texto(element)).toContain('No se pudo completar la operación. Intentá nuevamente.');
    expect(element.querySelector('form')).not.toBeNull();
  });
});

describe('Fortaleza y requisitos de la contraseña', () => {
  it('solo la longitud mínima es obligatoria; el resto son recomendaciones', () => {
    const requisitos = evaluarRequisitos('');

    expect(requisitos.filter((r) => r.obligatorio).map((r) => r.id)).toEqual(['largo']);
    expect(requisitos.filter((r) => !r.obligatorio).map((r) => r.id)).toEqual(['mayusculasMinusculas', 'numero', 'simbolo']);
  });

  it('detecta cada requisito', () => {
    const cumplidos = (pw: string) => evaluarRequisitos(pw).filter((r) => r.cumplido).map((r) => r.id);

    expect(cumplidos('abcdefgh')).toEqual(['largo']);
    expect(cumplidos('Abcdefg1')).toEqual(['largo', 'mayusculasMinusculas', 'numero']);
    expect(cumplidos('Abcdef1!')).toEqual(['largo', 'mayusculasMinusculas', 'numero', 'simbolo']);
    expect(cumplidos('Ñandú123')).toContain('mayusculasMinusculas'); // contempla ñ y acentos
  });

  it('califica de vacía a fuerte', () => {
    expect(calcularFortaleza('')).toEqual({ nivel: 0, etiqueta: '' });
    expect(calcularFortaleza('abc')).toEqual({ nivel: 1, etiqueta: 'Muy corta' });
    expect(calcularFortaleza('abcdefgh')).toEqual({ nivel: 2, etiqueta: 'Aceptable' });
    expect(calcularFortaleza('Abcdefg1')).toEqual({ nivel: 3, etiqueta: 'Buena' });
    expect(calcularFortaleza('Abcdef1!xyz')).toEqual({ nivel: 4, etiqueta: 'Fuerte' });
  });

  it('una contraseña larga solo en minúsculas es aceptable, no débil', () => {
    expect(calcularFortaleza('abcdefghijkl').nivel).toBe(2);
  });
});
