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

    llenar(component, 'Abcdef1!xy', 'otra-distinta');
    fixture.detectChanges();

    expect(texto(element)).toContain('Las contraseñas no coinciden.');
    expect((element.querySelector('button[type="submit"]') as HTMLButtonElement).disabled).toBe(true);

    component.submit();
    expect(complete).not.toHaveBeenCalled();
  });

  it('no deja enviar una contraseña de menos de 10 caracteres', async () => {
    const { fixture, element, complete, component } = await create('crear');

    llenar(component, 'corta', 'corta');
    fixture.detectChanges();

    expect((element.querySelector('button[type="submit"]') as HTMLButtonElement).disabled).toBe(true);
    component.submit();
    expect(complete).not.toHaveBeenCalled();
  });

  it('envía el token del enlace y la contraseña, y muestra el éxito del modo', async () => {
    const { fixture, element, complete, component } = await create('crear');

    llenar(component, 'Abcdef1!xy', 'Abcdef1!xy');
    component.submit();
    fixture.detectChanges();

    expect(complete).toHaveBeenCalledWith('token-de-prueba', 'Abcdef1!xy');
    expect(texto(element)).toContain('¡Contraseña creada!');
    expect(texto(element)).toContain('Ir al inicio de sesión');
  });

  it('en la recuperación el éxito dice que la contraseña fue reestablecida', async () => {
    const { fixture, element, component } = await create('restablecer');

    llenar(component, 'Abcdef1!xy', 'Abcdef1!xy');
    component.submit();
    fixture.detectChanges();

    expect(texto(element)).toContain('¡Contraseña reestablecida!');
  });

  it('después del éxito vuelve al inicio de sesión solo, pasado el tiempo de lectura', async () => {
    vi.useFakeTimers();
    try {
      const { component, router } = await create('crear');
      const navegar = vi.spyOn(router, 'navigate').mockResolvedValue(true);

      llenar(component, 'Abcdef1!xy', 'Abcdef1!xy');
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

      llenar(component, 'Abcdef1!xy', 'Abcdef1!xy');
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

    llenar(component, 'Abcdef1!xy', 'Abcdef1!xy');
    component.submit();
    fixture.detectChanges();

    expect(texto(element)).toContain('Este enlace ya no es válido');
    expect(element.querySelector('form')).toBeNull();
  });

  it('un error de red (no 400) deja el formulario y muestra un aviso para reintentar', async () => {
    const complete = vi.fn(() => throwError(() => ({ status: 500 })));
    const { fixture, element, component } = await create('crear', { complete });

    llenar(component, 'Abcdef1!xy', 'Abcdef1!xy');
    component.submit();
    fixture.detectChanges();

    expect(texto(element)).toContain('No se pudo completar la operación. Intentá nuevamente.');
    expect(element.querySelector('form')).not.toBeNull();
  });

  it('con demasiados intentos (429) avisa que hay que esperar, en lugar del mensaje genérico', async () => {
    const complete = vi.fn(() => throwError(() => ({ status: 429 })));
    const { fixture, element, component } = await create('crear', { complete });

    llenar(component, 'Abcdef1!xy', 'Abcdef1!xy');
    component.submit();
    fixture.detectChanges();

    expect(texto(element)).toContain('demasiados intentos');
    expect(texto(element)).toContain('Esperá unos minutos');
    expect(texto(element)).not.toContain('No se pudo completar la operación');
    expect(element.querySelector('form')).not.toBeNull();
  });
});

