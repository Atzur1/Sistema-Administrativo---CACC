// Un cobro del período, para la tabla "Pagos del período".
export interface PeriodPaymentModel {
  idPago: number;
  jugador: string;
  dni: string;
  categoria: string;
  monto: number;
  medioPago: string; // 'Efectivo' | 'Transferencia'
  // HU-107: día del cobro, "2026-10-31" (el que lo ubica en el período).
  fechaPago: string;
  // HU-107: hora real de registro, ya en hora del club y con su corrimiento: "2026-10-31T14:35:12-03:00".
  // null en los pagos anteriores a que existiera esa columna.
  fechaHoraRegistro: string | null;
}

// Lo recaudado en un mes, ya separado por medio de pago (HU-018). Los dos
// subtotales suman el total: la base los calcula sobre las mismas filas.
// Los porcentajes no viajan, los deriva la pantalla.
// HU-107: siempre del período completo; las filas llegan aparte y paginadas.
export interface CollectionPeriodModel {
  total: number;
  efectivo: number;
  transferencia: number;
  pagos: number;
  pagosEfectivo: number;
  pagosTransferencia: number;
}

// HU-107: orden de la tabla, que aplica el servidor.
export type PeriodPaymentsSortBy = 'fecha' | 'monto';
export type SortDirection = 'asc' | 'desc';

export interface PeriodPaymentsQuery {
  anio: number;
  mes: number;
  idCategoria: number | null;
  page: number;
  pageSize: number;
  sortBy: PeriodPaymentsSortBy;
  sortDirection: SortDirection;
}

// HU-107: una página de la tabla. total es la cantidad de cobros de TODO el período.
export interface PeriodPaymentsPageModel {
  items: PeriodPaymentModel[];
  total: number;
  pagina: number;
  tamanioPagina: number;
  totalPaginas: number;
}
