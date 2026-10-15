import { CommonModule } from '@angular/common';
import { HttpClient, HttpParams } from '@angular/common/http';
import { ChangeDetectorRef, Component, NgZone, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { API_BASE_URL } from '../../services/api-url';
import { AuthService } from '../../services/auth';
import { CustomSelect } from '../../shared/custom-select/custom-select';
import { CustomDatepicker } from '../../shared/custom-datepicker/custom-datepicker';

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
}

interface AuditResponse {
  page: number;
  pageSize: number;
  total: number;
  items: AuditEvent[];
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

@Component({
  selector: 'app-auditoria',
  standalone: true,
  imports: [CommonModule, FormsModule, CustomSelect, CustomDatepicker],
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

  nombreCompleto(event: AuditEvent): string {
    const name = [event.userName, event.userSurname].filter(Boolean).join(' ').trim();
    return (
      name || (event.userId === null ? 'Acción del sistema' : 'Identidad no cargada en la cuenta')
    );
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

  campos(json: string | null): Array<{ label: string; value: string }> {
    if (!json) return [];
    try {
      const values = JSON.parse(json) as Record<string, unknown>;
      const labels: Record<string, string> = {
        idJugador: 'Jugador',
        idPersona: 'Persona',
        idCategoria: 'División',
        montoBase: 'Importe original',
        montoFinal: 'Importe actualizado',
        monto: 'Importe',
        fechaPago: 'Fecha de pago',
        metodoPago: 'Medio de pago',
        vencimiento: 'Vencimiento',
        estado: 'Estado',
        concepto: 'Concepto',
        genero: 'Rama',
        vigente_desde: 'Vigente desde',
        idDescuento: 'Beneficio',
        activo: 'Activo',
        tipoValor: 'Tipo de valor',
        porcentaje: 'Porcentaje',
        montoFijo: 'Importe fijo',
        desde: 'Vigente desde',
        hasta: 'Vigente hasta',
        fechaCancelacion: 'Fecha de baja',
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
      return Object.entries(values).map(([key, value]) => ({
        label: labels[key] ?? key.replaceAll('_', ' '),
        value: this.valorLegible(value, key),
      }));
    } catch {
      return [{ label: 'Información', value: 'No hay detalle disponible.' }];
    }
  }

  private valorLegible(value: unknown, key: string): string {
    if (value === null || value === undefined || value === '') return '—';
    if (key === 'estado' && typeof value === 'number') return value === 1 ? 'Pagado' : 'Pendiente';
    if (key === 'activo' || key === 'activacionPendiente' || key === 'credencialActualizada') {
      return value === true || value === 1 ? 'Sí' : 'No';
    }
    if (typeof value === 'boolean') return value ? 'Sí' : 'No';
    if (key === 'tipo' && value === 'RECUPERACION') return 'Reestablecer contraseña';
    if (key === 'tipo' && value === 'ACTIVACION') return 'Crear contraseña (primer ingreso)';
    if (typeof value === 'number' && key.toLocaleLowerCase().includes('monto')) {
      return new Intl.NumberFormat('es-AR', {
        style: 'currency',
        currency: 'ARS',
        maximumFractionDigits: 2,
      }).format(value);
    }
    if (typeof value === 'string' && /^\d{4}-\d\d-\d\d/.test(value)) {
      const date = new Date(value);
      return Number.isNaN(date.getTime())
        ? value
        : new Intl.DateTimeFormat('es-AR', { dateStyle: 'medium' }).format(date);
    }
    return String(value);
  }
}
