import { Injectable, OnDestroy, Signal, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable, Subscription } from 'rxjs';
import { AuthService } from './auth';
import { CuotasFaltantesService } from './cuotas-faltantes';
import { UsuariosPortalService } from './usuarios-portal';
import { MonthWithMissingFeesModel } from '../models/MissingFeeModel';
import { EditCandidateRequestModel, PortalUserCandidateModel } from '../models/PortalUserModel';

// HU-104: como máximo una consulta por fuente cada 60.000 ms, compartida por todos los que miran los pendientes
// (campanita, Resumen General, Usuarios y Permisos) dentro de esta pestaña. Con dos fuentes, un ciclo hace hasta dos
// GET (meses y candidatos), no uno solo. Es un límite del frontend: no limita a otros navegadores ni dispositivos.
export const PENDING_REFRESH_MS = 60_000;

// Estado de una fuente: 'idle' sin consultar, 'loading' primera consulta en curso, 'ok' la última respuesta llegó
// bien, 'error' la última falló (los datos de antes, si había, se conservan: un error no es "cero pendientes").
export type SourceStatus = 'idle' | 'loading' | 'ok' | 'error';

// Del conjunto: 'complete' las dos fuentes al día; 'partial' hay datos pero falta (o falló) alguna fuente.
export type PendingStatus = 'idle' | 'loading' | 'complete' | 'partial';

// Un pendiente que espera una decisión del SuperAdmin, con su enlace a la pantalla donde se resuelve.
export interface PendingAction {
  key: string; // estable: "cuotas:2026-09" o "candidato:20"
  kind: 'missing-fees' | 'candidate';
  label: string;
  link: string;
  queryParams?: Record<string, string>;
}

const NOMBRES_MES = [
  'enero', 'febrero', 'marzo', 'abril', 'mayo', 'junio',
  'julio', 'agosto', 'septiembre', 'octubre', 'noviembre', 'diciembre',
];

// Lo que hace falta para coordinar una fuente: sus datos, su estado y lo necesario para no pisarlos con respuestas viejas.
class Source<T> {
  readonly data = signal<T[] | null>(null);
  readonly status = signal<SourceStatus>('idle');
  lastAttempt: number | null = null;
  inFlight: Subscription | null = null;
  // Resoluciones confirmadas: clave -> número de orden en que se resolvió. Una respuesta de un pedido que empezó
  // antes las respeta; una de un pedido posterior ya las refleja (y entonces se olvidan).
  readonly resolved = new Map<string, number>();

  constructor(
    readonly fetch: () => Observable<T[]>,
    readonly keyOf: (item: T) => string,
  ) {}

  reset(): void {
    this.inFlight?.unsubscribe();
    this.inFlight = null;
    this.lastAttempt = null;
    this.resolved.clear();
    this.data.set(null);
    this.status.set('idle');
  }
}

// Coordinador único de los pendientes de acción (HU-104). Reutiliza las consultas existentes de cuotas faltantes y de
// candidatos, y mantiene su resultado aparte del historial de la campanita y de los toasts (NotificationService):
// leer o limpiar el historial no resuelve nada. Lo inicia el Portal Administrativo y lo detiene al salir de él
// (cierre de sesión o sesión vencida: las dos navegan fuera del portal).
@Injectable({ providedIn: 'root' })
export class PendingActionsService implements OnDestroy {
  private readonly auth = inject(AuthService);

  private readonly monthsSource = new Source<MonthWithMissingFeesModel>(
    () => this.cuotas.getMeses(),
    (m) => `cuotas:${m.periodo.slice(0, 7)}`,
  );
  private readonly candidatesSource = new Source<PortalUserCandidateModel>(
    () => this.usuarios.getCandidatos(),
    (c) => `candidato:${c.idUsuario}`,
  );

