import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { from } from 'rxjs';

import { JugadorPerfil } from './jugador-perfil';
import { HistorialPagosResultado, JugadoresService, PagoHistorialItem } from '../../services/jugadores';
import { JugadorResumen } from '../../services/pagos';

// HU-017: historial de pagos dentro del perfil del jugador. El servicio HTTP se reemplaza por un
// doble de prueba, así que estas pruebas no necesitan la API ni la base. La consulta real (agrupado
// por período y orden del pago más reciente al más antiguo) se cubre en la colección de Postman
// "HU-017 - Historial de Pagos por Jugador".

const JUGADOR: JugadorResumen = {
  idJugador: 3,
  nombre: 'MATIAS',
  apellido: 'PRUEBA',
  dni: '99000003',
  genero: 'Masculino',
  categoria: 'AFA 20067',
  nombreCompleto: 'PRUEBA, MATIAS',
};

function periodo(nombre: string, monto: number, extra: Partial<PagoHistorialItem> = {}): PagoHistorialItem {
  return {
    periodo: nombre,
    montoTotal: monto,
    montoOriginal: monto,
    tieneBeneficio: false,
    motivoBeneficio: null,
    tipoValorBeneficio: null,
    porcentajeBeneficio: null,
    montoFijoBeneficio: null,
    abonos: [{ monto, metodoPago: 'Transferencia', fechaPago: '2026-09-19T00:00:00' }],
    ...extra,
  };
}

function historial(items: PagoHistorialItem[], total = items.length, page = 1): HistorialPagosResultado {
  return { items, total, page, pageSize: 10 };
}

// Doce nombres de período distintos, para armar historiales de más de una página.
const MESES = ['Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio', 'Julio', 'Agosto', 'Septiembre', 'Octubre'];
const DIEZ_PERIODOS = MESES.map((mes) => periodo(`${mes} 2026`, 85000));

interface Opciones {
  id?: string | null;
  jugador?: JugadorResumen | 'error';
  historialPorPagina?: (page: number) => HistorialPagosResultado | 'error';
}

// Las respuestas llegan de forma asíncrona (como en la app real): el componente llama a
// detectChanges() dentro de los callbacks y no admite que corran de forma síncrona en ngOnInit.
const respuesta = <T>(valor: T | 'error') => from(valor === 'error' ? Promise.reject(new Error('fallo')) : Promise.resolve(valor));

async function crear(opciones: Opciones = {}) {
  const servicio = {
    getJugador: vi.fn(() => respuesta(opciones.jugador ?? JUGADOR)),
    getHistorialPagos: vi.fn((_id: number, page: number) =>
      respuesta((opciones.historialPorPagina ?? (() => historial([])))(page))
    ),
  };

  await TestBed.configureTestingModule({
    imports: [JugadorPerfil],
    providers: [
      provideRouter([]),
      { provide: JugadoresService, useValue: servicio },
      {
        provide: ActivatedRoute,
        useValue: { snapshot: { paramMap: convertToParamMap(opciones.id === null ? {} : { id: opciones.id ?? '3' }) } },
      },
    ],
  }).compileComponents();

  const router = TestBed.inject(Router);
  const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

  const fixture: ComponentFixture<JugadorPerfil> = TestBed.createComponent(JugadorPerfil);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();

  const el = fixture.nativeElement as HTMLElement;
  return { fixture, component: fixture.componentInstance, servicio, navigate, el };
}

const texto = (el: Element | null) => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

