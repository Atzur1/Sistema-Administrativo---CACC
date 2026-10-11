import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { PlayerAccountModel } from '../models/PlayerAccountModel';
import { API_BASE_URL } from './api-url';

export interface JugadorResumen {
  idJugador: number;
  nombre: string;
  apellido: string;
  dni: string;
  genero: string; // "Masculino" | "Femenino"
  categoria: string;
  nombreCompleto: string;
  fechaAlta?: string | null;
  // null = activo. Con fecha, el jugador dejó el club y no se le emiten más cuotas.
  fechaBaja?: string | null;
}

export interface PendienteJugador {
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
}

export interface CategoriaDeuda {
  idCategoria: number;
  categoria: string;
  montoTotal: number;
  cantidadJugadores: number;
}

// HU-020: catálogo completo de categorías (las 13 del club), independiente de
// quién debe o no — para el selector de "Deudas y Morosidad".
export interface Categoria {
  idCategoria: number;
  nombre: string;
}

export interface PagoReciente {
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
export interface FiltroPagosRegistrados {
  pagina: number;
  tamanioPagina: number;
  busqueda?: string | null;
  estado?: 'Pagado' | 'Parcial' | null;
}

export interface PagosRegistradosPagina {
  items: PagoReciente[];
  total: number; // cuántos cumplen los filtros
  totalSinFiltros: number;
  pagina: number;
  tamanioPagina: number;
}

export interface ResumenPagosHoy {
  pagosHoy: number;
  recaudadoHoy: number;
}

export interface ResumenPagos {
  recaudadoAnioActual: number;
  pagosDelMes: number;
  cantidadPendientes: number;
  // HU-019: indicador destacado "Deuda Global Total" — suma de cada cuota pendiente por su
  // monto ya congelado al mes de emisión (no el arancel vigente hoy), y cantidad de jugadores
  // únicos con al menos una cuota con saldo real > 0.
  deudaGlobalTotal: number;
  jugadoresMorosos: number;
}

export interface AbonoDetalle {
  monto: number;
  metodoPago: string;
  fechaPago: string;
  fechaHoraRegistro?: string | null;
  responsableNombre?: string | null;
  responsableApellido?: string | null;
}

export interface CuotaPendienteDetalle {
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
  abonos: AbonoDetalle[];
}

// HU-025: una cuota mensual del jugador con su estado real (calculado por la API).
export type EstadoCuotaJugador = 'Pendiente' | 'Vencido' | 'Pagado';

// HU-024: contrato de GET /api/pagos/jugador/{id}/estado-de-cuenta (PlayerStatementDto).
// Solo cuotas mensuales: la inscripción se abona por separado.
export interface PlayerStatement {
  playerId: number;
  playerFullName: string;
  dni: string;
  totalDebtAmount: number; // Saldo de las Pendientes y Vencidas, con beneficios y abonos aplicados
  fees: FeeItem[]; // En orden cronológico
}

export interface FeeItem {
  id: number; // Pendiente/Vencido: la cuota a cobrar. Pagado: el último cobro del período.
  periodName: string; // "Marzo 2026"
  amount: number; // Monto congelado con que se emitió la cuota
  amountDue: number; // Lo que se cobra hoy (0 si está pagada o cubierta)
  amountPaid: number;
  dueDate: string; // "2026-04-10": la cuota de marzo vence (y queda vencida) el 10 de abril
  status: EstadoCuotaJugador;
  paidAt: string | null;
  paymentMethod: string | null;
  coveredByBenefit: boolean;
  benefitReason: string | null;
  // Quedó saldo y el beneficio de ese período se canceló al dar de baja al jugador.
  benefitCancelledByLeave?: boolean;
}

// La tabla de cobro de la ficha (HU-025) trabaja con CuotaJugador: el estado de cuenta se
// adapta a esa forma para no duplicar la lógica de selección y cobro.
export function feeToCuotaJugador(fee: FeeItem): CuotaJugador {
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

export interface CuotaJugador {
  idPago: number; // Pendiente/Vencido: la cuota a cobrar. Pagado: el último cobro del período.
  periodo: string; // "Marzo 2026"
  fechaVencimiento: string; // Estado de cuenta: el día en que vence (el 10 del mes siguiente al que cubre)
  montoCuota: number; // Importe histórico con que se emitió
  saldoPendiente: number; // Lo que se cobra hoy (ya con beneficio y abonos parciales)
  montoAbonado: number;
  estado: EstadoCuotaJugador;
  cubiertaPorBeneficio: boolean;
  motivoBeneficio: string | null;
  beneficioCanceladoPorBaja?: boolean;
  metodoPago: string | null;
  fechaPago: string | null;
}

export interface CuotaCobrada {
  idPago: number;
  periodo: string;
  monto: number;
  estado: EstadoCuotaJugador;
}

export interface CobrarCuotasResponse {
  exito: boolean;
  idJugador: number;
  pagosAbonados: number[];
  cuotas: CuotaCobrada[];
  montoTotal: number;
  metodoPago: string;
  fechaPago: string;
  fechaHoraRegistro: string;
  estado: EstadoCuotaJugador;
  mensaje: string;
}

export interface RegistrarPagoResponse {
  exito: boolean;
  idPago: number;
  idJugador: number;
  periodo: string;
  monto: number;
  metodoPago: string;
  fechaPago: string;
  mensaje: string;
}

@Injectable({ providedIn: 'root' })
export class PagosService {
  private readonly API_URL = API_BASE_URL;

  constructor(private http: HttpClient) {}

  getJugadores(): Observable<JugadorResumen[]> {
    return this.http.get<JugadorResumen[]>(`${this.API_URL}/jugadores`);
  }

  // idCategoria: HU-020, acota el padrón a esa categoría/división del lado del servidor.
  getPendientes(idCategoria?: number | null): Observable<PendienteJugador[]> {
    const url =
      idCategoria != null
        ? `${this.API_URL}/pagos/pendientes?idCategoria=${idCategoria}`
        : `${this.API_URL}/pagos/pendientes`;
    return this.http.get<PendienteJugador[]>(url);
  }

  // HU-029: con onlyDebtors la propia API devuelve solo a los jugadores que deben algo
  getPlayerAccounts(onlyDebtors: boolean): Observable<PlayerAccountModel[]> {
    return this.http.get<PlayerAccountModel[]>(
      `${this.API_URL}/pagos/player-accounts?onlyDebtors=${onlyDebtors}`,
    );
  }

  // mes: si se omite, trae la deuda del año completo en vez de un mes puntual.
  getDeudaPorCategoria(anio: number, mes?: number | null): Observable<CategoriaDeuda[]> {
    const params = mes != null ? `anio=${anio}&mes=${mes}` : `anio=${anio}`;
    return this.http.get<CategoriaDeuda[]>(`${this.API_URL}/pagos/deuda-por-categoria?${params}`);
  }

  // HU-020: las 13 categorías del club, tengan o no jugadores morosos hoy.
  getCategorias(): Observable<Categoria[]> {
    return this.http.get<Categoria[]>(`${this.API_URL}/categorias`);
  }

  getRecientes(top: number = 10): Observable<PagoReciente[]> {
    return this.http.get<PagoReciente[]>(`${this.API_URL}/pagos/recientes?top=${top}`);
  }

  // Tabla "Pagos registrados" de Actividad y Movimientos: la API filtra y pagina (solo baja la página pedida).
  getPagosRegistrados(filtro: FiltroPagosRegistrados): Observable<PagosRegistradosPagina> {
    const params: Record<string, string | number> = { page: filtro.pagina, pageSize: filtro.tamanioPagina };
    if (filtro.busqueda?.trim()) params['busqueda'] = filtro.busqueda.trim();
    if (filtro.estado) params['estado'] = filtro.estado;
    return this.http.get<PagosRegistradosPagina>(`${this.API_URL}/pagos/registrados`, { params });
  }

  getResumen(): Observable<ResumenPagos> {
    return this.http.get<ResumenPagos>(`${this.API_URL}/pagos/resumen`);
  }

  getResumenHoy(): Observable<ResumenPagosHoy> {
    return this.http.get<ResumenPagosHoy>(`${this.API_URL}/pagos/resumen-hoy`);
  }

  getDeuda(idJugador: number): Observable<CuotaPendienteDetalle[]> {
    return this.http.get<CuotaPendienteDetalle[]>(`${this.API_URL}/pagos/deuda/${idJugador}`);
  }

  // HU-025: cuotas del jugador (pendientes, vencidas y pagadas) para la tabla de cobro.
  getCuotas(idJugador: number): Observable<CuotaJugador[]> {
    return this.http.get<CuotaJugador[]>(`${this.API_URL}/pagos/cuotas/${idJugador}`);
  }

  // HU-024: estado de cuenta del jugador (encabezado, total a abonar y cuotas con su estado).
  getEstadoDeCuenta(idJugador: number): Observable<PlayerStatement> {
    return this.http.get<PlayerStatement>(`${this.API_URL}/pagos/jugador/${idJugador}/estado-de-cuenta`);
  }

  // HU-025: cobro atómico de las cuotas seleccionadas. El operador no viaja en el body: la API
  // lo toma del token de la sesión.
  cobrarCuotas(idJugador: number, idsPago: number[], metodoPago: string): Observable<CobrarCuotasResponse> {
    return this.http.post<CobrarCuotasResponse>(`${this.API_URL}/pagos/cobro`, {
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
  ): Observable<RegistrarPagoResponse> {
    return this.http.post<RegistrarPagoResponse>(`${this.API_URL}/pagos/registrar`, {
      idJugador,
      periodo,
      anio,
      monto,
      metodoPago,
    });
  }
}