describe('Fortaleza y requisitos de la contraseña', () => {
  it('la longitud mínima, las letras y los números son obligatorios; el resto son recomendaciones', () => {
    const requisitos = evaluarRequisitos('');

    expect(requisitos.filter((r) => r.obligatorio).map((r) => r.id)).toEqual(['largo', 'letras', 'numeros']);
    expect(requisitos.filter((r) => r.obligatorio).map((r) => r.texto)).toEqual(['Al menos 10 caracteres', 'Contiene letras', 'Contiene números']);
    expect(requisitos.filter((r) => !r.obligatorio).map((r) => r.id)).toEqual(['mayusculasMinusculas', 'simbolo']);
  });

  it('detecta cada requisito', () => {
    const cumplidos = (pw: string) => evaluarRequisitos(pw).filter((r) => r.cumplido).map((r) => r.id);

    expect(cumplidos('abcdefghij')).toEqual(['largo', 'letras']);
    expect(cumplidos('1234567890')).toEqual(['largo', 'numeros']);
    expect(cumplidos('abcdefgh1X')).toEqual(['largo', 'letras', 'numeros', 'mayusculasMinusculas']);
    expect(cumplidos('Abcdefgh1!')).toEqual(['largo', 'letras', 'numeros', 'mayusculasMinusculas', 'simbolo']);
    expect(cumplidos('Abc1')).toEqual(['letras', 'numeros', 'mayusculasMinusculas']);
    expect(cumplidos('Ñandú12345')).toContain('mayusculasMinusculas'); // contempla ñ y acentos
  });

  it('califica de vacía a fuerte', () => {
    expect(calcularFortaleza('')).toEqual({ nivel: 0, etiqueta: '' });
    expect(calcularFortaleza('abc')).toEqual({ nivel: 1, etiqueta: 'Muy corta' });
    expect(calcularFortaleza('abcdefghi1')).toEqual({ nivel: 2, etiqueta: 'Aceptable' });
    expect(calcularFortaleza('Abcdefghi1')).toEqual({ nivel: 2, etiqueta: 'Aceptable' });
    expect(calcularFortaleza('Abcdefgh1!')).toEqual({ nivel: 3, etiqueta: 'Buena' });
    expect(calcularFortaleza('Abcdef1!xyzw')).toEqual({ nivel: 4, etiqueta: 'Fuerte' }); // 12+ caracteres suma
  });

  it('sin números (o sin letras) no deja enviar', async () => {
    const { component } = await create('crear');

    llenar(component, 'solamenteletras', 'solamenteletras');
    expect(component.form.invalid).toBe(true);

    llenar(component, '12345678901', '12345678901');
    expect(component.form.invalid).toBe(true);
  });

  // HU-087: el servidor es la autoridad (DNI, nombre, apellido, correo, comunes, club). Cada motivo se muestra en su
  // propia línea, y el mismo enlace sirve para volver a intentar.
  it('si el servidor rechaza la contraseña por la política, lista cada motivo y deja elegir otra', async () => {
    const complete = vi.fn(() =>
      throwError(() => ({
        status: 400,
        error: {
          exito: false,
          mensaje: 'La contraseña no cumple los requisitos: No puede contener tu apellido. Es una contraseña demasiado común. Elegí otra.',
          problemas: ['No puede contener tu apellido.', 'Es una contraseña demasiado común. Elegí otra.'],
        },
      })),
    );
    const { fixture, element, component } = await create('crear', { complete });

    llenar(component, 'Perez-2026-x', 'Perez-2026-x');
    component.submit();
    fixture.detectChanges();

    const lista = element.querySelector<HTMLElement>('.password-problems')!;
    expect(texto(lista)).toContain('La contraseña no cumple los requisitos:');
    expect(Array.from(lista.querySelectorAll<HTMLElement>('li')).map(texto)).toEqual([
      'No puede contener tu apellido.',
      'Es una contraseña demasiado común. Elegí otra.',
    ]);
    expect(lista.getAttribute('role')).toBe('alert');
    expect(element.querySelector('form')).not.toBeNull();
    expect(texto(element)).not.toContain('Validation failed');
  });

  it('después de un rechazo de la política, vuelve a enviar con el MISMO enlace', async () => {
    const complete = vi
      .fn()
      .mockReturnValueOnce(throwError(() => ({ status: 400, error: { problemas: ['Es una contraseña demasiado común. Elegí otra.'] } })))
      .mockReturnValueOnce(of({ mensaje: 'La contraseña se actualizó. Ya puede iniciar sesión.' }));
    const { fixture, element, component } = await create('restablecer', { complete });

    llenar(component, 'password123', 'password123');
    component.submit();
    fixture.detectChanges();
    llenar(component, 'Tribuna-Norte-77', 'Tribuna-Norte-77');
    component.submit();
    fixture.detectChanges();

    expect(complete).toHaveBeenNthCalledWith(1, 'token-de-prueba', 'password123');
    expect(complete).toHaveBeenNthCalledWith(2, 'token-de-prueba', 'Tribuna-Norte-77');
    expect(component.estado).toBe('exito');
    expect(element.querySelector('.password-problems')).toBeNull();
  });
});

