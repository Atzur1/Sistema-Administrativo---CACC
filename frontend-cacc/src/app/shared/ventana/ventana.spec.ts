import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { Ventana } from './ventana';

// Pantalla de prueba: un botón abre la ventana, como en las pantallas reales.
@Component({
  standalone: true,
  imports: [Ventana],
  template: `
    <button type="button" class="abrir" (click)="abierta.set(true)">Abrir</button>
    @if (abierta()) {
      <app-ventana
        titulo="Deshabilitar acceso"
        subtitulo="Ana Pérez"
        textoConfirmar="Deshabilitar"
        textoEnCurso="Deshabilitando…"
        [peligro]="peligro"
        [enCurso]="enCurso()"
        [error]="error()"
        (confirmar)="confirmaciones = confirmaciones + 1"
        (cerrar)="abierta.set(false)"
      >
        <p class="contenido">Corta el acceso de esta persona.</p>
      </app-ventana>
    }
  `,
})
class PantallaDePrueba {
  readonly abierta = signal(false);
  readonly enCurso = signal(false);
  readonly error = signal<string | null>(null);
  peligro = true;
  confirmaciones = 0;
}

async function crear(): Promise<ComponentFixture<PantallaDePrueba>> {
  await TestBed.configureTestingModule({ imports: [PantallaDePrueba] }).compileComponents();
  const fixture = TestBed.createComponent(PantallaDePrueba);
  fixture.autoDetectChanges();
  return fixture;
}

function abrir(fixture: ComponentFixture<PantallaDePrueba>): HTMLButtonElement {
  const boton = fixture.nativeElement.querySelector('.abrir') as HTMLButtonElement;
  boton.focus();
  boton.click();
  fixture.detectChanges();
  return boton;
}

const ventana = (fixture: ComponentFixture<PantallaDePrueba>) =>
  fixture.nativeElement.querySelector('[role="dialog"]') as HTMLElement | null;
const boton = (fixture: ComponentFixture<PantallaDePrueba>, selector: string) =>
  fixture.nativeElement.querySelector(selector) as HTMLButtonElement;
const escape = () => document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));

