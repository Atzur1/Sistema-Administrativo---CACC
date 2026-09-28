import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { from } from 'rxjs';

import { DeudasMorosidad } from './deudas-morosidad';
import { Categoria, PagosService, ResumenPagos } from '../../services/pagos';
import { ReportesService } from '../../services/reportes';
import { NotificationService } from '../../shared/notifications/notification.service';
import { PlayerAccountModel } from '../../models/PlayerAccountModel';

// HU-029: the debtors list in Deudas y Morosidad. The HTTP services are replaced by test
// doubles, so these tests need neither the API nor the database. The real query (balance
// greater than zero only) is covered by the Postman collection "HU-029 - Filtro de Jugadores Deudores".

const EMPTY_SUMMARY: ResumenPagos = {
  recaudadoAnioActual: 0,
  pagosDelMes: 0,
  cantidadPendientes: 0,
  deudaGlobalTotal: 0,
  jugadoresMorosos: 0,
};

function debtor(
  id: number,
  firstName: string,
  installments: number,
  amount: number,
  category = 'AFA 20067',
): PlayerAccountModel {
  return {
    playerId: id,
    firstName: firstName,
    lastName: 'PRUEBA',
    dni: `9900000${id}`,
    category,
    amountOwed: amount,
    pendingInstallments: installments,
  };
}

// Responses arrive asynchronously, as in the real app.
const response = <T>(value: T) => from(Promise.resolve(value));

async function create(
  debtors: PlayerAccountModel[],
  categorias: Categoria[] = [],
  resumen: ResumenPagos = EMPTY_SUMMARY,
): Promise<ComponentFixture<DeudasMorosidad>> {
  const pagosService = {
    getPendientes: vi.fn(() => response([])),
    getResumen: vi.fn(() => response(resumen)),
    getPlayerAccounts: vi.fn(() => response(debtors)),
    getDeudaPorCategoria: vi.fn(() => response([])),
    // HU-020: catálogo completo de categorías (tenga o no deudores hoy).
    getCategorias: vi.fn(() => response(categorias)),
  };

  await TestBed.configureTestingModule({
    imports: [DeudasMorosidad],
    providers: [
      provideRouter([]),
      { provide: PagosService, useValue: pagosService },
      { provide: ReportesService, useValue: {} },
      { provide: NotificationService, useValue: { notify: vi.fn() } },
    ],
  }).compileComponents();

  const fixture: ComponentFixture<DeudasMorosidad> = TestBed.createComponent(DeudasMorosidad);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return fixture;
}

function textOf(fixture: ComponentFixture<DeudasMorosidad>): string {
  return (fixture.nativeElement as HTMLElement).textContent ?? '';
}

function tablePlayers(fixture: ComponentFixture<DeudasMorosidad>): string[] {
  const cells: NodeListOf<HTMLElement> = (fixture.nativeElement as HTMLElement).querySelectorAll(
    '.cell-player',
  );
  return Array.from(cells).map((cell) => cell.textContent?.trim() ?? '');
}

describe('DeudasMorosidad - debtors list (HU-029)', () => {
  it('shows the empty-state message when no student owes anything', async () => {
    const fixture = await create([]);

    expect(textOf(fixture)).toContain(
      'No se registran alumnos con cuotas atrasadas en este momento.',
    );
    expect(tablePlayers(fixture)).toEqual([]);
  });

  it('sorts by installments owed, most first, and by amount when tied', async () => {
    const fixture = await create([
      debtor(1, 'SANTIAGO', 1, 92000),
      debtor(2, 'NICOLAS', 3, 276000),
      debtor(3, 'MATIAS', 1, 120000),
      debtor(4, 'ALEXIS', 2, 184000),
    ]);

    expect(tablePlayers(fixture)).toEqual([
      'PRUEBA, NICOLAS',
      'PRUEBA, ALEXIS',
      'PRUEBA, MATIAS',
      'PRUEBA, SANTIAGO',
    ]);
  });

  it('tells a search with no matches apart from a club with no debtors', async () => {
    const fixture = await create([debtor(1, 'SANTIAGO', 1, 92000)]);
    const component: DeudasMorosidad = fixture.componentInstance;

    component.busqueda = 'zzz';
    component.onFiltrosChange();
    fixture.detectChanges();

    expect(textOf(fixture)).toContain(
      'Ningún deudor coincide con la búsqueda o los filtros aplicados.',
    );
    expect(textOf(fixture)).not.toContain('No se registran alumnos con cuotas atrasadas');
  });

  it('shows every debtor again after clearing the filters', async () => {
    const fixture = await create([
      debtor(1, 'SANTIAGO', 1, 92000),
      debtor(2, 'NICOLAS', 2, 184000),
    ]);
    const component: DeudasMorosidad = fixture.componentInstance;

    component.busqueda = 'zzz';
    component.onFiltrosChange();
    component.limpiarFiltros();
    fixture.detectChanges();

    expect(tablePlayers(fixture)).toEqual(['PRUEBA, NICOLAS', 'PRUEBA, SANTIAGO']);
  });
});

// HU-020 (QA, 24/09): el combo de categorías tiene que listar las 13 categorías del
// club (catálogo propio), no solo las que hoy tienen algún moroso; y el banner de
// arriba tiene que mostrar el subtotal de la categoría elegida, no seguir mostrando
// los totales globales del club.
describe('DeudasMorosidad - filtro por categoría (HU-020)', () => {
  // El banner cuenta de 0 al valor real en ~900ms (ver animateBannerMetrics). Se fuerza
  // prefers-reduced-motion para que el componente escriba el valor final directamente,
  // sin depender de requestAnimationFrame ni de temporizadores reales en el test.
  const originalMatchMedia = window.matchMedia;

  beforeEach(() => {
    window.matchMedia = vi
      .fn()
      .mockReturnValue({ matches: true }) as unknown as typeof window.matchMedia;
  });

  afterEach(() => {
    window.matchMedia = originalMatchMedia;
  });

  it('lists a category with zero debtors today in the selector', async () => {
    const categorias: Categoria[] = [
      { idCategoria: 1, nombre: 'AFA 20067' },
      { idCategoria: 2, nombre: 'AFA 20112' },
    ];
    const fixture = await create([debtor(1, 'SANTIAGO', 1, 92000, 'AFA 20067')], categorias);
    const component: DeudasMorosidad = fixture.componentInstance;

    expect(component.categoriaOptions).toContain('AFA 20112');
  });

  it('recalculates the banner to the selected category subtotal, not the club-wide total', async () => {
    const categorias: Categoria[] = [
      { idCategoria: 1, nombre: 'AFA 20067' },
      { idCategoria: 2, nombre: 'AFA 20112' },
    ];
    const resumenGlobal: ResumenPagos = {
      recaudadoAnioActual: 0,
      pagosDelMes: 0,
      cantidadPendientes: 2,
      deudaGlobalTotal: 276000, // 92000 + 184000, el total del club entero
      jugadoresMorosos: 2,
    };
    const fixture = await create(
      [debtor(1, 'SANTIAGO', 1, 92000, 'AFA 20067'), debtor(2, 'NICOLAS', 2, 184000, 'AFA 20112')],
      categorias,
      resumenGlobal,
    );
    const component: DeudasMorosidad = fixture.componentInstance;

    component.selectedCategoria = 'AFA 20067';
    component.onFiltrosChange();
    fixture.detectChanges();

    const banner = component.bannerMetrics;
    expect(banner.find((m) => m.label === 'Deuda total')?.value).toBe('$92 k');
    expect(banner.find((m) => m.label === 'Deudores')?.value).toBe('1');
  });
});
