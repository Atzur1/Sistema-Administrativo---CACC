export interface PaymentHistoryPartialPaymentModel {
  idPago: number; // fila de PAGOS del abono: la que se anula con anularPago()
  monto: number;
  metodoPago: string;
  fechaPago: string;
  fechaHoraRegistro?: string | null;
  responsableNombre?: string | null;
  responsableApellido?: string | null;
}

export interface PaymentHistoryItemModel {
  periodo: string;
  concepto: 'Cuota' | 'Inscripcion';
  montoTotal: number; // Lo efectivamente cobrado
  montoOriginal: number; // El valor completo de la cuota, antes del beneficio (si tuvo uno)
  tieneBeneficio: boolean;
  motivoBeneficio: string | null;
  tipoValorBeneficio: string | null; // "%" o "$"
  porcentajeBeneficio: number | null;
  montoFijoBeneficio: number | null;
  abonos: PaymentHistoryPartialPaymentModel[];
}

export interface PaymentHistoryModel {
  items: PaymentHistoryItemModel[];
  total: number;
  page: number;
  pageSize: number;
}