// HU-109: los dos campos se pueden mostrar, Bloq Mayús se sigue avisando y las reglas se tildan al escribir.
describe('AccountAccess - campos de contraseña más claros', () => {
  it('cada campo tiene su botón para mostrar u ocultar lo escrito', async () => {
    const { fixture, element } = await create('crear');
    const campos = () => Array.from(element.querySelectorAll<HTMLInputElement>('input[autocomplete="new-password"]'));
    const ojos = Array.from(element.querySelectorAll<HTMLButtonElement>('button[aria-pressed]'));

    expect(campos().map((c) => c.type)).toEqual(['password', 'password']);
    expect(ojos.length).toBe(2);

    ojos.forEach((ojo) => ojo.click());
    fixture.detectChanges();
    expect(campos().map((c) => c.type)).toEqual(['text', 'text']);
  });

  it('el aviso de Bloq Mayús se mantiene', async () => {
    const { fixture, element } = await create('restablecer');
    const evento = new KeyboardEvent('keydown', { key: 'A' });
    Object.defineProperty(evento, 'getModifierState', { value: () => true });

    element.querySelector('input[autocomplete="new-password"]')!.dispatchEvent(evento);
    fixture.detectChanges();

    expect(texto(element)).toContain('Bloq Mayús está activado');
  });

  it('las reglas de la política se ven y se van tildando a medida que se cumplen', async () => {
    const { fixture, element } = await create('crear');
    const tildadas = () => element.querySelectorAll('.requirements li.is-met').length;
    const escribir = (valor: string) => {
      const input = element.querySelector<HTMLInputElement>('#password')!;
      input.value = valor;
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();
    };

    expect(texto(element)).toContain('Al menos 10 caracteres');
    expect(texto(element)).toContain('Contiene letras');
    expect(texto(element)).toContain('Contiene números');
    expect(tildadas()).toBe(0);

    escribir('clubcamion');
    expect(tildadas()).toBe(2);

    escribir('clubcamion7');
    expect(tildadas()).toBe(3);
  });

  // HU-087: el estado de cada requisito no depende solo del color: se dice en texto y cambia el ícono.
  it('cada requisito dice en texto si está cumplido o pendiente', async () => {
    const { fixture, element } = await create('crear');
    const fila = (texto_: string) =>
      Array.from(element.querySelectorAll<HTMLElement>('.requirements li')).find((li) => texto(li).includes(texto_))!;
    const input = element.querySelector<HTMLInputElement>('#password')!;

    expect(texto(fila('Contiene números'))).toContain('Pendiente');
    input.value = 'abc1';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(texto(fila('Contiene números'))).toContain('Cumplido');
    expect(texto(fila('Al menos 10 caracteres'))).toContain('Pendiente');
    expect(fila('Contiene números').querySelector('[data-icono="cumplido"]')).not.toBeNull();
    expect(fila('Al menos 10 caracteres').querySelector('[data-icono="pendiente"]')).not.toBeNull();
  });
});

// HU-109 (corrección de QA del 30/10): los dos ojos se distinguen para un lector de pantalla, y los textos grises de
// los campos llegan a 4.5:1.
describe('AccountAccess - correcciones de QA', () => {
  it('cada ojo dice a qué campo corresponde, al mostrar y al ocultar', async () => {
    const { fixture, element } = await create('crear');
    const ojos = () => Array.from(element.querySelectorAll<HTMLButtonElement>('button[aria-pressed]'));

    expect(ojos().map((o) => o.getAttribute('aria-label'))).toEqual(['Mostrar contraseña', 'Mostrar confirmación de contraseña']);

    ojos().forEach((ojo) => ojo.click());
    fixture.detectChanges();
    expect(ojos().map((o) => o.getAttribute('aria-label'))).toEqual(['Ocultar contraseña', 'Ocultar confirmación de contraseña']);
  });

  it('el placeholder y la etiqueta "recomendado" usan el gris del sistema, que llega a 4.5:1 sobre blanco', async () => {
    const modulo = 'node:fs';
    const fs = await import(/* @vite-ignore */ modulo);
    const sinComentarios = (css: string) => css.replace(/\/\*[\s\S]*?\*\//g, '');
    const css = sinComentarios(fs.readFileSync('src/app/account-access/account-access.css', 'utf8') as string);
    const global = sinComentarios(fs.readFileSync('src/styles.css', 'utf8') as string);

    expect(css).toMatch(/\.form-control::placeholder\s*\{\s*color:\s*var\(--text-muted\);/);
    expect(css).toMatch(/\.requirement-tag\s*\{[^}]*color:\s*var\(--text-muted\);/);
    expect(css).toMatch(/--text-muted:\s*var\(--color-text-subtle\);/);

    const gris = global.match(/--color-text-subtle:\s*(#[0-9a-fA-F]{6});/)![1];
    const luminancia = (hex: string) => {
      const [r, g, b] = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255).map((c) =>
        c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4),
      );
      return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    };
    expect(1.05 / (luminancia(gris) + 0.05)).toBeGreaterThanOrEqual(4.5);
  });
});
