import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { from } from 'rxjs';

import { DeudasMorosidad } from './deudas-morosidad';
import { PagosService } from '../../services/pagos';
import { ReportesService } from '../../services/reportes';
import { NotificationService } from '../../shared/notifications/notification.service';
import { PlayerAccountModel } from '../../models/PlayerAccountModel';
import { CategoryModel } from '../../models/CategoryModel';
import { PaymentSummaryModel } from '../../models/PaymentModel';

// HU-029: the debtors list in Deudas y Morosidad. The HTTP services are replaced by test
// doubles, so these tests need neither the API nor the database. The real query (balance
// greater than zero only) is covered by the Postman collection "HU-029 - Filtro de Jugadores Deudores".

const EMPTY_SUMMARY: PaymentSummaryModel = {
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
  // Por defecto todas vencidas (vencen el 10 del mes siguiente): así se prueba el estado deportivo.
  overdue = installments,
): PlayerAccountModel {
  return {
    playerId: id,
    firstName: firstName,
    lastName: 'PRUEBA',
    dni: `9900000${id}`,
    category,
    amountOwed: amount,
    pendingInstallments: installments,
    overdueInstallments: overdue,
    // Como lo informa el backend (la misma regla que la API de integración).
    sportStatus: overdue >= 2 ? 'inhabilitado' : overdue === 1 ? 'solo_entrenamientos' : 'habilitado',
  };
}

// Responses arrive asynchronously, as in the real app.
const response = <T>(value: T) => from(Promise.resolve(value));

async function create(
  debtors: PlayerAccountModel[],
  categorias: CategoryModel[] = [],
  resumen: PaymentSummaryModel = EMPTY_SUMMARY,
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

    expect(textOf(fixture)).toContain('No hay jugadores con cuotas atrasadas');
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

    expect(textOf(fixture)).toContain('Ningún resultado coincide con la búsqueda o los filtros');
    expect(textOf(fixture)).not.toContain('No hay jugadores con cuotas atrasadas');

    // HU-095: el vacío por filtros ofrece limpiarlos, y al tocarlo vuelve la lista.
    const limpiar = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.vacio-accion')!;
    expect(limpiar.textContent?.trim()).toBe('Limpiar filtros');
    limpiar.click();
    fixture.detectChanges();
    expect(tablePlayers(fixture)).toEqual(['PRUEBA, SANTIAGO']);
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

// HU-081: un jugador dado de baja puede seguir debiendo. Se lista con el estado "Dado de baja" (no con su
// situación deportiva), se puede filtrar, y no cuenta como inhabilitado.
describe('DeudasMorosidad - jugadores dados de baja (HU-081)', () => {
  const baja = (id: number, nombre: string, cuotas: number, monto: number): PlayerAccountModel => ({
    ...debtor(id, nombre, cuotas, monto),
    leaveDate: '2026-09-15',
    sportStatus: 'baja', // el backend informa baja aunque el jugador deba cuotas vencidas
  });

  it('muestra al dado de baja con su fecha en lugar del estado deportivo', async () => {
    const fixture = await create([baja(1, 'SANTIAGO', 3, 276000)]);

    const pill = (fixture.nativeElement as HTMLElement).querySelector('.etiqueta-estado');
    expect(pill?.textContent?.trim()).toBe('Dado de baja 15/09/2026');
    expect(pill?.classList).toContain('tono-neutro');
  });

  it('filtra activos y dados de baja', async () => {
    const fixture = await create([debtor(1, 'ACTIVO', 1, 92000), baja(2, 'BAJA', 2, 184000)]);
    const component: DeudasMorosidad = fixture.componentInstance;

    component.selectedSituacion = 'Dados de baja';
    component.onFiltrosChange();
    fixture.detectChanges();
    expect(tablePlayers(fixture)).toEqual(['PRUEBA, BAJA']);

    component.selectedSituacion = 'Activos';
    component.onFiltrosChange();
    fixture.detectChanges();
    expect(tablePlayers(fixture)).toEqual(['PRUEBA, ACTIVO']);
  });

  it('no cuenta al dado de baja como inhabilitado', async () => {
    const fixture = await create([baja(1, 'BAJA', 4, 368000)]);
    const component: DeudasMorosidad = fixture.componentInstance;

    expect(component.topInhabilitados).toEqual([]);

    component.selectedEstado = 'Inhabilitado';
    component.onFiltrosChange();
    fixture.detectChanges();
    expect(tablePlayers(fixture)).toEqual([]);
  });
});

// Regla del día 10: la cuota de cada mes vence el 10 del mes siguiente, y el estado deportivo cuenta solo las
// vencidas. Quien debe la cuota del mes pero todavía no venció sigue habilitado ("En término").
describe('DeudasMorosidad - estado deportivo por cuotas vencidas', () => {
  const pills = (fixture: Awaited<ReturnType<typeof create>>) =>
    Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.etiqueta-estado')).map((p) => p.textContent?.trim());

  it('sin cuotas vencidas está en término; con 1 solo entrena; con 2 o más inhabilitado', async () => {
    const fixture = await create([
      debtor(1, 'TERMINO', 1, 92000, 'AFA 20067', 0),
      debtor(2, 'PARCIAL', 2, 184000, 'AFA 20067', 1),
      debtor(3, 'INHABILITADO', 2, 184000, 'AFA 20067', 2),
    ]);

    expect(pills(fixture)).toEqual(['Inhabilitado', 'Solo entrenamientos', 'En término']);
  });

  it('el panel de inhabilitados no incluye a quien solo debe cuotas en término', async () => {
    const fixture = await create([debtor(1, 'TERMINO', 2, 184000, 'AFA 20067', 0)]);

    expect(fixture.componentInstance.topInhabilitados).toEqual([]);
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
    const categorias: CategoryModel[] = [
      { idCategoria: 1, nombre: 'AFA 20067' },
      { idCategoria: 2, nombre: 'AFA 20112' },
    ];
    const fixture = await create([debtor(1, 'SANTIAGO', 1, 92000, 'AFA 20067')], categorias);
    const component: DeudasMorosidad = fixture.componentInstance;

    expect(component.categoriaOptions).toContain('AFA 20112');
  });

  it('recalculates the banner to the selected category subtotal, not the club-wide total', async () => {
    const categorias: CategoryModel[] = [
      { idCategoria: 1, nombre: 'AFA 20067' },
      { idCategoria: 2, nombre: 'AFA 20112' },
    ];
    const resumenGlobal: PaymentSummaryModel = {
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
