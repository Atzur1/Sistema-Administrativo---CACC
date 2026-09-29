import { CommonModule } from '@angular/common';
import { HttpClient, HttpParams } from '@angular/common/http';
import { ChangeDetectorRef, Component, NgZone, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { API_BASE_URL } from '../../services/api-url';
import { AuthService } from '../../services/auth';
import { CustomSelect } from '../../shared/custom-select/custom-select';

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

@Component({
  selector: 'app-auditoria',
  standalone: true,
  imports: [CommonModule, FormsModule, CustomSelect],
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
  readonly actions = [
    { value: 'INSERT', label: 'Agregado' },
    { value: 'UPDATE', label: 'Actualizado' },
    { value: 'DELETE', label: 'Eliminado' },
  ];
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

  accionLegible(item: AuditEvent): string {
    if (this.esCuotaSaldada(item)) return 'Cuota saldada';
    return this.actions.find((action) => action.value === item.action)?.label ?? item.action;
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
    const labels: Record<string, string> = {
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
    return labels[entity] ?? entity.replaceAll('_', ' ').toLocaleLowerCase('es-AR');
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
