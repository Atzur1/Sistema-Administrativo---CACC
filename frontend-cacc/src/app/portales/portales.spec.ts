import { ComponentFixture, TestBed } from '@angular/core/testing';

import { Portales } from './portales';

describe('Portales', () => {
  let component: Portales;
  let fixture: ComponentFixture<Portales>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Portales],
    }).compileComponents();

    fixture = TestBed.createComponent(Portales);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

// HU-109 (corrección de QA): un <button> no hereda la letra de la pantalla; sin declararla, los botones de Portales
// salían en Arial. Se lee portales.css tal como está en el archivo.
describe('Portales - letra de los botones', () => {
  it('"Ingresar al Portal…" y "Cerrar sesión" heredan la letra de la pantalla', async () => {
    const modulo = 'node:fs';
    const fs = await import(/* @vite-ignore */ modulo);
    const css = (fs.readFileSync('src/app/portales/portales.css', 'utf8') as string).replace(/\/\*[\s\S]*?\*\//g, '');

    for (const clase of ['btn-portal', 'btn-logout']) {
      const regla = css.match(new RegExp(String.raw`\.${clase}\s*\{([^}]*)\}`));
      expect(regla, `.${clase}`).not.toBeNull();
      expect(regla![1]).toMatch(/font-family:\s*inherit/);
    }
  });
});
