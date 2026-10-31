// El estado deportivo lo calcula el backend (la misma regla que la API de integración con el Portal Deportivo).
export type EstadoDeportivo = 'habilitado' | 'solo_entrenamientos' | 'inhabilitado' | 'baja';

export interface PendingDebtorModel {
  idJugador: number;
  nombreCompleto: string;
  dni: string;
  idCategoria: number;
  categoria: string;
  montoTotal: number;
  cantidadCuotas: number;
  // Las que ya vencieron (el 10 del mes siguiente): de acá sale el estado deportivo.
  cantidadVencidas: number;
  // "yyyy-MM-dd" si el jugador está dado de baja (puede seguir debiendo), null si está activo.
  fechaBaja?: string | null;
  // Calculado por el backend: la pantalla lo muestra, no lo recalcula.
  estadoDeportivo: EstadoDeportivo;
}

export interface CategoryDebtModel {
  idCategoria: number;
  categoria: string;
  montoTotal: number;
  cantidadJugadores: number;
}

export interface RecentPaymentModel {
  idPago: number;
  idJugador: number;
  nombreCompleto: string;
  categoria: string;
  periodo: string;
  metodoPago: string;
  monto: number;
  estado: 'Pagado' | 'Parcial';
  fechaPago: string;
  concepto: 'Cuota' | 'Inscripcion';
  // Momento exacto en que se grabó el pago; null para los registrados antes de que
  // existiera esta columna, que solo tienen el día (fechaPago).
  fechaHoraRegistro: string | null;
  responsableNombre?: string | null;
  responsableApellido?: string | null;
}

// Filtros y página de "Pagos registrados" (los resuelve la API).
export interface RegisteredPaymentsFilterModel {
  pagina: number;
  tamanioPagina: number;
  busqueda?: string | null;
  estado?: 'Pagado' | 'Parcial' | null;
}

export interface RegisteredPaymentsPageModel {
  items: RecentPaymentModel[];
  total: number; // cuántos cumplen los filtros
  totalSinFiltros: number;
  pagina: number;
  tamanioPagina: number;
}

export interface TodayPaymentSummaryModel {
  pagosHoy: number;
  recaudadoHoy: number;
}

export interface PaymentSummaryModel {
  recaudadoAnioActual: number;
  pagosDelMes: number;
  cantidadPendientes: number;
  // HU-019: indicador destacado "Deuda Global Total" — suma de cada cuota pendiente por su
  // monto ya congelado al mes de emisión (no el arancel vigente hoy), y cantidad de jugadores
  // únicos con al menos una cuota con saldo real > 0.
  deudaGlobalTotal: number;
  jugadoresMorosos: number;
}

export interface PartialPaymentDetailModel {
  monto: number;
  metodoPago: string;
  fechaPago: string;
  fechaHoraRegistro?: string | null;
  responsableNombre?: string | null;
  responsableApellido?: string | null;
}

export interface PendingFeeDetailModel {
  idPago: number;
  periodo: string;
  montoOriginal: number;
  saldoPendiente: number; // Ya con el beneficio de Becados y Descuentos aplicado, si tiene uno
  tieneBeneficio: boolean;
  motivoBeneficio: string | null;
  tipoValorBeneficio: string | null; // "%" o "$"
  porcentajeBeneficio: number | null;
  montoFijoBeneficio: number | null;
  // El beneficio de este período se canceló al dar de baja al jugador: quedó saldo, se muestra "Beca cancelada por baja".
  beneficioCanceladoPorBaja?: boolean;
  abonos: PartialPaymentDetailModel[];
}

// HU-025: estado real de una cuota mensual del jugador (calculado por la API).
export type FeeStatus = 'Pendiente' | 'Vencido' | 'Pagado';

// HU-024: contrato de GET /api/pagos/jugador/{id}/estado-de-cuenta (PlayerStatementDTO).
// Solo cuotas mensuales: la inscripción se abona por separado.
export interface PlayerStatementModel {
  playerId: number;
  playerFullName: string;
  dni: string;
  totalDebtAmount: number; // Saldo de las Pendientes y Vencidas, con beneficios y abonos aplicados
  fees: FeeItemModel[]; // En orden cronológico
}

export interface FeeItemModel {
  id: number; // Pendiente/Vencido: la cuota a cobrar. Pagado: el último cobro del período.
  periodName: string; // "Marzo 2026"
  amount: number; // Monto congelado con que se emitió la cuota
  amountDue: number; // Lo que se cobra hoy (0 si está pagada o cubierta)
  amountPaid: number;
  dueDate: string; // "2026-04-10": la cuota de marzo vence (y queda vencida) el 10 de abril
  status: FeeStatus;
  paidAt: string | null;
  paymentMethod: string | null;
  coveredByBenefit: boolean;
  benefitReason: string | null;
  // Quedó saldo y el beneficio de ese período se canceló al dar de baja al jugador.
  benefitCancelledByLeave?: boolean;
}

export interface PlayerFeeModel {
  idPago: number; // Pendiente/Vencido: la cuota a cobrar. Pagado: el último cobro del período.
  periodo: string; // "Marzo 2026"
  fechaVencimiento: string; // Estado de cuenta: el día en que vence (el 10 del mes siguiente al que cubre)
  montoCuota: number; // Importe histórico con que se emitió
  saldoPendiente: number; // Lo que se cobra hoy (ya con beneficio y abonos parciales)
  montoAbonado: number;
  estado: FeeStatus;
  cubiertaPorBeneficio: boolean;
  motivoBeneficio: string | null;
  beneficioCanceladoPorBaja?: boolean;
  metodoPago: string | null;
  fechaPago: string | null;
}

export interface CollectedFeeModel {
  idPago: number;
  periodo: string;
  monto: number;
  estado: FeeStatus;
}

export interface CollectFeesResponseModel {
  exito: boolean;
  idJugador: number;
  pagosAbonados: number[];
  cuotas: CollectedFeeModel[];
  montoTotal: number;
  metodoPago: string;
  fechaPago: string;
  fechaHoraRegistro: string;
  estado: FeeStatus;
  mensaje: string;
}

export interface RegisterPaymentResponseModel {
  exito: boolean;
  idPago: number;
  idJugador: number;
  periodo: string;
  monto: number;
  metodoPago: string;
  fechaPago: string;
  mensaje: string;
}
