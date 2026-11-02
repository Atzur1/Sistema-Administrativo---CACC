import { ChangeDetectorRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, Subject, of } from 'rxjs';

import { ActividadMovimientos } from './actividad-movimientos';
import { PagosService } from '../../services/pagos';
import { RecentPaymentModel, RegisteredPaymentsFilterModel, RegisteredPaymentsPageModel } from '../../models/PaymentModel';
import { SEARCH_DEBOUNCE_MS } from '../../shared/filter-bar/filter-bar';

// "Pagos registrados" se pagina en la API: la pantalla pide solo la página visible con los filtros aplicados.
const PAGO: RecentPaymentModel = {
  idPago: 1, idJugador: 3, nombreCompleto: 'Pérez, Juan', categoria: 'Sub 17', periodo: 'Octubre 2026',
  metodoPago: 'Efectivo', monto: 92000, estado: 'Pagado', fechaPago: '2026-10-05', concepto: 'Cuota',
  fechaHoraRegistro: null,
} as RecentPaymentModel;

const pagina = (items: RecentPaymentModel[], total = 25, totalSinFiltros = 40): RegisteredPaymentsPageModel =>
  ({ items, total, totalSinFiltros, pagina: 1, tamanioPagina: 10 });

async function create(responder: (f: RegisteredPaymentsFilterModel) => Observable<RegisteredPaymentsPageModel> = () => of(pagina([PAGO]))) {
  const getPagosRegistrados = vi.fn(responder);
  const getResumenHoy = vi.fn(() => of({ pagosHoy: 0, recaudadoHoy: 0 }));
  const getRecientes = vi.fn(() => of([]));
  await TestBed.configureTestingModule({
    imports: [ActividadMovimientos],
    providers: [
      provideRouter([]),
      { provide: PagosService, useValue: { getPagosRegistrados, getResumenHoy, getRecientes } },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(ActividadMovimientos);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  // Las pruebas cambian el estado llamando métodos: la vista se marca antes de redibujar (OnPush por defecto).
  const refrescar = () => {
    fixture.componentRef.injector.get(ChangeDetectorRef).markForCheck();
    fixture.detectChanges();
  };
  const buscador = () => el.querySelector<HTMLInputElement>('app-filter-bar input[type="search"]')!;
  const escribir = (texto: string) => {
    buscador().value = texto;
    buscador().dispatchEvent(new Event('input'));
  };
  return { fixture, component: fixture.componentInstance, el, refrescar, buscador, escribir, getPagosRegistrados, getResumenHoy, getRecientes };
}

describe('ActividadMovimientos - Pagos registrados paginados en la API', () => {
  afterEach(() => vi.useRealTimers());

  it('pide solo la primera página y usa el total filtrado de la API para el paginador', async () => {
    const { component, getPagosRegistrados } = await create();

    expect(getPagosRegistrados).toHaveBeenCalledTimes(1);
    expect(getPagosRegistrados).toHaveBeenCalledWith(expect.objectContaining({ pagina: 1, tamanioPagina: 10 }));
    expect(component.pagedPayments.length).toBe(1);
    expect(component.totalPaginas).toBe(3); // 25 pagos filtrados / 10 por página (no los 40 sin filtros)
  });

  it('cambiar de página pide esa página y conserva los filtros', async () => {
    const { component, getPagosRegistrados } = await create();
    component.onEstadoChange('Parcial');

    component.irAPagina(2);

    expect(getPagosRegistrados).toHaveBeenLastCalledWith(expect.objectContaining({ pagina: 2, estado: 'Parcial' }));
  });
});

// HU-102: los filtros de "Pagos registrados" usan la barra compartida (HU-097).
describe('ActividadMovimientos - barra de filtros compartida (HU-102)', () => {
  afterEach(() => vi.useRealTimers());

  it('el estado se aplica al instante, vuelve a la página 1 y consulta una sola vez', async () => {
    const { component, getPagosRegistrados } = await create();
    component.irAPagina(2);
    getPagosRegistrados.mockClear();

    component.onEstadoChange('Parcial');

    expect(component.page).toBe(1);
    expect(getPagosRegistrados).toHaveBeenCalledTimes(1);
    expect(getPagosRegistrados).toHaveBeenLastCalledWith(expect.objectContaining({ pagina: 1, estado: 'Parcial' }));
  });

  it('la búsqueda espera 300 ms desde la última tecla, vuelve a la página 1 y consulta una sola vez', async () => {
    const { component, escribir, getPagosRegistrados } = await create();
    component.irAPagina(2);
    getPagosRegistrados.mockClear();
    vi.useFakeTimers();

    escribir('pe');
    escribir('perez');
    vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS - 1);
    expect(getPagosRegistrados).not.toHaveBeenCalled();

    vi.advanceTimersByTime(1);
    expect(getPagosRegistrados).toHaveBeenCalledTimes(1);
    expect(getPagosRegistrados).toHaveBeenLastCalledWith(expect.objectContaining({ pagina: 1, busqueda: 'perez' }));
  });

  it('Enter antes de los 300 ms busca al instante y una sola vez', async () => {
    const { buscador, escribir, getPagosRegistrados } = await create();
    getPagosRegistrados.mockClear();
    vi.useFakeTimers();

    escribir('perez');
    buscador().dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS);

    expect(getPagosRegistrados).toHaveBeenCalledTimes(1);
    expect(getPagosRegistrados).toHaveBeenLastCalledWith(expect.objectContaining({ busqueda: 'perez' }));
  });

  it('cada filtro puesto es una etiqueta; quitar una deja los demás y consulta una vez', async () => {
    const { component, el, refrescar, getPagosRegistrados } = await create();
    component.onBusquedaChange('perez');
    component.onEstadoChange('Parcial');
    refrescar();

    const etiquetas = () => Array.from(el.querySelectorAll('.filter-bar-chip')).map((c) => c.textContent!.replace('×', '').trim());
    expect(etiquetas()).toEqual(['Búsqueda: perez', 'Estado: Parcial']);

    getPagosRegistrados.mockClear();
    el.querySelectorAll<HTMLButtonElement>('.filter-bar-chip button')[1].click();
    refrescar();

    expect(etiquetas()).toEqual(['Búsqueda: perez']);
    expect(getPagosRegistrados).toHaveBeenCalledTimes(1);
    expect(getPagosRegistrados).toHaveBeenLastCalledWith(expect.objectContaining({ pagina: 1, busqueda: 'perez', estado: null }));
  });

  it('"Limpiar filtros" restablece búsqueda, estado y página, y no deja una búsqueda escrita pendiente', async () => {
    const { component, el, refrescar, escribir, buscador, getPagosRegistrados } = await create();
    component.onEstadoChange('Parcial');
    component.irAPagina(2);
    refrescar();
    vi.useFakeTimers();

    escribir('per');                                        // todavía no aplicada
    getPagosRegistrados.mockClear();
    el.querySelector<HTMLButtonElement>('.filter-bar-clear')!.click();
    vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS);
    refrescar();

    expect(component.page).toBe(1);
    expect(component.busqueda).toBe('');
    expect(component.estado).toBeNull();
    expect(buscador().value).toBe('');
    expect(getPagosRegistrados).toHaveBeenCalledTimes(1);
    expect(getPagosRegistrados).toHaveBeenLastCalledWith(expect.objectContaining({ pagina: 1, busqueda: '', estado: null }));
  });

  it('una respuesta vieja que llega tarde no reemplaza la de los filtros más recientes', async () => {
    const respuestas: Subject<RegisteredPaymentsPageModel>[] = [];
    const { component } = await create(() => {
      const s = new Subject<RegisteredPaymentsPageModel>();
      respuestas.push(s);
      return s;
    });

    component.onEstadoChange('Parcial');
    expect(respuestas).toHaveLength(2);
    expect(respuestas[0].observed).toBe(false);             // el pedido viejo se canceló

    respuestas[1].next(pagina([{ ...PAGO, idPago: 2, estado: 'Parcial' }], 1, 40));
    respuestas[0].next(pagina([PAGO, PAGO], 25, 40));

    expect(component.pagedPayments.map((p) => p.id)).toEqual([2]);
    expect(component.totalFiltrados).toBe(1);
  });

  it('filtrar no vuelve a pedir las métricas del día ni la actividad reciente', async () => {
    const { component, getResumenHoy, getRecientes } = await create();

    component.onEstadoChange('Pagado');
    component.onBusquedaChange('perez');
    component.limpiarFiltros();

    expect(getResumenHoy).toHaveBeenCalledTimes(1);
    expect(getRecientes).toHaveBeenCalledTimes(1);
  });

  it('al salir de la pantalla se cancela el pedido de pagos en curso', async () => {
    const respuestas: Subject<RegisteredPaymentsPageModel>[] = [];
    const { fixture } = await create(() => {
      const s = new Subject<RegisteredPaymentsPageModel>();
      respuestas.push(s);
      return s;
    });

    fixture.destroy();

    expect(respuestas[0].observed).toBe(false);
  });

  it('la barra va a ancho completo, debajo del encabezado y antes de los paneles, y aclara qué filtra', async () => {
    const { el } = await create();

    const barra = el.querySelector('.dashboard > .panel-filtros app-filter-bar');
    expect(barra).not.toBeNull();
    expect(el.querySelector('.main-grid app-filter-bar')).toBeNull();
    expect(barra!.compareDocumentPosition(el.querySelector('.main-grid')!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(el.querySelector('.panel-filtros [filterBarNote]')!.textContent).toContain('Pagos registrados');
  });

  it('el selector de estado tiene un nombre que llega al botón del desplegable', async () => {
    const { el } = await create();

    const label = el.querySelector<HTMLLabelElement>('.panel-filtros label[for="filtro-estado-pagos"]')!;
    expect(label.textContent!.trim()).toBe('Estado');
    expect(el.querySelector('button#filtro-estado-pagos')).not.toBeNull();
  });
});
