import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { CabeceraJugador, DatosCabeceraJugador } from './cabecera-jugador';

// HU-096: cabecera única de jugador para Perfil y Deuda. Solo presenta lo que le pasa la pantalla: no pide datos a
// la API ni calcula el estado deportivo (lo resuelve el backend).

const JUGADOR: DatosCabeceraJugador = {
  nombre: 'MATIAS',
  apellido: 'PRUEBA',
  dni: '99000003',
  categoria: 'AFA 20067',
  nombreCompleto: 'PRUEBA, MATIAS',
  estadoDeportivo: 'habilitado',
};

@Component({
  standalone: true,
  imports: [CabeceraJugador],
  template: `
    <app-cabecera-jugador [jugador]="jugador" [volverA]="volverA" (volver)="alVolver()">
      <button cabeceraAcciones type="button" class="accion-propia">Acción propia</button>
      <div class="complemento-propio">Datos propios de la pantalla</div>
    </app-cabecera-jugador>
  `,
})
class AnfitrionConAcciones {
  jugador: DatosCabeceraJugador | null = JUGADOR;
  volverA: string | readonly unknown[] | null = null;
  volvio = 0;
  alVolver() {
    this.volvio++;
  }
}

@Component({
  standalone: true,
  imports: [CabeceraJugador],
  template: `<app-cabecera-jugador [jugador]="jugador" [volverA]="['/admin/portal/cuotas-pagos']" />`,
})
class AnfitrionConRuta {
  jugador: DatosCabeceraJugador | null = JUGADOR;
}

const texto = (el: Element | null) => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

async function montar<T>(tipo: new () => T, cambios: Partial<T> = {}) {
  await TestBed.configureTestingModule({ imports: [tipo as never], providers: [provideRouter([])] }).compileComponents();
  const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  const fixture: ComponentFixture<T> = TestBed.createComponent(tipo);
  Object.assign(fixture.componentInstance as object, cambios);
  fixture.detectChanges();
  await fixture.whenStable();
  return { fixture, el: fixture.nativeElement as HTMLElement, host: fixture.componentInstance, navigate };
}