describe('JugadorPerfil - historial de pagos', () => {
  describe('carga', () => {
    it('pide el jugador y la primera página de 10 períodos usando el id de la ruta', async () => {
      const { servicio } = await crear({ id: '3' });

      expect(servicio.getJugador).toHaveBeenCalledWith(3);
      expect(servicio.getHistorialPagos).toHaveBeenCalledWith(3, 1, 10);
    });

    it('muestra los datos básicos del jugador en el encabezado', async () => {
      const { el } = await crear();

      expect(texto(el.querySelector('.profile-name'))).toBe('PRUEBA, MATIAS');
      expect(texto(el.querySelector('.profile-meta'))).toBe('DNI 99000003 · AFA 20067');
      expect(texto(el.querySelector('.profile-avatar'))).toBe('PM');
    });

    it('muestra la sección "Historial de Pagos"', async () => {
      const { el } = await crear();

      expect(texto(el.querySelector('.panel-title'))).toBe('Historial de Pagos');
    });

    it('vuelve a Cuotas y Pagos si el id de la ruta no es un número', async () => {
      const { servicio, navigate } = await crear({ id: 'abc' });

      expect(navigate).toHaveBeenCalledWith(['/admin/portal/cuotas-pagos']);
      expect(servicio.getJugador).not.toHaveBeenCalled();
      expect(servicio.getHistorialPagos).not.toHaveBeenCalled();
    });

    it('vuelve a Cuotas y Pagos si la ruta no trae id', async () => {
      const { navigate } = await crear({ id: null });

      expect(navigate).toHaveBeenCalledWith(['/admin/portal/cuotas-pagos']);
    });

    it('avisa si no se encuentra el jugador', async () => {
      const { el } = await crear({ jugador: 'error' });

      expect(texto(el.querySelector('.error-text'))).toBe('No se encontró el jugador solicitado.');
    });

    it('guarda un mensaje de error si falla la consulta del historial', async () => {
      const { component } = await crear({ historialPorPagina: () => 'error' });

      expect(component.errorMessage).toBe('No se pudo cargar el historial de pagos.');
      expect(component.cargandoHistorial).toBe(false);
    });

    it('el botón Volver regresa a Cuotas y Pagos', async () => {
      const { el, navigate } = await crear();

      (el.querySelector('.back-button') as HTMLButtonElement).click();

      expect(navigate).toHaveBeenCalledWith(['/admin/portal/cuotas-pagos']);
    });
  });

  describe('estado vacío', () => {
    it('muestra el mensaje claro cuando el jugador no tiene pagos', async () => {
      const { el } = await crear({ historialPorPagina: () => historial([]) });

      expect(texto(el.querySelector('.historial-empty'))).toBe('No se registran pagos realizados para este jugador.');
      expect(el.querySelectorAll('.periodo-card').length).toBe(0);
    });

    it('no muestra la paginación', async () => {
      const { el } = await crear({ historialPorPagina: () => historial([]) });

      expect(el.querySelector('.pagination')).toBeNull();
    });

    it('cuenta 0 pagos en el encabezado de la sección', async () => {
      const { el } = await crear({ historialPorPagina: () => historial([]) });

      expect(texto(el.querySelector('.panel-count'))).toBe('0');
    });
  });

  describe('grilla de períodos', () => {
    it('lista cada período con su monto total', async () => {
      const { el } = await crear({
        historialPorPagina: () => historial([periodo('Septiembre 2026', 85000), periodo('Agosto 2026', 92000)]),
      });

      const tarjetas = Array.from(el.querySelectorAll('.periodo-card'));
      expect(tarjetas.length).toBe(2);
      expect(texto(tarjetas[0].querySelector('.periodo-nombre'))).toBe('Septiembre 2026');
      expect(texto(tarjetas[0].querySelector('.periodo-total'))).toMatch(/\$\s?85\.000/);
      expect(texto(tarjetas[1].querySelector('.periodo-nombre'))).toBe('Agosto 2026');
      expect(texto(tarjetas[1].querySelector('.periodo-total'))).toMatch(/\$\s?92\.000/);
    });

    it('mantiene el orden que devuelve la API (más reciente primero)', async () => {
      const { el } = await crear({
        historialPorPagina: () => historial([periodo('Septiembre 2026', 1), periodo('Agosto 2026', 1), periodo('Julio 2026', 1)]),
      });

      const nombres = Array.from(el.querySelectorAll('.periodo-nombre')).map((n) => texto(n));
      expect(nombres).toEqual(['Septiembre 2026', 'Agosto 2026', 'Julio 2026']);
    });

    it('muestra el método de pago y la fecha del cobro con formato dd/mm/aaaa', async () => {
      const { el } = await crear({ historialPorPagina: () => historial([periodo('Septiembre 2026', 85000)]) });

      expect(texto(el.querySelector('.abono-meta'))).toBe('Transferencia · 19/09/2026');
      expect(texto(el.querySelector('.abono-monto'))).toMatch(/\$\s?85\.000/);
    });

    it('lista todos los abonos de un período pagado en partes', async () => {
      const enPartes = periodo('Enero 2026', 70000, {
        abonos: [
          { monto: 20000, metodoPago: 'Efectivo', fechaPago: '2026-01-10T00:00:00' },
          { monto: 50000, metodoPago: 'Transferencia', fechaPago: '2026-01-25T00:00:00' },
        ],
      });
      const { el } = await crear({ historialPorPagina: () => historial([enPartes]) });

      const abonos = Array.from(el.querySelectorAll('.abono-row'));
      expect(abonos.length).toBe(2);
      expect(texto(abonos[0].querySelector('.abono-meta'))).toBe('Efectivo · 10/01/2026');
      expect(texto(abonos[0].querySelector('.abono-monto'))).toMatch(/\$\s?20\.000/);
      expect(texto(abonos[1].querySelector('.abono-meta'))).toBe('Transferencia · 25/01/2026');
      expect(texto(abonos[1].querySelector('.abono-monto'))).toMatch(/\$\s?50\.000/);
    });

    it('cuenta los períodos totales en el encabezado de la sección', async () => {
      const { el } = await crear({ historialPorPagina: () => historial(DIEZ_PERIODOS, 23) });

      expect(texto(el.querySelector('.panel-count'))).toBe('23');
    });

    it('muestra el beneficio aplicado solo en los períodos que lo tuvieron', async () => {
      const conBeca = periodo('Agosto 2026', 45000, {
        montoOriginal: 90000,
        tieneBeneficio: true,
        motivoBeneficio: 'Media Beca',
        tipoValorBeneficio: '%',
        porcentajeBeneficio: 50,
      });
      const { el } = await crear({ historialPorPagina: () => historial([periodo('Septiembre 2026', 85000), conBeca]) });

      const tarjetas = Array.from(el.querySelectorAll('.periodo-card'));
      expect(tarjetas[0].querySelector('.periodo-beneficio')).toBeNull();
      expect(texto(tarjetas[1].querySelector('.periodo-beneficio-badge'))).toContain('Media Beca (50%)');
      expect(texto(tarjetas[1].querySelector('.periodo-cuota-original'))).toMatch(/Cuota: \$\s?90\.000/);
    });
  });

  describe('paginación', () => {
    it('no muestra la paginación con 10 períodos o menos', async () => {
      const { el } = await crear({ historialPorPagina: () => historial(DIEZ_PERIODOS, 10) });

      expect(el.querySelector('.pagination')).toBeNull();
    });

    it('muestra la paginación cuando hay más de 10 períodos', async () => {
      const { el } = await crear({ historialPorPagina: () => historial(DIEZ_PERIODOS, 11) });

      expect(texto(el.querySelector('.pagination-status'))).toBe('Página 1 de 2');
    });

    it('en la primera página deshabilita Anterior y habilita Siguiente', async () => {
      const { el } = await crear({ historialPorPagina: () => historial(DIEZ_PERIODOS, 25) });

      const [anterior, siguiente] = Array.from(el.querySelectorAll<HTMLButtonElement>('.pagination-button'));
      expect(anterior.disabled).toBe(true);
      expect(siguiente.disabled).toBe(false);
    });

    it('Siguiente pide la página 2 del mismo jugador', async () => {
      const { el, fixture, servicio } = await crear({
        historialPorPagina: (page) => historial(DIEZ_PERIODOS, 25, page),
      });

      el.querySelectorAll<HTMLButtonElement>('.pagination-button')[1].click();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(servicio.getHistorialPagos).toHaveBeenLastCalledWith(3, 2, 10);
      expect(texto(el.querySelector('.pagination-status'))).toBe('Página 2 de 3');
    });

    it('en la última página deshabilita Siguiente y habilita Anterior', async () => {
      const { el, fixture } = await crear({ historialPorPagina: (page) => historial(DIEZ_PERIODOS, 25, page) });

      for (let i = 0; i < 2; i++) {
        el.querySelectorAll<HTMLButtonElement>('.pagination-button')[1].click();
        await fixture.whenStable();
        fixture.detectChanges();
      }

      const [anterior, siguiente] = Array.from(el.querySelectorAll<HTMLButtonElement>('.pagination-button'));
      expect(texto(el.querySelector('.pagination-status'))).toBe('Página 3 de 3');
      expect(siguiente.disabled).toBe(true);
      expect(anterior.disabled).toBe(false);
    });

    it('Anterior vuelve a la página previa', async () => {
      const { el, fixture, servicio } = await crear({ historialPorPagina: (page) => historial(DIEZ_PERIODOS, 25, page) });

      el.querySelectorAll<HTMLButtonElement>('.pagination-button')[1].click();
      await fixture.whenStable();
      fixture.detectChanges();
      el.querySelectorAll<HTMLButtonElement>('.pagination-button')[0].click();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(servicio.getHistorialPagos).toHaveBeenLastCalledWith(3, 1, 10);
      expect(texto(el.querySelector('.pagination-status'))).toBe('Página 1 de 3');
    });

    it('irAPagina ignora páginas fuera de rango y la página actual', async () => {
      const { component, servicio } = await crear({ historialPorPagina: (page) => historial(DIEZ_PERIODOS, 25, page) });
      const llamadasIniciales = servicio.getHistorialPagos.mock.calls.length;

      component.irAPagina(0);
      component.irAPagina(4);
      component.irAPagina(1);

      expect(servicio.getHistorialPagos.mock.calls.length).toBe(llamadasIniciales);
      expect(component.page).toBe(1);
    });

    it.each([
      [0, 1],
      [1, 1],
      [10, 1],
      [11, 2],
      [20, 2],
      [21, 3],
      [100, 10],
    ])('con %i períodos hay %i página(s)', async (total, paginas) => {
      const { component } = await crear({ historialPorPagina: () => historial(DIEZ_PERIODOS, total) });

      expect(component.totalPaginas).toBe(paginas);
    });
  });

  describe('formato', () => {
    it('da formato a los montos en moneda local (pesos argentinos)', async () => {
      const { component } = await crear();

      expect(component.formatMonto(85000)).toMatch(/^\$\s?85\.000$/);
      expect(component.formatMonto(1250000)).toMatch(/^\$\s?1\.250\.000$/);
    });

    it('da formato a la fecha como día/mes/año', async () => {
      const { component } = await crear();

      expect(component.formatFecha('2026-09-19T00:00:00')).toBe('19/09/2026');
      expect(component.formatFecha('2026-01-05T00:00:00')).toBe('05/01/2026');
    });

    it('arma el texto del beneficio por porcentaje', async () => {
      const { component } = await crear();

      const texto = component.beneficioTexto(
        periodo('Agosto 2026', 1, { tieneBeneficio: true, motivoBeneficio: 'Descuento por Hermanos', tipoValorBeneficio: '%', porcentajeBeneficio: 20 })
      );

      expect(texto).toBe('Descuento por Hermanos (20%)');
    });

    it('arma el texto del beneficio por monto fijo', async () => {
      const { component } = await crear();

      const texto = component.beneficioTexto(
        periodo('Agosto 2026', 1, { tieneBeneficio: true, motivoBeneficio: 'Media Beca', tipoValorBeneficio: '$', montoFijoBeneficio: 5000 })
      );

      expect(texto).toMatch(/^Media Beca \(\$\s?5\.000\)$/);
    });

    it('no arma texto para un período sin beneficio', async () => {
      const { component } = await crear();

      expect(component.beneficioTexto(periodo('Agosto 2026', 1))).toBe('');
    });
  });

  describe('solo lectura', () => {
    it('no ofrece campos de edición sobre importes ni fechas', async () => {
      const { el } = await crear({ historialPorPagina: () => historial([periodo('Septiembre 2026', 85000)]) });

      expect(el.querySelectorAll('input, textarea, select, [contenteditable]').length).toBe(0);
    });

    it('los únicos botones son Volver y la paginación', async () => {
      const { el } = await crear({ historialPorPagina: () => historial(DIEZ_PERIODOS, 25) });

      const clases = Array.from(el.querySelectorAll('button')).map((b) => b.className.split(' ')[0]);
      expect(clases.every((c) => c === 'back-button' || c === 'pagination-button')).toBe(true);
    });
  });
});
