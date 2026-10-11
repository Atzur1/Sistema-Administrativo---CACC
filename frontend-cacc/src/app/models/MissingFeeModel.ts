// Un mes que quedó con cuotas sin generar (por ejemplo, porque el sistema estuvo apagado el día 10).
export interface MonthWithMissingFeesModel {
  periodo: string; // "2026-09-01": primer día del mes
  cantidad: number;
  montoTotal: number;
}

// Una cuota que se generaría: a quién y por cuánto (su arancel de ese mes).
export interface MissingFeeModel {
  idJugador: number;
  nombreCompleto: string;
  dni: string;
  categoria: string;
  monto: number;
}