describe('Ventana', () => {
  afterEach(() => {
    document.body.querySelectorAll('.cs-menu').forEach((menu) => menu.remove());
    TestBed.resetTestingModule();
  });

  it('es un diálogo modal con el título como etiqueta y el subtítulo como descripción', async () => {
    const fixture = await crear();
    abrir(fixture);

    const dialogo = ventana(fixture)!;
    expect(dialogo.getAttribute('aria-modal')).toBe('true');
    const titulo = document.getElementById(dialogo.getAttribute('aria-labelledby')!);
    expect(titulo?.textContent?.trim()).toBe('Deshabilitar acceso');
    const subtitulo = document.getElementById(dialogo.getAttribute('aria-describedby')!);
    expect(subtitulo?.textContent?.trim()).toBe('Ana Pérez');
    expect(dialogo.querySelector('.contenido')?.textContent).toContain('Corta el acceso');
  });

  it('muestra la acción de riesgo con el tono de peligro', async () => {
    const fixture = await crear();
    abrir(fixture);

    expect(boton(fixture, '.ventana-confirmar').classList).toContain('es-peligro');
    expect(ventana(fixture)!.classList).toContain('ventana-peligro');
  });

  it('se cierra con Esc, con la cruz, con Volver y al tocar el fondo', async () => {
    const fixture = await crear();

    abrir(fixture);
    escape();
    fixture.detectChanges();
    expect(ventana(fixture)).toBeNull();

    abrir(fixture);
    boton(fixture, '.ventana-cerrar').click();
    fixture.detectChanges();
    expect(ventana(fixture)).toBeNull();

    abrir(fixture);
    boton(fixture, '.ventana-volver').click();
    fixture.detectChanges();
    expect(ventana(fixture)).toBeNull();

    abrir(fixture);
    (fixture.nativeElement.querySelector('.ventana-fondo') as HTMLElement).click();
    fixture.detectChanges();
    expect(ventana(fixture)).toBeNull();
  });

  it('un clic dentro de la ventana no la cierra', async () => {
    const fixture = await crear();
    abrir(fixture);

    (fixture.nativeElement.querySelector('.contenido') as HTMLElement).click();
    fixture.detectChanges();

    expect(ventana(fixture)).not.toBeNull();
  });

  it('no se cierra ni se puede confirmar dos veces mientras la operación está en curso', async () => {
    const fixture = await crear();
    abrir(fixture);
    fixture.componentInstance.enCurso.set(true);
    fixture.detectChanges();

    escape();
    boton(fixture, '.ventana-cerrar').click();
    (fixture.nativeElement.querySelector('.ventana-fondo') as HTMLElement).click();
    boton(fixture, '.ventana-confirmar').click();
    fixture.detectChanges();

    expect(ventana(fixture)).not.toBeNull();
    expect(fixture.componentInstance.confirmaciones).toBe(0);
    const confirmar = boton(fixture, '.ventana-confirmar');
    expect(confirmar.disabled).toBe(true);
    expect(confirmar.classList).toContain('en-curso');
    expect(confirmar.textContent?.trim()).toBe('Deshabilitando…');
  });

  it('confirma una sola vez por clic', async () => {
    const fixture = await crear();
    abrir(fixture);

    boton(fixture, '.ventana-confirmar').click();

    expect(fixture.componentInstance.confirmaciones).toBe(1);
  });

  it('muestra el mensaje de error como alerta', async () => {
    const fixture = await crear();
    abrir(fixture);
    fixture.componentInstance.error.set('No se pudo deshabilitar.');
    fixture.detectChanges();

    const error = fixture.nativeElement.querySelector('.ventana-error') as HTMLElement;
    expect(error.getAttribute('role')).toBe('alert');
    expect(error.textContent?.trim()).toBe('No se pudo deshabilitar.');
  });

  it('con un desplegable abierto, el primer Esc cierra el desplegable y no la ventana', async () => {
    const fixture = await crear();
    abrir(fixture);
    const menu = document.createElement('div');
    menu.className = 'cs-menu';
    document.body.appendChild(menu);

    escape();
    fixture.detectChanges();

    expect(ventana(fixture)).not.toBeNull();
  });

  it('al cerrarse devuelve el foco al botón que la abrió', async () => {
    const fixture = await crear();
    const abridor = abrir(fixture);
    await fixture.whenStable();

    boton(fixture, '.ventana-cerrar').click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(document.activeElement).toBe(abridor);
  });
});

// Pantalla de prueba con un menú "⋯": la opción que abre la ventana desaparece al cerrarse el menú,
// como en Usuarios y Permisos. La ventana recibe el botón del menú como lugar de vuelta del foco.
@Component({
  standalone: true,
  imports: [Ventana],
  template: `
    <button #botonMenu type="button" class="menu" (click)="menuAbierto.set(true)">⋯</button>
    @if (menuAbierto()) {
      <button type="button" class="opcion" (click)="menuAbierto.set(false); abierta.set(true)">Deshabilitar</button>
    }
    @if (abierta()) {
      <app-ventana titulo="Deshabilitar acceso" [focoAlCerrar]="botonMenu" (cerrar)="abierta.set(false)">
        <p>Corta el acceso de esta persona.</p>
      </app-ventana>
    }
  `,
})
class PantallaConMenu {
  readonly menuAbierto = signal(false);
  readonly abierta = signal(false);
}

describe('Ventana abierta desde un menú', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('al cerrarse devuelve el foco al botón del menú, porque la opción ya no existe', async () => {
    await TestBed.configureTestingModule({ imports: [PantallaConMenu] }).compileComponents();
    const fixture = TestBed.createComponent(PantallaConMenu);
    fixture.autoDetectChanges();
    const el = fixture.nativeElement as HTMLElement;

    el.querySelector<HTMLButtonElement>('.menu')!.click();
    fixture.detectChanges();
    const opcion = el.querySelector<HTMLButtonElement>('.opcion')!;
    opcion.focus();
    opcion.click();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(el.querySelector('.opcion')).toBeNull();

    el.querySelector<HTMLButtonElement>('.ventana-cerrar')!.click();
    fixture.detectChanges();
    await new Promise((resolver) => setTimeout(resolver));

    expect(document.activeElement).toBe(el.querySelector('.menu'));
  });
});