describe('CabeceraJugador (HU-096)', () => {
  describe('datos del jugador', () => {
    it('muestra el nombre completo en el único h1', async () => {
      const { el } = await montar(AnfitrionConAcciones);

      const h1 = el.querySelectorAll('h1');
      expect(h1.length).toBe(1);
      expect(texto(h1[0])).toBe('PRUEBA, MATIAS');
    });

    it('sin nombreCompleto arma "APELLIDO, Nombre", la convención del portal', async () => {
      const { el } = await montar(AnfitrionConAcciones, {
        jugador: { nombre: 'Juan', apellido: 'Pérez', dni: '30111222' },
      });

      expect(texto(el.querySelector('h1'))).toBe('Pérez, Juan');
    });

    it('muestra el DNI tal como viene, sin alterar el valor', async () => {
      const { el } = await montar(AnfitrionConAcciones);

      expect(texto(el.querySelector('.cj-dni'))).toBe('DNI 99000003');
    });

    it('muestra la categoría que recibe', async () => {
      const { el } = await montar(AnfitrionConAcciones);

      expect(texto(el.querySelector('.cj-categoria'))).toBe('AFA 20067');
    });

    it('sin categoría muestra "Sin categoría", nunca null ni undefined', async () => {
      const { el } = await montar(AnfitrionConAcciones, { jugador: { ...JUGADOR, categoria: null } });

      expect(texto(el.querySelector('.cj-categoria'))).toBe('Sin categoría');
      expect(el.textContent).not.toMatch(/undefined|null/);
    });
  });

  describe('estado deportivo', () => {
    const casos: [DatosCabeceraJugador['estadoDeportivo'], string, string][] = [
      ['habilitado', 'Habilitado', 'tono-exito'],
      ['solo_entrenamientos', 'Solo entrenamientos', 'tono-aviso'],
      ['inhabilitado', 'Inhabilitado', 'tono-peligro'],
      ['baja', 'Dado de baja', 'tono-neutro'],
      [null, 'Estado no disponible', 'tono-neutro'],
    ];

    for (const [estado, etiqueta, tono] of casos) {
      it(`"${estado}" se muestra como "${etiqueta}" con la etiqueta compartida (${tono})`, async () => {
        const { el } = await montar(AnfitrionConAcciones, { jugador: { ...JUGADOR, estadoDeportivo: estado } });

        const badge = el.querySelector('.cj-estado')!;
        expect(badge.classList).toContain('etiqueta-estado');
        expect(badge.classList).toContain(tono);
        // El estado se lee en texto (no solo por color), también con lector de pantalla.
        expect(texto(badge)).toContain(etiqueta);
        expect(texto(badge)).toContain('Estado deportivo:');
      });
    }

    it('dado de baja agrega la fecha de la baja, sin correrla por zona horaria', async () => {
      const { el } = await montar(AnfitrionConAcciones, {
        jugador: { ...JUGADOR, estadoDeportivo: 'baja', fechaBaja: '2026-09-30T00:00:00' },
      });

      expect(texto(el.querySelector('.cj-estado'))).toContain('Dado de baja el 30/09/2026');
      expect(el.querySelector('.cj-baja')).toBeNull();
    });

    it('si el estado no está disponible pero hay fecha de baja, la informa aparte', async () => {
      const { el } = await montar(AnfitrionConAcciones, {
        jugador: { ...JUGADOR, estadoDeportivo: null, fechaBaja: '2026-09-30' },
      });

      expect(texto(el.querySelector('.cj-estado'))).toContain('Estado no disponible');
      expect(texto(el.querySelector('.cj-baja'))).toBe('Dado de baja el 30/09/2026');
    });
  });

  describe('foto / iniciales', () => {
    it('sin foto muestra las iniciales de apellido y nombre', async () => {
      const { el } = await montar(AnfitrionConAcciones);

      expect(texto(el.querySelector('.cj-iniciales'))).toBe('PM');
      expect(el.querySelector('img')).toBeNull();
    });

    it('las iniciales ignoran espacios y respetan acentos y ñ', async () => {
      const { el } = await montar(AnfitrionConAcciones, {
        jugador: { nombre: '  ñandú', apellido: ' Álvarez', dni: '1' },
      });

      expect(texto(el.querySelector('.cj-iniciales'))).toBe('ÁÑ');
    });

    it('con foto la muestra con texto alternativo y sin iniciales', async () => {
      const { el } = await montar(AnfitrionConAcciones, { jugador: { ...JUGADOR, fotoUrl: '/fotos/3.jpg' } });

      const img = el.querySelector('img.cj-foto') as HTMLImageElement;
      expect(img.getAttribute('src')).toBe('/fotos/3.jpg');
      expect(img.alt).toBe('Foto de PRUEBA, MATIAS');
      expect(el.querySelector('.cj-iniciales')).toBeNull();
    });

    it('si la foto no carga, vuelve a las iniciales', async () => {
      const { el, fixture } = await montar(AnfitrionConAcciones, { jugador: { ...JUGADOR, fotoUrl: '/rota.jpg' } });

      el.querySelector('img.cj-foto')!.dispatchEvent(new Event('error'));
      fixture.detectChanges();

      expect(el.querySelector('img.cj-foto')).toBeNull();
      expect(texto(el.querySelector('.cj-iniciales'))).toBe('PM');
    });
  });

  describe('Volver', () => {
    it('con (volver) escuchado avisa a la pantalla y no navega por su cuenta', async () => {
      const { el, host, navigate } = await montar(AnfitrionConAcciones, { volverA: ['/otra'] });

      (el.querySelector('.cj-volver') as HTMLButtonElement).click();

      expect(host.volvio).toBe(1);
      expect(navigate).not.toHaveBeenCalled();
    });

    it('con volverA y sin (volver) navega a esa ruta', async () => {
      const { el, navigate } = await montar(AnfitrionConRuta);

      (el.querySelector('.cj-volver') as HTMLButtonElement).click();

      expect(navigate).toHaveBeenCalledWith(['/admin/portal/cuotas-pagos']);
    });

    it('es un botón accesible con texto visible y queda visible mientras la pantalla carga', async () => {
      const { el } = await montar(AnfitrionConAcciones, { jugador: null });

      const volver = el.querySelector('.cj-volver') as HTMLButtonElement;
      expect(volver.tagName).toBe('BUTTON');
      expect(volver.type).toBe('button');
      expect(texto(volver)).toBe('Volver');
      expect(volver.querySelector('svg')!.getAttribute('aria-hidden')).toBe('true');
    });
  });

  describe('acciones y contenido de cada pantalla', () => {
    it('proyecta las acciones propias en el área de acciones', async () => {
      const { el } = await montar(AnfitrionConAcciones);

      expect(el.querySelector('.cj-acciones .accion-propia')).not.toBeNull();
    });

    it('proyecta el contenido propio debajo de los datos, dentro de la tarjeta', async () => {
      const { el } = await montar(AnfitrionConAcciones);

      expect(el.querySelector('.cj-tarjeta .complemento-propio')).not.toBeNull();
    });

    it('el orden de tabulación es Volver y después las acciones', async () => {
      const { el } = await montar(AnfitrionConAcciones);

      const botones = Array.from(el.querySelectorAll('button')).map((b) => b.className);
      expect(botones[0]).toContain('cj-volver');
      expect(botones[1]).toContain('accion-propia');
    });
  });

  describe('carga', () => {
    it('sin datos del jugador no muestra tarjeta, h1 ni iniciales vacías', async () => {
      const { el } = await montar(AnfitrionConAcciones, { jugador: null });

      expect(el.querySelector('.cj-tarjeta')).toBeNull();
      expect(el.querySelector('h1')).toBeNull();
      expect(el.querySelector('.cj-iniciales')).toBeNull();
      expect(el.textContent).not.toMatch(/undefined|null/);
    });
  });
});
