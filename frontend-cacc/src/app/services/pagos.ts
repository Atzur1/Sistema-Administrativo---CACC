import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { PlayerAccountModel } from '../models/PlayerAccountModel';
import { PlayerSummaryModel } from '../models/PlayerSummaryModel';
import { CategoryModel } from '../models/CategoryModel';
import {
  CategoryDebtModel,
  CollectFeesResponseModel,
  FeeItemModel,
  PaymentSummaryModel,
  PendingDebtorModel,
  PendingFeeDetailModel,
  PlayerFeeModel,
  PlayerStatementModel,
  RecentPaymentModel,
  RegisteredPaymentsFilterModel,
  RegisteredPaymentsPageModel,
  RegisterPaymentResponseModel,
  TodayPaymentSummaryModel,
} from '../models/PaymentModel';
import { API_BASE_URL } from './api-url';

// La tabla de cobro de la ficha (HU-025) trabaja con PlayerFeeModel: el estado de cuenta se
// adapta a esa forma para no duplicar la lógica de selección y cobro.
export function feeToCuotaJugador(fee: FeeItemModel): PlayerFeeModel {
  return {
    idPago: fee.id,
    periodo: fee.periodName,
    fechaVencimiento: fee.dueDate,
    montoCuota: fee.amount,
    saldoPendiente: fee.amountDue,
    montoAbonado: fee.amountPaid,
    estado: fee.status,
    cubiertaPorBeneficio: fee.coveredByBenefit,
    motivoBeneficio: fee.benefitReason,
    beneficioCanceladoPorBaja: fee.benefitCancelledByLeave ?? false,
    metodoPago: fee.paymentMethod,
    fechaPago: fee.paidAt,
  };
}

@Injectable({ providedIn: 'root' })
export class PagosService {
  private readonly API_URL = API_BASE_URL;

  constructor(private http: HttpClient) {}

  getJugadores(): Observable<PlayerSummaryModel[]> {
    return this.http.get<PlayerSummaryModel[]>(`${this.API_URL}/jugadores`);
  }

  // idCategoria: HU-020, acota el padrón a esa categoría/división del lado del servidor.
  getPendientes(idCategoria?: number | null): Observable<PendingDebtorModel[]> {
    const url =
      idCategoria != null
        ? `${this.API_URL}/pagos/pendientes?idCategoria=${idCategoria}`
        : `${this.API_URL}/pagos/pendientes`;
    return this.http.get<PendingDebtorModel[]>(url);
  }

  // HU-029: con onlyDebtors la propia API devuelve solo a los jugadores que deben algo
  getPlayerAccounts(onlyDebtors: boolean): Observable<PlayerAccountModel[]> {
    return this.http.get<PlayerAccountModel[]>(
      `${this.API_URL}/pagos/player-accounts?onlyDebtors=${onlyDebtors}`,
    );
  }

  // mes: si se omite, trae la deuda del año completo en vez de un mes puntual.
  getDeudaPorCategoria(anio: number, mes?: number | null): Observable<CategoryDebtModel[]> {
    const params = mes != null ? `anio=${anio}&mes=${mes}` : `anio=${anio}`;
    return this.http.get<CategoryDebtModel[]>(`${this.API_URL}/pagos/deuda-por-categoria?${params}`);
  }

  // HU-020: las 13 categorías del club, tengan o no jugadores morosos hoy.
  getCategorias(): Observable<CategoryModel[]> {
    return this.http.get<CategoryModel[]>(`${this.API_URL}/categorias`);
  }

  getRecientes(top: number = 10): Observable<RecentPaymentModel[]> {
    return this.http.get<RecentPaymentModel[]>(`${this.API_URL}/pagos/recientes?top=${top}`);
  }

  // Tabla "Pagos registrados" de Actividad y Movimientos: la API filtra y pagina (solo baja la página pedida).
  getPagosRegistrados(filtro: RegisteredPaymentsFilterModel): Observable<RegisteredPaymentsPageModel> {
    const params: Record<string, string | number> = { page: filtro.pagina, pageSize: filtro.tamanioPagina };
    if (filtro.busqueda?.trim()) params['busqueda'] = filtro.busqueda.trim();
    if (filtro.estado) params['estado'] = filtro.estado;
    return this.http.get<RegisteredPaymentsPageModel>(`${this.API_URL}/pagos/registrados`, { params });
  }

  getResumen(): Observable<PaymentSummaryModel> {
    return this.http.get<PaymentSummaryModel>(`${this.API_URL}/pagos/resumen`);
  }

  getResumenHoy(): Observable<TodayPaymentSummaryModel> {
    return this.http.get<TodayPaymentSummaryModel>(`${this.API_URL}/pagos/resumen-hoy`);
  }

  getDeuda(idJugador: number): Observable<PendingFeeDetailModel[]> {
    return this.http.get<PendingFeeDetailModel[]>(`${this.API_URL}/pagos/deuda/${idJugador}`);
  }

  // HU-025: cuotas del jugador (pendientes, vencidas y pagadas) para la tabla de cobro.
  getCuotas(idJugador: number): Observable<PlayerFeeModel[]> {
    return this.http.get<PlayerFeeModel[]>(`${this.API_URL}/pagos/cuotas/${idJugador}`);
  }

  // HU-024: estado de cuenta del jugador (encabezado, total a abonar y cuotas con su estado).
  getEstadoDeCuenta(idJugador: number): Observable<PlayerStatementModel> {
    return this.http.get<PlayerStatementModel>(`${this.API_URL}/pagos/jugador/${idJugador}/estado-de-cuenta`);
  }

  // HU-025: cobro atómico de las cuotas seleccionadas. El operador no viaja en el body: la API
  // lo toma del token de la sesión.
  cobrarCuotas(idJugador: number, idsPago: number[], metodoPago: string): Observable<CollectFeesResponseModel> {
    return this.http.post<CollectFeesResponseModel>(`${this.API_URL}/pagos/cobro`, {
      idJugador,
      idsPago,
      metodoPago,
    });
  }

  registrarPago(
    idJugador: number,
    periodo: string,
    anio: number,
    monto: number,
    metodoPago: string,
  ): Observable<RegisterPaymentResponseModel> {
    return this.http.post<RegisterPaymentResponseModel>(`${this.API_URL}/pagos/registrar`, {
      idJugador,
      periodo,
      anio,
      monto,
      metodoPago,
    });
  }
}
