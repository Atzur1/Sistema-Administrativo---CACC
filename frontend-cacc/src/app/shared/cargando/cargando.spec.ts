import { ComponentFixture, TestBed } from '@angular/core/testing';

import { Cargando } from './cargando';

async function crear(
  forma: Cargando['forma'],
  filas = 3,
  columnas = 4,
): Promise<ComponentFixture<Cargando>> {
  await TestBed.configureTestingModule({ imports: [Cargando] }).compileComponents();
  const fixture = TestBed.createComponent(Cargando);
  fixture.componentRef.setInput('texto', 'Cargando cobros pendientes…');
  fixture.componentRef.setInput('forma', forma);
  fixture.componentRef.setInput('filas', filas);
  fixture.componentRef.setInput('columnas', columnas);
  fixture.detectChanges();
  return fixture;
}

describe('Cargando', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('anuncia qué está cargando a los lectores de pantalla', async () => {
    const fixture = await crear('lista');
    const el = fixture.nativeElement as HTMLElement;

    const estado = el.querySelector('[role="status"]')!;
    expect(estado.getAttribute('aria-live')).toBe('polite');
    expect(estado.textContent?.trim()).toBe('Cargando cobros pendientes…');
    // Los bloques son decorativos: no los lee el lector de pantalla.
    expect(el.querySelector('.cargando-bloques')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('en forma de tabla dibuja las filas y columnas pedidas', async () => {
    const fixture = await crear('tabla', 5, 3);
    const filas = (fixture.nativeElement as HTMLElement).querySelectorAll('.cargando-fila-tabla');

    expect(filas).toHaveLength(5);
    expect(filas[0].querySelectorAll('.bloque')).toHaveLength(3);
  });

  it('en forma de tarjetas dibuja una caja por tarjeta', async () => {
    const fixture = await crear('tarjetas', 4);

    expect((fixture.nativeElement as HTMLElement).querySelectorAll('.cargando-tarjeta')).toHaveLength(4);
  });

  it('en forma de lista dibuja un círculo y dos líneas por fila', async () => {
    const fixture = await crear('lista', 2);
    const filas = (fixture.nativeElement as HTMLElement).querySelectorAll('.cargando-fila-lista');

    expect(filas).toHaveLength(2);
    expect(filas[0].querySelector('.bloque-redondo')).not.toBeNull();
    expect(filas[0].querySelectorAll('.cargando-textos .bloque')).toHaveLength(2);
  });
});
