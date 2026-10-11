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
}
