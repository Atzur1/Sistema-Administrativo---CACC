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

export interface ScheduleTariffResponseModel {
  exito: boolean;
  mensaje: string;
}
