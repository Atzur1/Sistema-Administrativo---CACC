import { ChangeDetectorRef, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { ExportFormat, ExportMenu } from './export-menu';

// HU-097: el menú "Exportar Reporte" compartido.
@Component({
  standalone: true,
  imports: [ExportMenu],
  template: `
    <app-export-menu [enCurso]="enCurso" [deshabilitado]="deshabilitado" (exportar)="elegidos.push($event)" />
    <button class="afuera">Afuera</button>
  `,
})
class Host {
  enCurso = false;
  deshabilitado = false;
  elegidos: ExportFormat[] = [];
}

async function crear() {
  await TestBed.configureTestingModule({ imports: [Host] }).compileComponents();
  const fixture = TestBed.createComponent(Host);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const refrescar = () => {
    fixture.componentRef.injector.get(ChangeDetectorRef).markForCheck();
    fixture.detectChanges();
  };
  const boton = () => el.querySelector<HTMLButtonElement>('.export-menu-trigger')!;
  const opciones = () => Array.from(el.querySelectorAll<HTMLButtonElement>('.export-menu-option'));
  return { fixture, host: fixture.componentInstance, el, refrescar, boton, opciones };
}

describe('ExportMenu', () => {
  it('is a single "Exportar Reporte" button with the unified PDF and CSV options', async () => {
    const { boton, opciones, refrescar } = await crear();
    expect(boton().textContent!.trim()).toBe('Exportar Reporte');
    expect(opciones()).toHaveLength(0);

    boton().click();
    refrescar();

    expect(opciones().map((o) => o.querySelector('.export-menu-label')!.textContent!.trim())).toEqual([
      'Exportar a PDF', 'Exportar a CSV',
    ]);
    expect(boton().getAttribute('aria-expanded')).toBe('true');
  });

  it('choosing an option emits the format and closes the menu', async () => {
    const { host, boton, opciones, refrescar } = await crear();
    boton().click();
    refrescar();

    opciones()[1].click();
    refrescar();

    expect(host.elegidos).toEqual(['csv']);
    expect(opciones()).toHaveLength(0);
  });

  it('closes when clicking outside or pressing Esc', async () => {
    const { el, boton, opciones, refrescar } = await crear();

    boton().click();
    refrescar();
    el.querySelector<HTMLButtonElement>('.afuera')!.click();
    refrescar();
    expect(opciones()).toHaveLength(0);

    boton().click();
    refrescar();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    refrescar();
    expect(opciones()).toHaveLength(0);
  });

  it('while exporting it says "Generando…" and cannot be opened', async () => {
    const { host, boton, opciones, refrescar } = await crear();
    host.enCurso = true;
    refrescar();

    expect(boton().textContent!.trim()).toBe('Generando…');
    expect(boton().disabled).toBe(true);
    boton().click();
    refrescar();
    expect(opciones()).toHaveLength(0);
  });

  it('can be disabled while another menu of the same screen exports', async () => {
    const { host, boton, refrescar } = await crear();
    host.deshabilitado = true;
    refrescar();

    expect(boton().disabled).toBe(true);
    expect(boton().textContent!.trim()).toBe('Exportar Reporte');
  });
});
