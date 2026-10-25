import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { delay, of } from 'rxjs';

import { CUOTAS_AL_DIA_HINT, ResumenGeneral, describeTrend } from './resumen-general';
import { EstadisticasService } from '../../services/estadisticas';
import { AuthService } from '../../services/auth';
import { CuotasFaltantesService } from '../../services/cuotas-faltantes';
import { GeneralSummaryModel, SummaryTrendsModel } from '../../models/GeneralSummaryModel';

// HU-099: Resumen General con tarjetas clicables, variación mensual, gráfico legible y explicación de "Cuotas al día".

const NEUTRAL = { changePercentage: null, direction: 'none' as const };

function summary(overrides: Partial<GeneralSummaryModel> = {}, trends: Partial<SummaryTrendsModel> = {}): GeneralSummaryModel {
  return {
    totalJugadores: 120,
    cantidadCategorias: 8,
    pagosDelMes: 40,
    recaudadoAnioActual: 9_000_000,
    ingresadoEsteMes: 95_625,
    ingresadoMesAnterior: 85_000,
    deudaAcumulada: 1_250_000,
    jugadoresSinDeuda: 90,
    jugadoresConUnaImpaga: 20,
    jugadoresConDosOMasImpagas: 10,
    recaudacionMensual: [
      { mes: 'Sep', anio: 2026, monto: 4_000_000 },
      { mes: 'Oct', anio: 2026, monto: 15_000 },
    ],
    coberturaPagoMensual: [],
    mayorDeudaPendiente: [],
    trends: {
      activePlayers: { changePercentage: 12, direction: 'up' },
      accumulatedDebt: NEUTRAL,
      monthRevenue: { changePercentage: -8, direction: 'down' },
      enabledPlayers: NEUTRAL,
      ...trends,
    },
    ...overrides,
  };
}