  private running = false;
  // La sesión con la que se pidió: una respuesta que llega con otra sesión (o sin ninguna) se descarta.
  private sessionToken: string | null = null;
  private timer: ReturnType<typeof setTimeout> | undefined;
  private sequence = 0;

  readonly months: Signal<MonthWithMissingFeesModel[] | null> = this.monthsSource.data.asReadonly();
  readonly candidates: Signal<PortalUserCandidateModel[] | null> = this.candidatesSource.data.asReadonly();
  readonly monthsStatus: Signal<SourceStatus> = this.monthsSource.status.asReadonly();
  readonly candidatesStatus: Signal<SourceStatus> = this.candidatesSource.status.asReadonly();

  readonly items = computed<PendingAction[]>(() => [
    ...(this.months() ?? []).map(toMonthAction),
    ...(this.candidates() ?? []).map(toCandidateAction),
  ]);

  readonly total = computed(() => this.items().length);

  readonly status = computed<PendingStatus>(() => {
    const estados = [this.monthsStatus(), this.candidatesStatus()];
    if (estados.every((e) => e === 'idle')) return 'idle';
    if (estados.every((e) => e === 'ok')) return 'complete';
    const conDatos = this.months() !== null || this.candidates() !== null;
    return conDatos || estados.includes('error') ? 'partial' : 'loading';
  });

  // Solo con las dos fuentes al día se puede afirmar que no hay pendientes.
  readonly confirmedEmpty = computed(() => this.status() === 'complete' && this.total() === 0);

  constructor(
    private readonly cuotas: CuotasFaltantesService,
    private readonly usuarios: UsuariosPortalService,
  ) {}

  // Idempotente: lo llaman el portal y las pantallas que muestran pendientes; solo la primera vez consulta. Navegar o
  // volver a llamarlo no reinicia el minuto. Solo para SuperAdmin: un Administrador no consulta nada.
  start(): void {
    if (this.running || !this.auth.isSuperAdmin()) return;
    this.running = true;
    this.sessionToken = this.auth.getToken();
    this.tick();
  }

  ngOnDestroy(): void {
    this.stop();
  }

  // Al salir del portal: deja de consultar, cancela lo que estaba en camino y borra los datos (son de esta sesión).
  stop(): void {
    this.running = false;
    this.sessionToken = null;
    clearTimeout(this.timer);
    this.timer = undefined;
    this.monthsSource.reset();
    this.candidatesSource.reset();
  }

  // Generación confirmada por el servidor: ese mes ya no tiene cuotas faltantes (la generación del mes es completa
  // y transaccional; "creadas = 0" significa que ya estaban todas).
  resolveMonth(periodo: string): void {
    this.resolve(this.monthsSource, `cuotas:${periodo.slice(0, 7)}`);
  }

  // Habilitación confirmada por el servidor (también si falló el correo: el acceso ya se otorgó).
  resolveCandidate(idUsuario: number): void {
    this.resolve(this.candidatesSource, `candidato:${idUsuario}`);
  }

  // Corrección de nombre, apellido o DNI confirmada por el servidor: sigue siendo candidata, con sus datos nuevos.
  updateCandidate(idUsuario: number, cambios: EditCandidateRequestModel): void {
    const actuales = this.candidatesSource.data();
    if (!actuales) return;
    this.candidatesSource.data.set(
      actuales.map((c) => (c.idUsuario === idUsuario
        ? { ...c, nombre: cambios.nombre, apellido: cambios.apellido, dni: cambios.dni.replace(/\D/g, '') }
        : c)),
    );
  }

  private resolve<T>(source: Source<T>, key: string): void {
    source.resolved.set(key, ++this.sequence);
    const actuales = source.data();
    if (actuales?.some((item) => source.keyOf(item) === key)) {
      source.data.set(actuales.filter((item) => source.keyOf(item) !== key));
    }
  }

