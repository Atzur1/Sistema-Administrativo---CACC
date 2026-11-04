// Un arancel es por género O por categoría: el que no aplica viene null. Si la categoría de un
// jugador tiene arancel propio, ese gana; si no, se usa el de su género.
export interface TariffHistoryItemModel {
  idArancel: number;
  genero: 'Masculino' | 'Femenino' | null;
  idCategoria: number | null;
  nombreCategoria: string | null;
  monto: number;
  vigenteDesde: string;
  vigenteHasta: string | null; // null = sigue vigente, todavía no hay uno más nuevo después
  estado: 'Vigente' | 'Programado' | 'Anterior';
  // Solo se cancelan los aranceles del mes en curso en adelante; los anteriores son historial.
  puedeCancelar: boolean;
  responsableNombre?: string | null;
  responsableApellido?: string | null;
}

export interface TariffSummaryModel {
  arancelMasculinoVigente: number | null;
  arancelFemeninoVigente: number | null;
  proximoCambioFecha: string | null;
}

// HU-106 / HU-069: cómo se interpreta el valor. 'absoluto' = monto final, 'porcentaje' = % sobre el arancel vigente,
// 'montoFijo' = $ que se suman al arancel vigente. El cálculo lo hace el backend.
export type TariffChangeType = 'absoluto' | 'porcentaje' | 'montoFijo';

// El cambio que se simula y, si se aprueba, se confirma: género O categoría (uno solo).
export interface TariffChangeRequest {
  genero?: string;
  idCategoria?: number;
  tipo: TariffChangeType;
  valor: number;
  vigenteDesde: string;
}

// Jugadores alcanzados de una categoría y género, con su cuota actual (null: hoy no tienen arancel ese mes).
export interface TariffImpactLineModel {
  idCategoria: number | null;
  nombreCategoria: string | null;
  genero: string;
  montoActual: number | null;
  montoNuevo: number;
  jugadores: number;
  diferenciaPorJugador: number;
  diferenciaTotal: number;
}

// Jugadores del alcance que no cambian porque su categoría tiene arancel propio (HU-048).
export interface TariffImpactExcludedModel {
  idCategoria: number | null;
  nombreCategoria: string | null;
  jugadores: number;
}

// HU-106: resumen de impacto calculado por el backend (POST /aranceles/simular). huella identifica este resumen: la
// confirmación se acepta solo si, recalculado al grabar, sigue siendo el mismo.
export interface TariffImpactModel {
  tipo: TariffChangeType;
  valor: number;
  genero: string | null;
  idCategoria: number | null;
  nombreCategoria: string | null;
  vigenteDesde: string;
  montoBase: number | null;
  montoNuevo: number;
  mesDeAplicacion: string;
  reemplazadoDesde: string | null;
  lineas: TariffImpactLineModel[];
  excluidos: TariffImpactExcludedModel[];
  jugadores: number;
  categorias: number;
  diferenciaMensual: number;
  mesEnCurso: boolean;
  cuotasDelMesPendientesSinPagos: number;
  cuotasDelMesConPagos: number;
  seEmiteElDia: string | null;
  requiereConfirmacionReforzada: boolean;
  huella: string;
}

export interface ScheduleTariffResponseModel {
  exito: boolean;
  mensaje: string;
  // El impacto que se grabó (en un 409, el impacto actual que hay que volver a revisar).
  impacto?: TariffImpactModel | null;
}