async function crear(resumen: GeneralSummaryModel = summary()) {
  await TestBed.configureTestingModule({
    imports: [ResumenGeneral],
    providers: [
      provideRouter([]),
      { provide: EstadisticasService, useValue: { getResumenGeneral: () => of(resumen).pipe(delay(0)) } },
      { provide: AuthService, useValue: { getUsuario: () => ({ firstName: 'Octavio' }) } },
      { provide: CuotasFaltantesService, useValue: { getMeses: () => of([]) } },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(ResumenGeneral);
  fixture.detectChanges();
  await new Promise((resolve) => setTimeout(resolve, 20));
  await fixture.whenStable();
  fixture.detectChanges();
  return { fixture, component: fixture.componentInstance, el: fixture.nativeElement as HTMLElement };
}

describe('describeTrend', () => {
  it('shows an up or down arrow with the whole percentage', () => {
    expect(describeTrend({ changePercentage: 12, direction: 'up' }).text).toBe('↑ 12 %');
    expect(describeTrend({ changePercentage: -8, direction: 'down' }).text).toBe('↓ 8 %');
    expect(describeTrend({ changePercentage: 0, direction: 'flat' }).text).toBe('= 0 %');
  });

  it('without a previous month it is neutral, with a readable text', () => {
    const trend = describeTrend(NEUTRAL);

    expect(trend.tone).toBe('neutral');
    expect(trend.text).toBe('Sin datos del mes anterior');
    expect(describeTrend(undefined).tone).toBe('neutral');
  });

  it('a card without monthly variation can say so', () => {
    expect(describeTrend(NEUTRAL, 'Sin variación mensual').text).toBe('Sin variación mensual');
  });

  it('the spoken version reads as a sentence', () => {
    expect(describeTrend({ changePercentage: -8, direction: 'down' }).spoken).toBe('bajó 8 % respecto del mes anterior');
  });
});

describe('ResumenGeneral - tarjetas clicables', () => {
  it('the four cards are links, in the story order, to their screens', async () => {
    const { el } = await crear();

    const cards = Array.from(el.querySelectorAll<HTMLAnchorElement>('.cards a.card'));
    expect(cards.map((c) => c.querySelector('.card-label')!.textContent!.trim())).toEqual([
      'Jugadores activos', 'Deuda acumulada', 'Ingresado este mes', 'Jugadores habilitados',
    ]);
    expect(cards.map((c) => c.getAttribute('href'))).toEqual([
      '/admin/portal/cuotas-pagos',
      '/admin/portal/deudas-morosidad',
      '/admin/portal/recaudacion-mensual',
      '/admin/portal/deudas-morosidad',
    ]);
  });

  it('each card has an accessible name with its value, variation and destination', async () => {
    const { el } = await crear();

    const nombres = Array.from(el.querySelectorAll('.cards a.card')).map((c) => c.getAttribute('aria-label'));
    expect(nombres[0]).toBe('Jugadores activos: 120. subió 12 % respecto del mes anterior. Ver Cuotas y Pagos');
    expect(nombres[1]).toContain('Deuda acumulada: ');
    expect(nombres[1]).toContain('Sin variación mensual. Ver Deudas y Morosidad');
    expect(nombres[2]).toContain('bajó 8 % respecto del mes anterior. Ver Recaudación Mensual');
  });

  it('the cards are reachable with the keyboard (real links, no tabindex tricks)', async () => {
    const { el } = await crear();

    for (const card of Array.from(el.querySelectorAll<HTMLAnchorElement>('.cards a.card'))) {
      expect(card.hasAttribute('href')).toBe(true);
      expect(card.getAttribute('tabindex')).toBeNull();
    }
  });
});

describe('ResumenGeneral - variación mensual', () => {
  it('two months with data: active players and revenue show their arrow', async () => {
    const { el } = await crear();

    const tendencias = Array.from(el.querySelectorAll('.card-trend')).map((t) => t.textContent!.trim());
    expect(tendencias).toEqual(['↑ 12 %', 'Sin variación mensual', '↓ 8 %', 'Sin variación mensual']);
    expect(el.querySelectorAll('.card-trend.trend-up')).toHaveLength(1);
    expect(el.querySelectorAll('.card-trend.trend-down')).toHaveLength(1);
  });

  it('a previous month without payments leaves revenue neutral, keeping the layout', async () => {
    const { el } = await crear(summary({ ingresadoMesAnterior: 0 }, { monthRevenue: NEUTRAL }));

    const revenue = el.querySelectorAll('.card-trend')[2];
    expect(revenue.textContent!.trim()).toBe('Sin datos del mes anterior');
    expect(revenue.classList).toContain('trend-neutral');
    expect(el.querySelectorAll('.card-trend')).toHaveLength(4);
  });

  it('a current month without payments is shown as a drop', async () => {
    const { el } = await crear(summary({ ingresadoEsteMes: 0 }, { monthRevenue: { changePercentage: -100, direction: 'down' } }));

    expect(el.querySelectorAll('.card-trend')[2].textContent!.trim()).toBe('↓ 100 %');
  });

  it('the revenue card no longer repeats the change as a badge', async () => {
    const { component } = await crear();

    expect(component.cards[2].badge).toBe('');
  });
});

describe('ResumenGeneral - gráfico de recaudación', () => {
  it('uses a bar chart starting at $0 with a minimum bar height, so a small month stays visible', async () => {
    const { component } = await crear();

    const dataset = component.revenueData.datasets[0] as { data: number[]; minBarLength?: number };
    expect(dataset.data).toEqual([4_000_000, 15_000]);
    expect(dataset.minBarLength).toBeGreaterThan(0);
    const y = component.revenueOptions!.scales!['y'] as { beginAtZero?: boolean };
    expect(y.beginAtZero).toBe(true);
  });

  it('the axis shows compact amounts and the tooltip the month with its year', async () => {
    const { component } = await crear();

    const ticks = (component.revenueOptions!.scales!['y'] as { ticks: { callback: (v: number) => string } }).ticks;
    expect(ticks.callback(4_000_000)).toBe('$4 M');
    expect(component.revenueTitles).toEqual(['Sep 2026', 'Oct 2026']);
  });

  it('the chart has a text alternative for screen readers', async () => {
    const { el } = await crear();

    const canvas = el.querySelector('.chart-canvas canvas')!;
    expect(canvas.getAttribute('role')).toBe('img');
    expect(canvas.getAttribute('aria-label')).toContain('Sep 2026 $4 M, Oct 2026 $15 k');
  });

  it('without payments it says so instead of an empty chart', async () => {
    const { el } = await crear(summary({ recaudacionMensual: [] }));

    expect(el.querySelector('.chart-canvas')).toBeNull();
    expect(el.querySelector('.chart-empty')!.textContent).toContain('Todavía no hay cobros');
  });
});

describe('ResumenGeneral - Cuotas al día', () => {
  it('has a "?" that explains how it is calculated', async () => {
    const { component, el } = await crear();

    const cuotas = component.bannerStats.find((s) => s.label === 'Cuotas al día')!;
    expect(cuotas.hint).toBe(CUOTAS_AL_DIA_HINT);
    expect(CUOTAS_AL_DIA_HINT).toContain('ninguna cuota vencida');
    expect(CUOTAS_AL_DIA_HINT).toContain('día 10');
    expect(el.querySelectorAll('.banner-stat app-help-hint')).toHaveLength(1);
  });
});
