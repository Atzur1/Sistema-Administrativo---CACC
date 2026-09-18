import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { BaseChartDirective } from 'ng2-charts';
import { Chart, ChartConfiguration, registerables } from 'chart.js';
import {
  EstadisticasService,
  PuntoCoberturaMensual,
  PuntoRecaudacionMensual,
  ResumenGeneralInfo,
} from '../../services/estadisticas';
import { AuthService } from '../../services/auth';
import { formatCompactCurrency } from '../../shared/format-currency';

Chart.register(...registerables);

// One metric card in the KPI row
interface SummaryCard {
  value: string;
  label: string;
  detail: string;
  badge: string;
  badgeTone: 'positive' | 'negative' | 'neutral';
  progress: number;
  progressColor: string;
}

// One bar in the monthly revenue chart
interface MonthlyBar {
  month: string;
  amount: string;
  height: number;
  current: boolean;
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

@Component({
  selector: 'app-resumen-general',
  standalone: true,
  imports: [CommonModule, BaseChartDirective],
  templateUrl: './resumen-general.html',
  styleUrl: './resumen-general.css',
})
export class ResumenGeneral implements OnInit {

  currentDate = '';
  greeting = '';
  cargando = true;

  bannerStats: { value: string; label: string }[] = [];
  cards: SummaryCard[] = [];
  monthlyRevenue: MonthlyBar[] = [];
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
    this.cargarResumen();
  }

  private buildGreeting(): string {
    const hora = new Date().getHours();
    const momento = hora < 12 ? 'Buenos días' : hora < 20 ? 'Buenas tardes' : 'Buenas noches';
    const nombre = this.authService.getUsuario()?.email?.split('@')[0] ?? 'Admin';
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

  private aplicarResumen(r: ResumenGeneralInfo) {
    const jugadoresConRestricciones = r.jugadoresConUnaImpaga + r.jugadoresConDosOMasImpagas;
    const porcentajeHabilitados = r.totalJugadores > 0 ? Math.round((r.jugadoresSinDeuda / r.totalJugadores) * 100) : 0;
    const cambioMensual =
      r.ingresadoMesAnterior > 0
        ? Math.round(((r.ingresadoEsteMes - r.ingresadoMesAnterior) / r.ingresadoMesAnterior) * 100)
        : null;

    this.bannerStats = [
      { value: String(r.pagosDelMes), label: 'Pagos del mes' },
      { value: formatCompactCurrency(r.recaudadoAnioActual), label: `Recaudado ${new Date().getFullYear()}` },
      { value: `${porcentajeHabilitados}%`, label: 'Cuotas al día' },
    ];

    this.cards = [
      {
        value: String(r.totalJugadores),
        label: 'Jugadores activos',
        detail: `Distribuidos en ${r.cantidadCategorias} categorías`,
        badge: `${r.cantidadCategorias} cat.`,
        badgeTone: 'neutral',
        progress: 100,
        progressColor: '#00a651',
      },
      {
        value: String(r.jugadoresSinDeuda),
        label: 'Jugadores habilitados',
        detail: `${jugadoresConRestricciones} con restricciones`,
        badge: `${porcentajeHabilitados}%`,
        badgeTone: porcentajeHabilitados >= 80 ? 'positive' : porcentajeHabilitados >= 50 ? 'neutral' : 'negative',
        progress: porcentajeHabilitados,
        progressColor: '#4cb863',
      },
      {
        value: CURRENCY_FULL.format(r.ingresadoEsteMes),
        label: 'Ingresado este mes',
        detail: `${r.pagosDelMes} pagos registrados`,
        badge: cambioMensual === null ? '' : `${cambioMensual >= 0 ? '+' : ''}${cambioMensual}%`,
        badgeTone: cambioMensual === null ? 'neutral' : cambioMensual >= 0 ? 'positive' : 'negative',
        progress: r.ingresadoMesAnterior > 0 ? Math.min(100, Math.round((r.ingresadoEsteMes / r.ingresadoMesAnterior) * 100)) : 0,
        progressColor: '#8dd49d',
      },
      {
        value: CURRENCY_FULL.format(r.deudaAcumulada),
        label: 'Deuda acumulada',
        detail: `${jugadoresConRestricciones} jugadores con cuotas impagas`,
        badge: String(jugadoresConRestricciones),
        badgeTone: 'neutral',
        progress: r.totalJugadores > 0 ? Math.round((jugadoresConRestricciones / r.totalJugadores) * 100) : 0,
        progressColor: '#c0392b',
      },
    ];

    this.monthlyRevenue = this.mapMonthlyRevenue(r.recaudacionMensual);

    this.eligibility = [
      { tone: 'success', title: `${r.jugadoresSinDeuda} habilitados`, description: 'Sin cuotas pendientes' },
      { tone: 'warning', title: `${r.jugadoresConUnaImpaga} habilitación parcial`, description: '1 cuota pendiente' },
      { tone: 'danger', title: `${r.jugadoresConDosOMasImpagas} inhabilitados`, description: '2 o más cuotas pendientes' },
    ];

    this.trendData = this.mapTrend(r.coberturaPagoMensual);

    this.suspendedPlayers = r.mayorDeudaPendiente.map((p) => ({
      id: p.idJugador,
      name: p.nombreCompleto,
      category: p.categoria,
      debtLabel: `${p.cantidadCuotas} ${p.cantidadCuotas === 1 ? 'cuota' : 'cuotas'}`,
    }));
  }

  private mapMonthlyRevenue(puntos: PuntoRecaudacionMensual[]): MonthlyBar[] {
    const max = Math.max(1, ...puntos.map((p) => p.monto));
    return puntos.map((p, i) => ({
      month: p.mes,
      amount: formatCompactCurrency(p.monto),
      height: Math.round((p.monto / max) * 100),
      current: i === puntos.length - 1,
    }));
  }

  private mapTrend(puntos: PuntoCoberturaMensual[]): ChartConfiguration<'line'>['data'] {
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
