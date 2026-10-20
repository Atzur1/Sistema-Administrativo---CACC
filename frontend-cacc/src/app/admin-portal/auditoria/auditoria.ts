import { CommonModule } from '@angular/common';
import { HttpClient, HttpParams } from '@angular/common/http';
import { ChangeDetectorRef, Component, NgZone, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { API_BASE_URL } from '../../services/api-url';
import { AuthService } from '../../services/auth';
import { CustomSelect } from '../../shared/custom-select/custom-select';
import { CustomDatepicker } from '../../shared/custom-datepicker/custom-datepicker';
import { Cargando } from '../../shared/cargando/cargando';
import { EstadoVacio } from '../../shared/estado-vacio/estado-vacio';
import { Ventana } from '../../shared/ventana/ventana';

interface AuditEvent {
  id: number;
  userId: number | null;
  userEmail: string | null;
  userName: string | null;
  userSurname: string | null;
  userDni: string | null;
  entity: string;
  entityId: string;
  action: string;
  timestampUtc: string;
  beforeJson: string | null;
  afterJson: string | null;
  // Los nombres de lo que el evento menciona solo por número, como "tipo:número" -> texto (por ejemplo
  // "jugador:3" -> "PRUEBA, NICOLAS (DNI 99000001)"). Lo que ya no existe en la base no figura.
  references?: Record<string, string>;
}

interface AuditResponse {
  page: number;
  pageSize: number;
  total: number;
  items: AuditEvent[];
}

// Un campo con su valor ya legible, para mostrar un dato suelto.
export interface CampoAuditoria {
  label: string;
  value: string;
}

// Un campo que cambió: lo que valía antes y lo que vale ahora.
export interface FilaCambio {
  label: string;
  antes: string;
  despues: string;
}

// Lo que muestra el panel de un evento:
//  - alta: solo hay datos nuevos ("Datos cargados"),
//  - baja: solo hay datos viejos ("Datos eliminados"),
//  - cambio: se compara y se destaca solo lo que cambió; el resto queda plegado,
//  - sin-datos: la operación no guardó una copia de los datos.
export interface DetalleEvento {
  modo: 'alta' | 'baja' | 'cambio' | 'sin-datos';
  datos: CampoAuditoria[];
  cambiados: FilaCambio[];
  iguales: FilaCambio[];
}

// Un filtro puesto, para mostrarlo como etiqueta que se puede quitar con una ×.
export interface FiltroActivo {
  clave: 'email' | 'from' | 'to' | 'entity' | 'action';
  etiqueta: string;
}

// Nombre legible de cada sección auditada: etiqueta las filas y arma las opciones del filtro "Sección".
const SECCIONES: Record<string, string> = {
  PAGOS: 'Pago',
  ARANCELES: 'Arancel mensual',
  ARANCELES_INSCRIPCION: 'Arancel de inscripción',
  JUGADORES_DESCUENTOS: 'Beneficio de jugador',
  JUGADORES: 'Ficha de jugador',
  PERSONA: 'Datos personales',
  USUARIO: 'Cuenta administrativa',
  CATEGORIAS: 'División',
  TIPO_DESCUENTO: 'Tipo de beneficio',
  TOKEN_ACCESO_CUENTA: 'Acceso a cuenta',
};

// Los datos que la copia guarda solo como número de otra tabla, y de qué tabla es (ver DaoLibrary.AuditReferences).
// El servidor manda el nombre en `references`; si ya no existe en la base, se muestra el número y la etiqueta lo dice.
const TIPO_DE_REFERENCIA: Record<string, string> = {
  idJugador: 'jugador',
  idPersona: 'persona',
  idCategoria: 'categoria',
  idTipo: 'tipo',
  idUsuario: 'usuario',
  idDescuento: 'beneficio',
};

// Estos se rotulan "(n.º)" cuando solo hay número. La cuenta afectada no: "Cuenta afectada: 7" se lee bien igual.
const SIN_SUFIJO_NUMERO = new Set(['idUsuario']);

// Los nombres internos de cada dato guardado en la auditoría, en castellano.
const ETIQUETAS: Record<string, string> = {
  idJugador: 'Jugador',
  idPersona: 'Persona',
  idCategoria: 'División',
  montoBase: 'Importe original',
  montoFinal: 'Importe actualizado',
  monto: 'Importe',
  fechaPago: 'Fecha de pago',
  metodoPago: 'Medio de pago',
  vencimiento: 'Cuota del período',
  estado: 'Estado',
  concepto: 'Concepto',
  genero: 'Rama',
  vigente_desde: 'Vigente desde',
  idDescuento: 'Beneficio',
  idTipo: 'Motivo del beneficio',
  rolPortal: 'Rol en el portal',
  accesoPortal: 'Acceso al portal',
  activo: 'Activo',
  anulado: 'Anulada',
  canceladaPorBaja: 'Cancelada por la baja del jugador',
  tipoValor: 'Tipo de valor',
  porcentaje: 'Porcentaje',
  montoFijo: 'Importe fijo',
  desde: 'Vigente desde',
  hasta: 'Vigente hasta',
  fechaCancelacion: 'Cancelada hasta',
  nombre_categoria: 'Nombre de división',
  tipo_descuento: 'Nombre del beneficio',
  email: 'Correo',
  nombre: 'Nombre',
  apellido: 'Apellido',
  dni: 'DNI',
  idRol: 'Rol',
  activacionPendiente: 'Activación pendiente',
  credencialActualizada: 'Contraseña actualizada',
  idUsuario: 'Cuenta afectada',
  tipo: 'Tipo de enlace',
  venceUtc: 'Vencimiento del enlace',
  consumidoUtc: 'Enlace utilizado',
};

// Datos que se guardan como 0/1 (o true/false) y se leen "Sí" / "No".
const CLAVES_SI_NO = new Set(['activo', 'anulado', 'canceladaPorBaja', 'activacionPendiente', 'credencialActualizada', 'accesoPortal']);

const MESES = [
  'Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio',
  'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre',
];

// Sin decimales cuando el importe es entero ("$ 80.000"), como en el resto del sistema.
const PESOS = new Intl.NumberFormat('es-AR', { style: 'currency', currency: 'ARS', minimumFractionDigits: 0, maximumFractionDigits: 2 });

type Datos = Record<string, unknown>;

@Component({
  selector: 'app-auditoria',
  standalone: true,
  imports: [CommonModule, FormsModule, CustomSelect, CustomDatepicker, Ventana, Cargando, EstadoVacio],
  templateUrl: './auditoria.html',
  styleUrl: './auditoria.css',
})
export class Auditoria {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly ngZone = inject(NgZone);
  private readonly cdr = inject(ChangeDetectorRef);

  readonly pageSize = 50;
  // Sin la entrada "Todas las acciones": el placeholder de app-custom-select
  // ya cubre ese estado, igual que en el resto de los filtros de la app
  // (deudas-morosidad, actividad-movimientos, becados-descuentos).
  //
  // Solo estas tres: es lo único que le interesa ver al club de un vistazo.
  // Son valores propios (no INSERT/UPDATE/DELETE crudos) porque cada uno ya
  // implica su sección — "Pagos realizados" es un alta en PAGOS, "Arancel
  // actualizado" es cualquier cambio en ARANCELES — el backend resuelve esa
  // combinación (ver AuditDao.Search). "Cuota saldada" quedó afuera del
  // filtro a propósito: sigue existiendo como etiqueta de fila (ver
  // accionLegible), pero no como algo que se pueda buscar aparte.
  readonly actions = [
    { value: 'PAGO_REALIZADO', label: 'Pagos realizados' },
    { value: 'ARANCEL_ACTUALIZADO', label: 'Arancel actualizado' },
    { value: 'DELETE', label: 'Eliminado' },
  ];
  // Sección: antes era texto libre que tenía que coincidir con el nombre interno de la tabla (para ver los
  // enlaces de contraseña había que escribir "TOKEN_ACCESO_CUENTA"). Sin elegir ninguna, el backend muestra
  // solo pagos y aranceles (ver AuditDao.Search); el resto se ve eligiéndolo acá.
  readonly secciones = Object.entries(SECCIONES).map(([value, label]) => ({ value, label }));

  // Accesos rápidos de período: ponen "Desde" / "Hasta" de una vez.
  readonly rangos = [
    { clave: 'hoy', etiqueta: 'Hoy', dias: 0 },
    { clave: '7', etiqueta: 'Últimos 7 días', dias: 6 },
    { clave: '30', etiqueta: 'Últimos 30 días', dias: 29 },
  ] as const;

  items: AuditEvent[] = [];
  total = 0;
  page = 1;
  from = '';
  to = '';
  email = '';
  entity = '';
  action = '';
  loading = false;
  error = '';

  // "Más filtros" (período a medida, sección y tipo de cambio) arranca plegado: lo de todos los días es buscar
  // una persona o elegir un período rápido.
  masFiltros = false;

  // Evento abierto en el panel lateral y su detalle ya armado (se calcula una vez al abrir).
  seleccionado: AuditEvent | null = null;
  detalleActual: DetalleEvento | null = null;
  mostrarIguales = false;

  private temporizadorBusqueda: ReturnType<typeof setTimeout> | undefined;

  get pages(): number {
    return Math.max(1, Math.ceil(this.total / this.pageSize));
  }

  get firstVisible(): number {
    return this.total === 0 ? 0 : (this.page - 1) * this.pageSize + 1;
  }

  get lastVisible(): number {
    return Math.min(this.page * this.pageSize, this.total);
  }

  constructor() {
    this.search(1);
  }

  // ===== FILTROS =====

  // Botón separado de un simple type="reset": app-custom-select no es un
  // <select> nativo, así que el reset del navegador no lo sincroniza — acá
  // se resetea cada propiedad explícitamente y se vuelve a buscar sin filtros.
  limpiarFiltros(): void {
    this.from = '';
    this.to = '';
    this.email = '';
    this.entity = '';
    this.action = '';
    this.search(1);
  }

  // Los filtros se aplican solos: no hay que apretar "Aplicar". Los de elegir (fechas, sección, tipo) buscan al
  // instante; el de texto espera un instante a que termine de escribir.
  buscarAhora(): void {
    clearTimeout(this.temporizadorBusqueda);
    this.search(1);
  }

  buscarAlEscribir(): void {
    clearTimeout(this.temporizadorBusqueda);
    this.temporizadorBusqueda = setTimeout(() => this.search(1), 400);
  }

  // "Hoy", "Últimos 7 días", etc. Elegir el que ya está puesto lo quita.
  aplicarRango(dias: number): void {
    const hasta = this.hoyLocal();
    const desde = this.sumarDias(hasta, -dias);
    const yaPuesto = this.from === desde && this.to === hasta;
    this.from = yaPuesto ? '' : desde;
    this.to = yaPuesto ? '' : hasta;
    this.buscarAhora();
  }

  rangoActivo(dias: number): boolean {
    return this.from === this.sumarDias(this.hoyLocal(), -dias) && this.to === this.hoyLocal();
  }

  get filtrosActivos(): FiltroActivo[] {
    const lista: FiltroActivo[] = [];
    if (this.email.trim()) lista.push({ clave: 'email', etiqueta: `Persona: ${this.email.trim()}` });
    if (this.from) lista.push({ clave: 'from', etiqueta: `Desde ${this.fechaCorta(this.from)}` });
    if (this.to) lista.push({ clave: 'to', etiqueta: `Hasta ${this.fechaCorta(this.to)}` });
    if (this.entity) lista.push({ clave: 'entity', etiqueta: `Sección: ${this.entidadLegible(this.entity)}` });
    if (this.action) {
      const accion = this.actions.find((a) => a.value === this.action);
      lista.push({ clave: 'action', etiqueta: `Tipo: ${accion?.label ?? this.action}` });
    }
    return lista;
  }

  // Cuántos de los filtros de "Más filtros" están puestos (para el número del botón).
  get cantidadMasFiltros(): number {
    return [this.from, this.to, this.entity, this.action].filter(Boolean).length;
  }

  quitarFiltro(clave: FiltroActivo['clave']): void {
    this[clave] = '';
    this.buscarAhora();
  }

  alternarMasFiltros(): void {
    this.masFiltros = !this.masFiltros;
  }

  // Sin sección elegida el servidor muestra solo pagos y aranceles (ver AuditDao.Search): se avisa en pantalla
  // en vez de dejarlo como una regla escondida detrás de un texto de ayuda.
  get alcanceReducido(): boolean {
    return !this.entity;
  }

  search(page: number): void {
    this.loading = true;
    this.error = '';
    let params = new HttpParams().set('pagina', page).set('tamanoPagina', this.pageSize);
    if (this.from) params = params.set('desdeUtc', new Date(`${this.from}T00:00:00`).toISOString());
    if (this.to) params = params.set('hastaUtc', new Date(`${this.to}T23:59:59`).toISOString());
    if (this.email.trim()) params = params.set('emailUsuario', this.email.trim());
    if (this.entity.trim()) params = params.set('entidad', this.entity.trim());
    if (this.action) params = params.set('accion', this.action);

    this.http
      .get<AuditResponse>(`${API_BASE_URL}/auditoria/cambios`, {
        params,
        headers: { Authorization: `Bearer ${this.auth.getToken() ?? ''}` },
      })
      .subscribe({
        // ngZone.run() + cdr.detectChanges(): la respuesta de HttpClient no dispara
        // detección de cambios sola en este proyecto (mismo problema documentado en
        // login.ts) — sin esto, loading/items quedan actualizados por dentro pero la
        // pantalla se ve colgada en "Buscando operaciones..." hasta el próximo evento
        // que sí corra dentro de la zona (por eso "se destranca" al clickear afuera).
        next: (response) => {
          this.ngZone.run(() => {
            this.items = response.items;
            this.total = response.total;
            this.page = response.page;
            this.loading = false;
            this.cdr.detectChanges();
          });
        },
        error: () => {
          this.ngZone.run(() => {
            this.error = 'No pudimos cargar la auditoría. Revisá tu conexión e intentá nuevamente.';
            this.loading = false;
            this.cdr.detectChanges();
          });
        },
      });
  }

  // ===== HORA =====

  // Argentina no tiene horario de verano: siempre UTC-3. Angular no entiende nombres de zona como
  // "America/Argentina/Buenos_Aires" (los ignora y muestra la hora del navegador), por eso se usa el corrimiento fijo.
  readonly HUSO_CLUB = '-0300';

  // El momento del evento como fecha absoluta. La base lo guarda en UTC; si por algún motivo llega sin la "Z" final
  // se la agrega, porque sin ella el navegador lo tomaría por hora local y lo mostraría 3 horas adelantado.
  momento(item: AuditEvent): Date {
    const texto = item.timestampUtc;
    return new Date(/(Z|[+-]\d\d:?\d\d)$/i.test(texto) ? texto : `${texto}Z`);
  }

  // ===== PANEL DE DETALLE =====

  abrirDetalle(item: AuditEvent): void {
    this.seleccionado = item;
    this.detalleActual = this.detalle(item);
    this.mostrarIguales = false;
  }

  // Esc, la × y tocar afuera los resuelve la ventana compartida (app-ventana).
  cerrarDetalle(): void {
    this.seleccionado = null;
    this.detalleActual = null;
  }

  // Arma lo que muestra el panel. Alta y baja listan los datos; un cambio compara antes y después y deja a la
  // vista solo lo que cambió (el resto va plegado): ver 10 campos para encontrar los 2 que cambiaron era lo más
  // difícil de leer de esta pantalla.
  detalle(item: AuditEvent): DetalleEvento {
    const antes = this.leer(item.beforeJson);
    const despues = this.leer(item.afterJson);

    if (!antes && !despues) return { modo: 'sin-datos', datos: [], cambiados: [], iguales: [] };
    if (!antes) return { modo: 'alta', datos: this.campos(item.afterJson, item.references), cambiados: [], iguales: [] };
    if (!despues) return { modo: 'baja', datos: this.campos(item.beforeJson, item.references), cambiados: [], iguales: [] };

    const claves = [...new Set([...Object.keys(antes), ...Object.keys(despues)])];
    const cambiados: FilaCambio[] = [];
    const iguales: FilaCambio[] = [];
    for (const clave of claves) {
      const fila: FilaCambio = {
        label: this.etiqueta(clave, [antes[clave], despues[clave]], item.references),
        antes: this.valorLegible(antes[clave], clave, item.references),
        despues: this.valorLegible(despues[clave], clave, item.references),
      };
      (this.distinto(antes[clave], despues[clave]) ? cambiados : iguales).push(fila);
    }
    return { modo: 'cambio', datos: [], cambiados, iguales };
  }

  // La frase que cuenta qué pasó, para leer la lista sin abrir cada fila.
  resumen(item: AuditEvent): string {
    const antes = this.leer(item.beforeJson);
    const despues = this.leer(item.afterJson);
    const cambio = (clave: string) => !!antes && !!despues && this.distinto(antes[clave], despues[clave]);
    const siNo = (valor: unknown) => valor === true || valor === 1;

    // A quién le pasó, con su nombre si el servidor lo pudo resolver. Sin nombre, la frase queda genérica. En la
    // frase va solo el nombre: el DNI está en la línea de abajo y en el detalle, no repetido en las dos.
    const quien = this.sinDni(this.sujeto(item));

    switch (item.entity) {
      case 'PAGOS': {
        if (this.esCuotaSaldada(item)) return quien ? `Se saldó una cuota de ${quien}` : 'Se saldó una cuota';
        if (item.action === 'INSERT') {
          const importe = despues?.['montoFinal'] ?? despues?.['monto'];
          const medio = despues?.['metodoPago'];
          const monto = typeof importe === 'number' ? PESOS.format(importe) : null;
          const enMedio = typeof medio === 'string' && medio ? ` en ${medio.toLocaleLowerCase('es-AR')}` : '';
          return quien
            ? `Registró el pago de ${quien}${monto ? `: ${monto}` : ''}${enMedio}`
            : `Registró un pago${monto ? ` de ${monto}` : ''}${enMedio}`;
        }
        return item.action === 'DELETE' ? 'Eliminó un registro de pago' : 'Modificó un pago';
      }
      case 'ARANCELES':
        return quien ? `Actualizó el arancel de ${quien}` : 'Actualizó un arancel mensual';
      case 'ARANCELES_INSCRIPCION':
        return 'Actualizó el arancel de inscripción';
      case 'JUGADORES_DESCUENTOS': {
        const cuyo = quien ? `la bonificación de ${quien}` : 'una bonificación';
        if (item.action === 'INSERT') {
          return quien
            ? `Cargó una bonificación${this.valorBeneficio(despues)} a ${quien}`
            : 'Cargó una bonificación' + this.valorBeneficio(despues);
        }
        if (item.action === 'DELETE') return `Eliminó ${cuyo}`;
        if (cambio('anulado') && siNo(despues?.['anulado'])) return `Anuló ${cuyo}`;
        if (cambio('activo') && !siNo(despues?.['activo'])) return `Canceló ${cuyo}`;
        return `Modificó ${cuyo}`;
      }
      case 'USUARIO': {
        const cuenta = quien ? `la cuenta de ${quien}` : 'una cuenta';
        if (item.action === 'INSERT') return quien ? `Creó la cuenta de ${quien}` : 'Creó una cuenta administrativa';
        if (cambio('credencialActualizada') || cambio('activacionPendiente')) {
          return quien ? `Actualizó la contraseña de ${quien}` : 'Actualizó la contraseña de una cuenta';
        }
        if (cambio('activo')) return `${siNo(despues?.['activo']) ? 'Habilitó' : 'Deshabilitó'} ${cuenta}`;
        if (cambio('idRol') || cambio('rolPortal')) return `Cambió el rol de ${cuenta}`;
        return quien ? `Modificó la cuenta de ${quien}` : 'Modificó una cuenta administrativa';
      }
      case 'TOKEN_ACCESO_CUENTA': {
        if (item.action !== 'INSERT') return 'Se usó un enlace de contraseña';
        const contrasena = quien ? `la contraseña de ${quien}` : 'una contraseña';
        return despues?.['tipo'] === 'ACTIVACION'
          ? `Envió el enlace para crear ${contrasena}`
          : `Envió el enlace para restablecer ${contrasena}`;
      }
      case 'JUGADORES':
        if (item.action === 'INSERT') return quien ? `Dio de alta a ${quien}` : 'Dio de alta un jugador';
        return quien ? `Modificó la ficha de ${quien}` : 'Modificó la ficha de un jugador';
      case 'PERSONA':
        if (item.action === 'INSERT') return quien ? `Cargó los datos personales de ${quien}` : 'Cargó datos personales';
        return quien ? `Modificó los datos personales de ${quien}` : 'Modificó datos personales';
      default:
        return `${this.accionLegible(item)} · ${this.entidadLegible(item.entity)}`;
    }
  }

  // La segunda línea de cada fila: qué registro es (no repite el nombre, que ya está en la frase de arriba) y, si
  // el evento es de un jugador o una persona, su DNI para distinguir a dos con el mismo nombre.
  contexto(item: AuditEvent): string {
    const dni = /\(DNI (\d+)\)/.exec(this.sujeto(item) ?? '')?.[1];
    return `${this.entidadLegible(item.entity)} n.º ${item.entityId}${dni ? ` · DNI ${dni}` : ''}`;
  }

  // "PRUEBA, NICOLAS (DNI 99000001)" -> "PRUEBA, NICOLAS"
  private sinDni(texto: string | null): string | null {
    return texto === null ? null : texto.replace(/\s*\(DNI \d+\)\s*$/, '');
  }

  // A quién (jugador, cuenta, división...) corresponde el evento, con su nombre. Es null cuando el evento no se
  // refiere a nadie en particular o cuando lo mencionado ya no existe en la base.
  sujeto(item: AuditEvent): string | null {
    const datos = this.leer(item.afterJson) ?? this.leer(item.beforeJson);
    const numero = (campo: string): number | null => {
      const valor = datos?.[campo];
      return typeof valor === 'number' ? valor : null;
    };
    const nombre = (tipo: string, id: string | number | null): string | null =>
      id === null ? null : (item.references?.[`${tipo}:${id}`] ?? null);

    switch (item.entity) {
      case 'JUGADORES':
        return nombre('jugador', item.entityId);
      case 'PERSONA':
        return nombre('persona', item.entityId);
      case 'USUARIO':
        return nombre('usuario', item.entityId);
      case 'CATEGORIAS':
        return nombre('categoria', item.entityId);
      case 'TIPO_DESCUENTO':
        return nombre('tipo', item.entityId);
      case 'PAGOS':
      case 'JUGADORES_DESCUENTOS':
        return nombre('jugador', numero('idJugador'));
      case 'TOKEN_ACCESO_CUENTA':
        return nombre('usuario', numero('idUsuario'));
      case 'ARANCELES': {
        const division = nombre('categoria', numero('idCategoria'));
        const rama = typeof datos?.['genero'] === 'string' ? `rama ${(datos['genero'] as string).toLocaleLowerCase('es-AR')}` : null;
        return division ?? rama;
      }
      default:
        return null;
    }
  }

  private valorBeneficio(datos: Datos | null): string {
    if (!datos) return '';
    const porcentaje = datos['porcentaje'];
    const fijo = datos['montoFijo'];
    if (typeof porcentaje === 'number') return ` del ${porcentaje} %`;
    if (typeof fijo === 'number') return ` de ${PESOS.format(fijo)}`;
    return '';
  }

  // ===== FILAS =====

  nombreCompleto(event: AuditEvent): string {
    const name = [event.userName, event.userSurname].filter(Boolean).join(' ').trim();
    return (
      name || (event.userId === null ? 'Acción del sistema' : 'Identidad no cargada en la cuenta')
    );
  }

  // Segunda línea de la persona: el correo y, si hay, el DNI.
  datosDePersona(event: AuditEvent): string {
    return [event.userEmail || 'Usuario no identificado', event.userDni ? `DNI ${event.userDni}` : '']
      .filter(Boolean)
      .join(' · ');
  }

  iniciales(event: AuditEvent): string {
    const name = [event.userName, event.userSurname].filter(Boolean);
    return name.length
      ? name
          .map((part) => part![0])
          .join('')
          .slice(0, 2)
          .toUpperCase()
      : '—';
  }

  // Independiente del dropdown de filtro (ese manda valores propios al backend,
  // como PAGO_REALIZADO): esto etiqueta la fila a partir de la acción y la
  // sección crudas que vienen del backend, y también cubre secciones que no
  // están en el filtro (Usuarios, Jugadores, etc.) cuando se buscan a mano
  // con "Sección", para que no queden con la acción de SQL server sin traducir.
  private static readonly ACCIONES_GENERICAS: Record<string, string> = {
    INSERT: 'Agregado',
    UPDATE: 'Actualizado',
    DELETE: 'Eliminado',
  };

  accionLegible(item: AuditEvent): string {
    if (this.esCuotaSaldada(item)) return 'Cuota saldada';
    if (item.entity === 'PAGOS' && item.action === 'INSERT') return 'Pago realizado';
    if (item.entity === 'ARANCELES') return 'Arancel actualizado';
    // Alta de un enlace de contraseña (HU-068): el autor de la fila es quien lo pidió.
    if (item.entity === 'TOKEN_ACCESO_CUENTA' && item.action === 'INSERT') return 'Enlace enviado';
    return Auditoria.ACCIONES_GENERICAS[item.action] ?? item.action;
  }

  claseAccion(item: AuditEvent): string {
    return this.esCuotaSaldada(item) ? 'action-settled' : 'action-' + item.action.toLowerCase();
  }

  // PagosService.EliminarPago tiene un único llamador en todo el backend: cuando un abono cubre el
  // saldo total de una cuota pendiente, esa fila (el "rastreador" de deuda, no el pago en sí) se
  // borra porque ya cumplió su función — el comprobante del pago queda en la fila insertada aparte.
  // No es que alguien haya eliminado un pago: es una cuota que se saldó. Por eso el DELETE crudo de
  // la auditoría se traduce acá en vez de mostrarse como "Eliminado", que sugiere una pérdida de dato.
  private esCuotaSaldada(item: AuditEvent): boolean {
    if (item.entity !== 'PAGOS' || item.action !== 'DELETE' || !item.beforeJson) return false;
    try {
      const before = JSON.parse(item.beforeJson) as { estado?: boolean };
      return before.estado === false;
    } catch {
      return false;
    }
  }

  entidadLegible(entity: string): string {
    return SECCIONES[entity] ?? entity.replaceAll('_', ' ').toLocaleLowerCase('es-AR');
  }

  // ===== DATOS =====

  // La lista de datos de un solo estado (antes o después), con su nombre y valor legibles. `referencias` son los
  // nombres que mandó el servidor para los datos que vienen como número.
  campos(json: string | null, referencias?: Record<string, string>): CampoAuditoria[] {
    if (!json) return [];
    const values = this.leer(json);
    if (!values) return [{ label: 'Información', value: 'No hay detalle disponible.' }];
    return Object.entries(values).map(([key, value]) => ({
      label: this.etiqueta(key, [value], referencias),
      value: this.valorLegible(value, key, referencias),
    }));
  }

  private leer(json: string | null): Datos | null {
    if (!json) return null;
    try {
      const values = JSON.parse(json) as unknown;
      return values && typeof values === 'object' && !Array.isArray(values) ? (values as Datos) : null;
    } catch {
      return null;
    }
  }

  // El nombre del dato. Un dato que viene como número de otra tabla y que no se pudo traducir a un nombre (porque
  // ya no existe) lo dice: "Jugador (n.º)".
  private etiqueta(clave: string, valores: unknown[] = [], referencias?: Record<string, string>): string {
    const base = ETIQUETAS[clave] ?? clave.replaceAll('_', ' ');
    const tipo = TIPO_DE_REFERENCIA[clave];
    if (!tipo || SIN_SUFIJO_NUMERO.has(clave)) return base;

    const hayNumero = valores.some((valor) => typeof valor === 'number');
    const sinNombre = valores.some((valor) => typeof valor === 'number' && !referencias?.[`${tipo}:${valor}`]);
    return hayNumero && sinNombre ? `${base} (n.º)` : base;
  }

  // Compara dos valores como los muestra la pantalla: 1 y true son lo mismo, y el texto "50" igual al número 50.
  private distinto(antes: unknown, despues: unknown): boolean {
    const normalizar = (valor: unknown) =>
      valor === null || valor === undefined || valor === ''
        ? ''
        : typeof valor === 'boolean'
          ? String(Number(valor))
          : String(valor);
    return normalizar(antes) !== normalizar(despues);
  }

  private valorLegible(value: unknown, key: string, referencias?: Record<string, string>): string {
    if (value === null || value === undefined || value === '') return '—';
    // Un número de otra tabla se muestra con su nombre cuando el servidor lo mandó.
    const tipo = TIPO_DE_REFERENCIA[key];
    if (tipo && typeof value === 'number') {
      const nombre = referencias?.[`${tipo}:${value}`];
      if (nombre) return nombre;
    }
    if (key === 'rolPortal' && typeof value === 'number') return value === 1 ? 'SuperAdmin' : 'Administrador';
    if (key === 'estado' && typeof value === 'number') return value === 1 ? 'Pagado' : 'Pendiente';
    if (CLAVES_SI_NO.has(key)) return value === true || value === 1 ? 'Sí' : 'No';
    if (typeof value === 'boolean') return value ? 'Sí' : 'No';
    if (key === 'tipo' && value === 'RECUPERACION') return 'Reestablecer contraseña';
    if (key === 'tipo' && value === 'ACTIVACION') return 'Crear contraseña (primer ingreso)';
    if (typeof value === 'number' && key.toLocaleLowerCase().includes('monto')) {
      return PESOS.format(value);
    }
    // "vencimiento" guarda el período que cubre la cuota (el primer día de su mes), no el día en que vence:
    // se muestra como el mes, "Octubre 2026".
    if (key === 'vencimiento' && typeof value === 'string' && /^\d{4}-\d\d/.test(value)) {
      const [anio, mes] = value.split('-');
      return `${MESES[Number(mes) - 1] ?? ''} ${anio}`.trim();
    }
    // Una fecha sin hora se lee tal cual, partida a mano: pasarla por Date la toma como medianoche UTC y la
    // muestra un día antes en Argentina.
    if (typeof value === 'string' && /^\d{4}-\d\d-\d\d$/.test(value)) {
      const [anio, mes, dia] = value.split('-');
      return `${dia}/${mes}/${anio}`;
    }
    // Fecha con hora: se guarda en UTC y se muestra en hora del club.
    if (typeof value === 'string' && /^\d{4}-\d\d-\d\dT/.test(value)) {
      const instante = new Date(/(Z|[+-]\d\d:?\d\d)$/i.test(value) ? value : `${value}Z`);
      return Number.isNaN(instante.getTime())
        ? value
        : new Intl.DateTimeFormat('es-AR', {
            day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit',
            hour12: false, timeZone: 'America/Argentina/Buenos_Aires',
          }).format(instante).replace(', ', ' ') + ' hs';
    }
    return String(value);
  }

  // ===== FECHAS (día calendario del club, en texto yyyy-MM-dd) =====

  private hoyLocal(): string {
    const ahora = new Date();
    return `${ahora.getFullYear()}-${String(ahora.getMonth() + 1).padStart(2, '0')}-${String(ahora.getDate()).padStart(2, '0')}`;
  }

  private sumarDias(iso: string, dias: number): string {
    const [anio, mes, dia] = iso.split('-').map(Number);
    const fecha = new Date(anio, mes - 1, dia + dias);
    return `${fecha.getFullYear()}-${String(fecha.getMonth() + 1).padStart(2, '0')}-${String(fecha.getDate()).padStart(2, '0')}`;
  }

  private fechaCorta(iso: string): string {
    const [anio, mes, dia] = iso.split('-');
    return `${dia}/${mes}/${anio}`;
  }
}