  // Una sola vuelta del coordinador: consulta las fuentes que ya pueden y programa la próxima. Si la pestaña estuvo
  // suspendida, al volver hace a lo sumo un intento por fuente (no recupera los minutos perdidos).
  private tick(): void {
    this.timer = undefined;
    if (!this.running) return;
    if (!this.auth.isSuperAdmin() || this.auth.getToken() !== this.sessionToken) {
      this.stop();
      return;
    }
    const ahora = Date.now();
    for (const source of [this.monthsSource, this.candidatesSource] as Source<unknown>[]) {
      if (!source.inFlight && (source.lastAttempt === null || ahora - source.lastAttempt >= PENDING_REFRESH_MS)) {
        this.query(source);
      }
    }
    this.schedule();
  }

  // La próxima vuelta: cuando se cumpla el minuto de la fuente que antes lo cumpla (el plazo se mide desde que
  // empezó su último intento, haya salido bien o mal). Las que están en curso se reprograman al terminar.
  private schedule(): void {
    clearTimeout(this.timer);
    this.timer = undefined;
    if (!this.running) return;
    const proximos = [this.monthsSource, this.candidatesSource]
      .filter((s) => !s.inFlight && s.lastAttempt !== null)
      .map((s) => s.lastAttempt! + PENDING_REFRESH_MS);
    if (proximos.length === 0) return;
    this.timer = setTimeout(() => this.tick(), Math.max(0, Math.min(...proximos) - Date.now()));
  }

  private query<T>(source: Source<T>): void {
    const pedido = ++this.sequence;
    const token = this.sessionToken;
    source.lastAttempt = Date.now();
    if (source.data() === null) source.status.set('loading');

    const terminar = () => {
      source.inFlight = null;
      this.schedule();
    };

    const suscripcion = source.fetch().subscribe({
      next: (items) => {
        if (!this.running || this.auth.getToken() !== token) return;
        // Lo resuelto después de que empezó este pedido no puede volver; lo resuelto antes ya lo refleja el servidor.
        for (const [key, resuelto] of source.resolved) {
          if (resuelto < pedido) source.resolved.delete(key);
        }
        source.data.set(items.filter((item) => !source.resolved.has(source.keyOf(item))));
        source.status.set('ok');
      },
      error: (error: unknown) => {
        if (!this.running || this.auth.getToken() !== token) return;
        // Sin autorización no se insiste: no queda un bucle de pedidos protegidos (el 401 además cierra la sesión).
        if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) {
          this.stop();
          return;
        }
        source.status.set('error');
        terminar();
      },
      complete: terminar,
    });
    // Si respondió en el acto (ya cerrada), no queda "en curso": si no, esa fuente no volvería a consultarse.
    source.inFlight = suscripcion.closed ? null : suscripcion;
    if (suscripcion.closed) this.schedule();
  }
}

// "2026-09-01" -> septiembre de 2026, leyendo el texto (sin Date: no cambia de mes por la zona horaria).
function toMonthAction(mes: MonthWithMissingFeesModel): PendingAction {
  const [anio, numero] = mes.periodo.slice(0, 7).split('-').map(Number);
  return {
    key: `cuotas:${mes.periodo.slice(0, 7)}`,
    kind: 'missing-fees',
    label: `Cuotas de ${NOMBRES_MES[numero - 1]} de ${anio} sin generar (${mes.cantidad})`,
    link: '/admin/portal/resumen-general',
  };
}

// Solo el nombre: el DNI y el correo no hacen falta para reconocer a la persona en un aviso.
function toCandidateAction(c: PortalUserCandidateModel): PendingAction {
  const nombre = [c.nombre, c.apellido].map((p) => (p ?? '').trim()).filter(Boolean).join(' ') || 'una persona sin nombre cargado';
  return {
    key: `candidato:${c.idUsuario}`,
    kind: 'candidate',
    label: `Revisar acceso de ${nombre}`,
    link: '/admin/portal/usuarios',
    queryParams: { pestana: 'candidatos' },
  };
}
