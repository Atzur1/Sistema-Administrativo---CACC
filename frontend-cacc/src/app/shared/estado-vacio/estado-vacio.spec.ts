import { ComponentFixture, TestBed } from '@angular/core/testing';

import { EstadoVacio } from './estado-vacio';

async function crear(entradas: Partial<Pick<EstadoVacio, 'mensaje' | 'detalle' | 'icono' | 'textoAccion'>>): Promise<ComponentFixture<EstadoVacio>> {
  await TestBed.configureTestingModule({ imports: [EstadoVacio] }).compileComponents();
  const fixture = TestBed.createComponent(EstadoVacio);
  fixture.componentRef.setInput('mensaje', entradas.mensaje ?? 'No hay cobros pendientes');
  for (const [clave, valor] of Object.entries(entradas)) {
    if (clave !== 'mensaje') fixture.componentRef.setInput(clave, valor);
  }
  fixture.detectChanges();
  return fixture;
}

describe('EstadoVacio', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('muestra el mensaje y se anuncia a los lectores de pantalla', async () => {
    const fixture = await crear({ mensaje: 'No hay cobros pendientes' });
    const el = fixture.nativeElement as HTMLElement;

    const estado = el.querySelector('[role="status"]')!;
    expect(estado.getAttribute('aria-live')).toBe('polite');
    expect(el.querySelector('.vacio-mensaje')?.textContent?.trim()).toBe('No hay cobros pendientes');
    expect(el.querySelector('.vacio-icono svg')).not.toBeNull();
  });

  it('sin acción ni aclaración no muestra botón ni segunda línea', async () => {
    const fixture = await crear({});
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('.vacio-accion')).toBeNull();
    expect(el.querySelector('.vacio-detalle')).toBeNull();
  });

  it('con acción muestra el botón y avisa al tocarlo', async () => {
    const fixture = await crear({
      icono: 'busqueda',
      mensaje: 'Ningún resultado coincide con la búsqueda o los filtros',
      detalle: 'Probá con otro nombre o DNI.',
      textoAccion: 'Limpiar filtros',
    });
    const el = fixture.nativeElement as HTMLElement;
    const avisos = vi.fn();
    fixture.componentInstance.accion.subscribe(avisos);

    const boton = el.querySelector<HTMLButtonElement>('.vacio-accion')!;
    expect(boton.textContent?.trim()).toBe('Limpiar filtros');
    expect(boton.classList).toContain('boton-fantasma');
    expect(el.querySelector('.vacio-detalle')?.textContent?.trim()).toBe('Probá con otro nombre o DNI.');

    boton.click();
    expect(avisos).toHaveBeenCalledTimes(1);
  });

  it('el estado "listo" (nada pendiente) usa el ícono en verde', async () => {
    const fixture = await crear({ icono: 'listo', mensaje: 'Este jugador no tiene cuotas pendientes' });

    expect((fixture.nativeElement as HTMLElement).querySelector('.vacio-icono-listo')).not.toBeNull();
  });
});
