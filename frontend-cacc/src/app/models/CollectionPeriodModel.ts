// Un cobro del período, para la tabla "Pagos del período".
export interface PeriodPaymentModel {
  idPago: number;
  jugador: string;
  dni: string;
  categoria: string;
  monto: number;
  medioPago: string; // 'Efectivo' | 'Transferencia'
}

// Lo recaudado en un mes, ya separado por medio de pago (HU-018). Los dos
// subtotales suman el total: la base los calcula sobre las mismas filas.
// Los porcentajes no viajan, los deriva la pantalla.
export interface CollectionPeriodModel {
  total: number;
  efectivo: number;
  transferencia: number;
  pagos: number;
  pagosEfectivo: number;
  pagosTransferencia: number;
  detalle: PeriodPaymentModel[];
}
