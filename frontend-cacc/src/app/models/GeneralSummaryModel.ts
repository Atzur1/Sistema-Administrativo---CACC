import { PendingDebtorModel } from './PaymentModel';

export interface MonthlyCollectionPointModel {
  mes: string;
  anio: number;
  monto: number;
}

export interface MonthlyCoveragePointModel {
  mes: string;
  anio: number;
  porcentaje: number;
}

// HU-099: variación de una tarjeta contra el mes anterior. changePercentage es entero; null sin base para comparar.
export type TrendDirection = 'up' | 'down' | 'flat' | 'none';

export interface KpiTrendModel {
  changePercentage: number | null;
  direction: TrendDirection;
}

export interface SummaryTrendsModel {
  activePlayers: KpiTrendModel;
  accumulatedDebt: KpiTrendModel;
  monthRevenue: KpiTrendModel;
  enabledPlayers: KpiTrendModel;
}

export interface GeneralSummaryModel {
  totalJugadores: number;
  cantidadCategorias: number;
  pagosDelMes: number;
  recaudadoAnioActual: number;
  ingresadoEsteMes: number;
  ingresadoMesAnterior: number;
  deudaAcumulada: number;
  jugadoresSinDeuda: number;
  jugadoresConUnaImpaga: number;
  jugadoresConDosOMasImpagas: number;
  recaudacionMensual: MonthlyCollectionPointModel[];
  coberturaPagoMensual: MonthlyCoveragePointModel[];
  mayorDeudaPendiente: PendingDebtorModel[];
  trends: SummaryTrendsModel;
}
