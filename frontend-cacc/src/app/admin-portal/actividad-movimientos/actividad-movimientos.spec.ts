import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { ActividadMovimientos } from './actividad-movimientos';
import { FiltroPagosRegistrados, PagoReciente, PagosService } from '../../services/pagos';

// "Pagos registrados" se pagina en la API: la pantalla pide solo la página visible con los filtros aplicados.
const PAGO: PagoReciente = {
  idPago: 1, idJugador: 3, nombreCompleto: 'Pérez, Juan', categoria: 'Sub 17', periodo: 'Octubre 2026',
  metodoPago: 'Efectivo', monto: 92000, estado: 'Pagado', fechaPago: '2026-10-05', concepto: 'Cuota',
  fechaHoraRegistro: null,
} as PagoReciente;

async function create() {
  const getPagosRegistrados = vi.fn((_: FiltroPagosRegistrados) =>
    of({ items: [PAGO], total: 25, totalSinFiltros: 40, pagina: 1, tamanioPagina: 10 }),
  );
  await TestBed.configureTestingModule({
    imports: [ActividadMovimientos],
    providers: [
      provideRouter([]),
      {
        provide: PagosService,
        useValue: {
          getPagosRegistrados,
          getResumenHoy: () => of({ pagosHoy: 0, recaudadoHoy: 0 }),
          getRecientes: () => of([]),
        },
      },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(ActividadMovimientos);
  fixture.detectChanges();
  return { fixture, component: fixture.componentInstance, getPagosRegistrados };
}

describe('ActividadMovimientos - Pagos registrados paginados en la API', () => {
  afterEach(() => vi.useRealTimers());

  it('pide solo la primera página y usa el total de la API para el paginador', async () => {
    const { component, getPagosRegistrados } = await create();

    expect(getPagosRegistrados).toHaveBeenCalledWith(expect.objectContaining({ pagina: 1, tamanioPagina: 10 }));
    expect(component.pagedPayments.length).toBe(1);
    expect(component.totalPaginas).toBe(3); // 25 pagos / 10 por página
  });

  it('cambiar de página pide esa página', async () => {
    const { component, getPagosRegistrados } = await create();

    component.irAPagina(2);

    expect(getPagosRegistrados).toHaveBeenLastCalledWith(expect.objectContaining({ pagina: 2 }));
  });

  it('al filtrar vuelve a la página 1 y manda la búsqueda y el estado a la API', async () => {
    vi.useFakeTimers();
    const { component, getPagosRegistrados } = await create();
    component.irAPagina(2);

    component.filterForm.setValue({ busqueda: 'perez', estado: 'Parcial' });
    vi.advanceTimersByTime(300);

    expect(component.page).toBe(1);
    expect(getPagosRegistrados).toHaveBeenLastCalledWith(
      expect.objectContaining({ pagina: 1, busqueda: 'perez', estado: 'Parcial' }),
    );
  });
});
