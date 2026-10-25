import { ChangeDetectorRef, Component, DestroyRef, NgZone, OnInit, WritableSignal, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { BaseChartDirective } from 'ng2-charts';
import { Chart, ChartConfiguration, registerables } from 'chart.js';
import { EstadisticasService } from '../../services/estadisticas';
import { AuthService } from '../../services/auth';
import { formatCompactCurrency } from '../../shared/format-currency';
import { animateCountUp } from '../../shared/count-up';
import { AvisoCuotasFaltantes } from './aviso-cuotas-faltantes/aviso-cuotas-faltantes';
import { HelpHint } from '../../shared/help-hint/help-hint';
import {
  GeneralSummaryModel, KpiTrendModel, MonthlyCollectionPointModel, MonthlyCoveragePointModel,
} from '../../models/GeneralSummaryModel';

Chart.register(...registerables);

// One count-up metric on the banner ("Pagos del mes", "Recaudado 2026"...).
// `value` is a signal so the count-up animation can update it every frame
// without depending on which zone the click/navigation that rendered this
// component landed in (see CustomSelect for the same reasoning).
interface BannerStat {
  value: WritableSignal<string>;
  label: string;
  // Texto del "?" que explica cómo se calcula (HU-099: "Cuotas al día")
  hint?: string;
}

// HU-099: variación de la tarjeta contra el mes anterior, lista para mostrar
export interface CardTrend {
  text: string;          // "↑ 12 %" o "Sin variación mensual"
  tone: 'up' | 'down' | 'flat' | 'neutral';
  spoken: string;        // lo mismo, dicho para el lector de pantalla
}

// One metric card in the KPI row. HU-099: cada tarjeta es un enlace a la pantalla con su detalle.
interface SummaryCard {
  value: WritableSignal<string>;
  label: string;
  detail: string;
  badge: string;
  badgeTone: 'positive' | 'negative' | 'neutral';
  progress: number;
  progressColor: string;
  route: string;
  destination: string;   // nombre de la pantalla a la que lleva, para el nombre accesible
  finalValue: string;    // el valor ya formateado (el animado arranca en 0 y no sirve para el aria-label)
  trend: CardTrend;
}

// One row in the eligibility panel
interface EligibilityItem {
  tone: 'success' | 'warning' | 'danger';
  title: string;
  description: string;
}

// One player in the "mayor deuda pendiente" list
interface DebtorPlayer {
  id: number;
  name: string;
  category: string;
  debtLabel: string;
}

const CURRENCY_FULL = new Intl.NumberFormat('es-AR', {
  style: 'currency',
  currency: 'ARS',
  maximumFractionDigits: 0,
});

// HU-099: lo que muestra el "?" de "Cuotas al día"
export const CUOTAS_AL_DIA_HINT =
  'Porcentaje de jugadores activos que no tienen ninguna cuota vencida impaga. Una cuota vence el día 10 del mes ' +
  'siguiente al que cubre; la inscripción no cuenta, y una cuota que una beca o descuento deja en $0 tampoco.';

// HU-099: texto de la variación mensual de una tarjeta. Sin base de comparación queda neutral, con el mismo alto.
export function describeTrend(trend: KpiTrendModel | null | undefined, neutralText = 'Sin datos del mes anterior'): CardTrend {
  if (!trend || trend.direction === 'none' || trend.changePercentage == null) {
    return { text: neutralText, tone: 'neutral', spoken: neutralText };
  }
  const value = Math.abs(trend.changePercentage);
  switch (trend.direction) {
    case 'up':
      return { text: `↑ ${value} %`, tone: 'up', spoken: `subió ${value} % respecto del mes anterior` };
    case 'down':
      return { text: `↓ ${value} %`, tone: 'down', spoken: `bajó ${value} % respecto del mes anterior` };
    default:
      return { text: '= 0 %', tone: 'flat', spoken: 'igual que el mes anterior' };
  }
}

const PANTALLAS = {
  cuotas: { route: '/admin/portal/cuotas-pagos', name: 'Cuotas y Pagos' },
  deudas: { route: '/admin/portal/deudas-morosidad', name: 'Deudas y Morosidad' },
  recaudacion: { route: '/admin/portal/recaudacion-mensual', name: 'Recaudación Mensual' },
};

@Component({
  selector: 'app-resumen-general',
  standalone: true,
  imports: [CommonModule, RouterLink, BaseChartDirective, AvisoCuotasFaltantes, HelpHint],
  templateUrl: './resumen-general.html',
  styleUrl: './resumen-general.css',
})
export class ResumenGeneral implements OnInit {

  private zone = inject(NgZone);
  private destroyRef = inject(DestroyRef);
  private cancelCountUp: () => void = () => {};

  currentDate = '';
  greeting = '';
  cargando = true;

  bannerStats: BannerStat[] = [];
  cards: SummaryCard[] = [];

  // HU-099: recaudación mensual como gráfico de barras con ejes. La escala arranca en $0 y cada barra con cobros
  // tiene un alto mínimo visible, así un mes chico al lado de uno grande no desaparece.
  revenueData: ChartConfiguration<'bar'>['data'] = { labels: [], datasets: [{ data: [] }] };
  revenueTitles: string[] = [];
  revenueOptions: ChartConfiguration<'bar'>['options'] = {
    responsive: true,
    maintainAspectRatio: false,
    animation: { duration: 600, easing: 'easeOutQuart' },
    layout: { padding: { top: 4, right: 4 } },
    plugins: {
      legend: { display: false },
      tooltip: {
        backgroundColor: '#000000',
        titleColor: '#ffffff',
        bodyColor: '#ffffff',
        borderWidth: 0,
        displayColors: false,
        callbacks: {
          title: (items) => this.revenueTitles[items[0]?.dataIndex ?? 0] ?? '',
          label: (context) => CURRENCY_FULL.format(Number(context.parsed.y)),
        },
      },
    },
    scales: {
      x: {
        grid: { display: false },
        border: { color: '#cfd8d2' },
        ticks: { color: '#4b5563', font: { size: 11, weight: 600 } },
      },
      y: {
        beginAtZero: true,
        grace: '8%',
        grid: { color: '#eef2ef' },
        border: { display: false },
        ticks: {
          color: '#4b5563',
          font: { size: 11 },
          maxTicksLimit: 6,
          callback: (value) => formatCompactCurrency(Number(value)),
        },
      },
    },
  };
  eligibility: EligibilityItem[] = [];
  suspendedPlayers: DebtorPlayer[] = [];

  trendData: ChartConfiguration<'line'>['data'] = { labels: [], datasets: [{ data: [] }] };
  trendBadgeText = '';
  trendBadgePositive = true;

  trendOptions: ChartConfiguration<'line'>['options'] = {
    responsive: true,
    maintainAspectRatio: false,
    animation: {
      duration: 800,
      easing: 'easeOutQuart',
      delay: (context) =>
        context.type === 'data' && context.mode === 'default' ? context.dataIndex * 90 : 0,
    },
    layout: { padding: { top: 4, right: 4 } },
    plugins: {
      legend: { display: false },
      tooltip: {
        backgroundColor: '#000000',
        titleColor: '#ffffff',
        bodyColor: '#ffffff',
        borderWidth: 0,
        displayColors: false,
        callbacks: { label: (context) => `${context.parsed.y}%` },
      },
    },
    scales: {
      x: { grid: { display: false }, border: { display: false }, ticks: { color: '#555a55', font: { size: 10 } } },
      // 0-100: la cobertura de pago mensual real varía mucho más que el 60-100 del mock original
      y: {
        min: 0,
        max: 100,
        grid: { color: '#f1f5f1' },
        border: { display: false },
        ticks: { color: '#555a55', font: { size: 10 }, stepSize: 20, callback: (value) => `${value}%` },
      },
    },
  };

  private monthNames = [
    'enero', 'febrero', 'marzo', 'abril', 'mayo', 'junio',
    'julio', 'agosto', 'septiembre', 'octubre', 'noviembre', 'diciembre',
  ];

  private dayNames = ['domingo', 'lunes', 'martes', 'miércoles', 'jueves', 'viernes', 'sábado'];

  constructor(
    private estadisticasService: EstadisticasService,
    private authService: AuthService,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit() {
    this.currentDate = this.formatToday();
    this.greeting = this.buildGreeting();
    this.destroyRef.onDestroy(() => this.cancelCountUp());
    this.cargarResumen();
  }

  private buildGreeting(): string {
    const hora = new Date().getHours();
    const momento = hora < 12 ? 'Buenos días' : hora < 20 ? 'Buenas tardes' : 'Buenas noches';
    // El nombre de la cuenta, igual que la barra superior. El usuario del correo queda solo para una
    // cuenta sin nombre cargado: saludar a "alexandermonelli50" no reconoce a nadie.
    const usuario = this.authService.getUsuario();
    const nombre = usuario?.firstName?.trim() || usuario?.email?.split('@')[0] || 'Admin';
    return `${momento}, ${nombre}`;
  }

  private cargarResumen() {
    this.estadisticasService.getResumenGeneral().subscribe({
      next: (resumen) => {
        this.aplicarResumen(resumen);
        this.cargando = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.cargando = false;
        this.cdr.detectChanges();
      },
    });
  }

  private aplicarResumen(r: GeneralSummaryModel) {
    const jugadoresConRestricciones = r.jugadoresConUnaImpaga + r.jugadoresConDosOMasImpagas;
    const porcentajeHabilitados = r.totalJugadores > 0 ? Math.round((r.jugadoresSinDeuda / r.totalJugadores) * 100) : 0;

    const pagosDelMesDisplay = signal('0');
    const recaudadoDisplay = signal(formatCompactCurrency(0));
    const cuotasAlDiaDisplay = signal('0%');
    const jugadoresActivosDisplay = signal('0');
    const jugadoresHabilitadosDisplay = signal('0');
    const ingresadoEsteMesDisplay = signal(CURRENCY_FULL.format(0));
    const deudaAcumuladaDisplay = signal(CURRENCY_FULL.format(0));

    this.bannerStats = [
      { value: pagosDelMesDisplay, label: 'Pagos del mes' },
      { value: recaudadoDisplay, label: `Recaudado ${new Date().getFullYear()}` },
      { value: cuotasAlDiaDisplay, label: 'Cuotas al día', hint: CUOTAS_AL_DIA_HINT },
    ];

    const sinVariacion = 'Sin variación mensual';
    const trends = r.trends;
    // HU-099: en el orden de la historia, cada una enlazada a la pantalla con su detalle.
    this.cards = [
      {
        value: jugadoresActivosDisplay,
        label: 'Jugadores activos',
        detail: `Distribuidos en ${r.cantidadCategorias} categorías`,
        badge: `${r.cantidadCategorias} cat.`,
        badgeTone: 'neutral',
        progress: 100,
        progressColor: '#00a651',
        route: PANTALLAS.cuotas.route,
        destination: PANTALLAS.cuotas.name,
        finalValue: String(r.totalJugadores),
        trend: describeTrend(trends?.activePlayers),
      },
      {
        value: deudaAcumuladaDisplay,
        label: 'Deuda acumulada',
        detail: `${jugadoresConRestricciones} jugadores con cuotas impagas`,
        badge: String(jugadoresConRestricciones),
        badgeTone: 'neutral',
        progress: r.totalJugadores > 0 ? Math.round((jugadoresConRestricciones / r.totalJugadores) * 100) : 0,
        progressColor: '#c0392b',
        route: PANTALLAS.deudas.route,
        destination: PANTALLAS.deudas.name,
        finalValue: CURRENCY_FULL.format(r.deudaAcumulada),
        trend: describeTrend(trends?.accumulatedDebt, sinVariacion),
      },
      {
        value: ingresadoEsteMesDisplay,
        label: 'Ingresado este mes',
        detail: `${r.pagosDelMes} pagos registrados`,
        badge: '',
        badgeTone: 'neutral',
        progress: r.ingresadoMesAnterior > 0 ? Math.min(100, Math.round((r.ingresadoEsteMes / r.ingresadoMesAnterior) * 100)) : 0,
        progressColor: '#8dd49d',
        route: PANTALLAS.recaudacion.route,
        destination: PANTALLAS.recaudacion.name,
        finalValue: CURRENCY_FULL.format(r.ingresadoEsteMes),
        trend: describeTrend(trends?.monthRevenue),
      },
      {
        value: jugadoresHabilitadosDisplay,
        label: 'Jugadores habilitados',
        detail: `${jugadoresConRestricciones} con restricciones`,
        badge: `${porcentajeHabilitados}%`,
        badgeTone: porcentajeHabilitados >= 80 ? 'positive' : porcentajeHabilitados >= 50 ? 'neutral' : 'negative',
        progress: porcentajeHabilitados,
        progressColor: '#4cb863',
        route: PANTALLAS.deudas.route,
        destination: PANTALLAS.deudas.name,
        finalValue: String(r.jugadoresSinDeuda),
        trend: describeTrend(trends?.enabledPlayers, sinVariacion),
      },
    ];

    // Los números "de vida" del panel cuentan de 0 hasta el valor real en vez
    // de aparecer de golpe (mismo criterio que el banner de Deudas y
    // Morosidad y de Actividad y Movimientos).
    this.cancelCountUp = animateCountUp(this.zone, [
      { target: r.pagosDelMes, display: pagosDelMesDisplay },
      { target: r.recaudadoAnioActual, display: recaudadoDisplay, format: formatCompactCurrency },
      { target: porcentajeHabilitados, display: cuotasAlDiaDisplay, format: (v) => `${v}%` },
      { target: r.totalJugadores, display: jugadoresActivosDisplay },
      { target: r.jugadoresSinDeuda, display: jugadoresHabilitadosDisplay },
      { target: r.ingresadoEsteMes, display: ingresadoEsteMesDisplay, format: (v) => CURRENCY_FULL.format(v) },
      { target: r.deudaAcumulada, display: deudaAcumuladaDisplay, format: (v) => CURRENCY_FULL.format(v) },
    ]);

    this.revenueData = this.mapMonthlyRevenue(r.recaudacionMensual);

    this.eligibility = [
      { tone: 'success', title: `${r.jugadoresSinDeuda} habilitados`, description: 'Sin cuotas vencidas' },
      { tone: 'warning', title: `${r.jugadoresConUnaImpaga} habilitación parcial`, description: '1 cuota vencida: solo entrena' },
      { tone: 'danger', title: `${r.jugadoresConDosOMasImpagas} inhabilitados`, description: '2 o más cuotas vencidas' },
    ];

    this.trendData = this.mapTrend(r.coberturaPagoMensual);

    this.suspendedPlayers = r.mayorDeudaPendiente.map((p) => ({
      id: p.idJugador,
      name: p.nombreCompleto,
      category: p.categoria,
      debtLabel: `${p.cantidadCuotas} ${p.cantidadCuotas === 1 ? 'cuota' : 'cuotas'}`,
    }));
  }

  // El gráfico dicho en texto para el lector de pantalla: "Sep 2026 $1,2 M, Oct 2026 $85 k".
  get revenueSummary(): string {
    const montos = (this.revenueData.datasets[0]?.data ?? []) as number[];
    return this.revenueTitles.map((titulo, i) => `${titulo} ${formatCompactCurrency(montos[i] ?? 0)}`).join(', ');
  }

  // Nombre accesible de una tarjeta: qué es, cuánto vale, cómo varió y adónde lleva.
  cardAriaLabel(card: SummaryCard): string {
    return `${card.label}: ${card.finalValue}. ${card.trend.spoken}. Ver ${card.destination}`;
  }

  // El último mes (el más reciente) va resaltado. minBarLength: un mes con cobros nunca queda en 0 px aunque al
  // lado haya uno mucho mayor; el valor real se lee en el eje y en el tooltip.
  mapMonthlyRevenue(puntos: MonthlyCollectionPointModel[]): ChartConfiguration<'bar'>['data'] {
    const ultimo = puntos.length - 1;
    this.revenueTitles = puntos.map((p) => `${p.mes} ${p.anio}`);
    return {
      labels: puntos.map((p) => p.mes),
      datasets: [
        {
          data: puntos.map((p) => p.monto),
          backgroundColor: puntos.map((_, i) => (i === ultimo ? '#00a651' : '#8dd49d')),
          hoverBackgroundColor: puntos.map((_, i) => (i === ultimo ? '#008f45' : '#5fc17f')),
          borderRadius: 6,
          maxBarThickness: 42,
          minBarLength: 6,
        },
      ],
    };
  }

  private mapTrend(puntos: MonthlyCoveragePointModel[]): ChartConfiguration<'line'>['data'] {
    const n = puntos.length;
    const primero = puntos[0]?.porcentaje ?? 0;
    const ultimo = puntos[n - 1]?.porcentaje ?? 0;
    this.trendBadgePositive = ultimo >= primero;
    this.trendBadgeText = this.trendBadgePositive ? '↑ Tendencia positiva' : '↓ Tendencia negativa';

    return {
      labels: puntos.map((p) => p.mes),
      datasets: [
        {
          data: puntos.map((p) => p.porcentaje),
          borderColor: '#00a651',
          borderWidth: 2.5,
          fill: true,
          backgroundColor: 'rgba(231, 244, 234, 0.4)',
          pointBackgroundColor: puntos.map((_, i) => (i === n - 1 ? '#00a651' : '#ffffff')),
          pointBorderColor: '#00a651',
          pointBorderWidth: 2,
          pointRadius: puntos.map((_, i) => (i === n - 1 ? 6 : 4)),
          pointHoverRadius: 6,
          tension: 0.35,
        },
      ],
    };
  }

  // Builds a Spanish long date without depending on locale registration
  private formatToday(): string {
    const today = new Date();
    const day = this.dayNames[today.getDay()];
    const month = this.monthNames[today.getMonth()];
    return `${day} ${today.getDate()} de ${month} de ${today.getFullYear()}`;
  }
}
