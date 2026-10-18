import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { AvisoJugadoresSinGenero } from './aviso-jugadores-sin-genero';
import { JugadoresSinGeneroService } from '../../../services/jugadores-sin-genero';
import { JugadoresSinGeneroModel } from '../../../models/JugadoresSinGeneroModel';

const DOS: JugadoresSinGeneroModel = {
  total: 2,
  jugadores: [
    { idJugador: 1, nombreCompleto: 'Pérez, Juan', dni: '41000001', categoria: 'Pt  AFA 2009' },
    { idJugador: 2, nombreCompleto: 'Gómez, Ana', dni: '42000002', categoria: '' },
  ],
};

async function create(respuesta: JugadoresSinGeneroModel | 'error' = DOS) {
  await TestBed.configureTestingModule({
    imports: [AvisoJugadoresSinGenero],
    providers: [
      {
        provide: JugadoresSinGeneroService,
        useValue: { get: () => (respuesta === 'error' ? throwError(() => new Error('sin conexión')) : of(respuesta)) },
      },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(AvisoJugadoresSinGenero);
  fixture.detectChanges();
  return { fixture, component: fixture.componentInstance, element: fixture.nativeElement as HTMLElement };
}

const texto = (element: HTMLElement, selector: string) =>
  element.querySelector(selector)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

describe('AvisoJugadoresSinGenero', () => {
  it('no muestra nada si todos los jugadores tienen género', async () => {
    const { element } = await create({ total: 0, jugadores: [] });

    expect(element.querySelector('.aviso')).toBeNull();
  });

  it('no muestra nada (ni rompe el Resumen General) si no se puede consultar', async () => {
    const { element } = await create('error');

    expect(element.querySelector('.aviso')).toBeNull();
  });

  it('resume en una línea cuántos jugadores faltan', async () => {
    const { element } = await create();

    const resumen = texto(element, '.aviso-resumen');
    expect(resumen).toContain('2 jugadores sin género cargado');
    expect(resumen).toContain('no se les puede emitir la cuota');
  });

  it('con un solo jugador habla en singular', async () => {
    const { element } = await create({ total: 1, jugadores: [DOS.jugadores[0]] });

    const resumen = texto(element, '.aviso-resumen');
    expect(resumen).toContain('1 jugador sin género cargado');
    expect(resumen).toContain('no se le puede emitir la cuota');
  });

  it('el listado arranca plegado y "Revisar" lo despliega con nombre, DNI y categoría', async () => {
    const { fixture, element } = await create();
    expect(element.querySelector('.aviso-despliegue')).toBeNull();

    (element.querySelector('.aviso-revisar') as HTMLButtonElement).click();
    fixture.detectChanges();

    const filas = Array.from(element.querySelectorAll('.aviso-item')).map((f) => [
      texto(f as HTMLElement, '.aviso-item-nombre'),
      texto(f as HTMLElement, '.aviso-item-meta'),
    ]);
    expect(filas).toEqual([
      ['Pérez, Juan', 'DNI 41000001 · Pt AFA 2009'],
      ['Gómez, Ana', 'DNI 42000002 · Sin categoría'],
    ]);
    expect(texto(element, '.aviso-revisar')).toContain('Ocultar');
  });

  it('avisa cuántos jugadores no entran en la lista cuando son muchos', async () => {
    const { fixture, element, component } = await create({ total: 250, jugadores: DOS.jugadores });

    expect(component.sinMostrar).toBe(248);
    (element.querySelector('.aviso-revisar') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(texto(element, '.aviso-mas')).toContain('248 más');
  });

  it('el texto no culpa a nadie: indica dónde se completa el dato', async () => {
    const { fixture, element } = await create();
    (element.querySelector('.aviso-revisar') as HTMLButtonElement).click();
    fixture.detectChanges();

    const explicacion = texto(element, '.aviso-texto');
    expect(explicacion).toContain('Portal Deportivo');
    expect(explicacion.toLowerCase()).not.toContain('error');
    expect(explicacion.toLowerCase()).not.toContain('mal');
  });
});
