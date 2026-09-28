import { ChangeDetectorRef, Component, OnDestroy, OnInit, signal } from '@angular/core';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { AbstractControl, FormBuilder, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import {
  JugadorResumen,
  PagoReciente,
  PagosService,
  PendienteJugador,
  ResumenPagos,
} from '../../services/pagos';
import { ArancelesService } from '../../services/aranceles';
import { EnrollmentFeeService } from '../../services/enrollment-fees';
import { findCurrentEnrollmentFee } from '../../models/EnrollmentFeeModel';
import { EnrollmentService } from '../../services/enrollment';
import { EnrollmentModel } from '../../models/EnrollmentModel';
import { formatCompactCurrency } from '../../shared/format-currency';
import { CustomSelect } from '../../shared/custom-select/custom-select';
import { NotificationService } from '../../shared/notifications/notification.service';

const CURRENCY_ARANCEL = new Intl.NumberFormat('es-AR', {
  style: 'currency',
  currency: 'ARS',
  maximumFractionDigits: 0,
});

// One row in the "Pendientes de cobro" panel
interface PendingRow {
  id: number;
  initials: string;
  name: string;
  category: string;
  amount: string;
  installments: string;
}

// One row in the "Últimos pagos" panel
interface PaymentRow {
  id: number;
  idJugador: number;
  initials: string;
  name: string;
  method: string;
  amount: string;
  elapsed: string;
  concepto: 'Cuota' | 'Inscripcion';
}

interface HeaderMetric {
  value: string;
  label: string;
}

const CURRENCY_FULL = new Intl.NumberFormat('es-AR', {
  style: 'currency',
  currency: 'ARS',
  maximumFractionDigits: 0,
});

@Component({
  selector: 'app-cuotas-pagos',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, CustomSelect],
  templateUrl: './cuotas-pagos.html',
  styleUrl: './cuotas-pagos.css',
})
export class CuotasPagos implements OnInit, OnDestroy {

  headerMetrics: HeaderMetric[] = [
    { value: '—', label: `Recaudado ${new Date().getFullYear()}` },
    { value: '—', label: 'Pagos del mes' },
    { value: '—', label: 'Pendientes' },
  ];

  // HU-019: indicador destacado y ÚNICO (monto + cantidad de morosos juntos,
  // no como dos métricas separadas) — el criterio de aceptación pide
  // exactamente ese formato combinado, ej. "Deuda Global Total: $1.480.000
  // (32 jugadores morosos)".
  deudaGlobalTotalTexto = '—';
  jugadoresMorososTexto = '';

  paymentForm: FormGroup;

  periods = [
    'Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio',
    'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre',
  ];

  // Año actual + los 2 anteriores. Se recalcula solo con la fecha de hoy, así que la ventana
  // se corre sola con el correr de los años (ej. en 2027 pasa a ser 2027/2026/2025) sin tocar código.
  years = buildYearOptions();

  methods = ['Transferencia', 'Efectivo'];

  private jugadores: JugadorResumen[] = [];

  // Jugador realmente seleccionado desde las sugerencias. El control del form solo guarda el
  // texto visible; esto es lo único que se manda al backend, así dos jugadores con el mismo
  // nombre nunca se confunden (con 577 jugadores reales, un match por string no alcanza).
  private selectedPlayer: JugadorResumen | null = null;

  matchingPlayers: JugadorResumen[] = [];
  showSuggestions = false;

  // Buscador del panel "Últimos pagos": busca entre TODOS los jugadores (no solo los que
  // aparecen en la lista de abajo) y navega directo a su perfil. No filtra ni reemplaza esa
  // lista, que siempre sigue mostrando los últimos 10 pagos.
  panelSearchResults: JugadorResumen[] = [];
  showPanelSearchResults = false;

  enviando = false;

  cargandoJugadores = false;
  cargandoListas = false;

  pendingRows: PendingRow[] = [];
  paymentRows: PaymentRow[] = [];

  // Lo que se ve en el input de Monto ("1.000"). El control del form (paymentForm.get('amount'))
  // guarda el número limpio sin puntos ("1000"), que es lo que se valida y se manda al backend.
  montoDisplay = '';

  // Referencia fija arriba del formulario (no depende de qué jugador/período elijas): los dos
  // aranceles vigentes ahora mismo, uno por género. Se recalculan solos si cambia el arancel
  // vigente. A propósito el campo Monto NO trae ninguna sugerencia — mezclar jugador+período
  // traía confusión (un jugador podía tener otro monto real para un período pasado), así que la
  // precisión de "cuánto debe puntualmente" vive en Deuda Pendiente, no acá.
  arancelMasculinoTexto = '';
  arancelFemeninoTexto = '';
  inscripcionTexto = '';
  private currentEnrollmentFeeAmount: number | null = null;

  // ===== PANEL MODE TOGGLE =====
  panelMode: 'cuota' | 'inscripcion' = 'cuota';

  // ===== INSCRIPCIÓN TAB STATE =====
  inscripcionForm: FormGroup;
  inscripcionMontoDisplay = '';
  inscripcionMatchingPlayers: JugadorResumen[] = [];
  inscripcionShowSuggestions = false;
  private selectedInscripcionPlayer: JugadorResumen | null = null;
  enrollmentData: EnrollmentModel | null = null;
  enviandoInscripcion = false;
  readonly cargandoEnrollment = signal(false);
  readonly enrollmentSinRegistro = signal(false);
  private cancelEnrollment$ = new Subject<void>();

  constructor(
    private fb: FormBuilder,
    private pagosService: PagosService,
    private arancelesService: ArancelesService,
    private enrollmentFeeService: EnrollmentFeeService,
    private enrollmentService: EnrollmentService,
    private cdr: ChangeDetectorRef,
    private router: Router,
    private notifications: NotificationService
  ) {
    this.paymentForm = this.fb.group({
      player: ['', [Validators.required, this.knownPlayerValidator]],
      period: ['', [Validators.required]],
      year: [new Date().getFullYear(), [Validators.required]],
      amount: ['', [Validators.required, Validators.min(1)]],
      method: ['', [Validators.required]],
    });
    this.inscripcionForm = this.fb.group({
      amount: ['', [Validators.required, Validators.min(1)]],
      method: ['', [Validators.required]],
    });
  }

  irAJugador(idJugador: number) {
    this.router.navigate(['/admin/portal/jugadores', idJugador]);
  }

  // Desde "Pendientes de cobro" no se va al historial general, sino a la deuda puntual
  // (cuánto debe, cuánto ya abonó y cuánto le falta).
  irADeuda(idJugador: number) {
    this.router.navigate(['/admin/portal/jugadores', idJugador, 'deuda']);
  }

  // Reformatea el Monto con puntos de miles a medida que se escribe, preservando la posición
  // del cursor por cantidad de dígitos (no por índice de caracter, que cambia cada vez que se
  // agrega o saca un punto) — así corregir un número a mitad de la escritura no hace saltar
  // el cursor al final.
  onMontoInput(target: HTMLInputElement) {
    const cursorPos = target.selectionStart ?? target.value.length;
    const digitsBeforeCursor = target.value.slice(0, cursorPos).replace(/\D/g, '').length;

    const digitsOnly = target.value.replace(/\D/g, '').slice(0, 12);
    const formatted = digitsOnly ? Number(digitsOnly).toLocaleString('es-AR') : '';

    // Se escribe el DOM a mano, no solo vía el binding [value]: si el texto recalculado da
    // igual al montoDisplay anterior (ej. al borrar un punto de formato, que reaparece solo),
    // Angular no vuelve a tocar el input porque "no cambió" desde su óptica — pero el navegador
    // ya había mutado el value nativamente al borrar, y sin esto queda esa edición sin corregir.
    target.value = formatted;
    this.montoDisplay = formatted;
    this.paymentForm.patchValue({ amount: digitsOnly });

    queueMicrotask(() => {
      let newPos = digitsBeforeCursor === 0 ? 0 : formatted.length;
      let digitsSeen = 0;
      for (let i = 0; i < formatted.length && digitsBeforeCursor > 0; i++) {
        if (/\d/.test(formatted[i])) {
          digitsSeen++;
        }
        if (digitsSeen === digitsBeforeCursor) {
          newPos = i + 1;
          break;
        }
      }
      target.setSelectionRange(newPos, newPos);
    });
  }

  onPanelSearch(term: string) {
    const needle = normalizeTexto(term.trim().toLowerCase());
    if (!needle) {
      this.panelSearchResults = [];
      this.showPanelSearchResults = false;
      return;
    }

    const digits = needle.replace(/\D/g, '');
    this.panelSearchResults = this.jugadores
      .filter(
        (jugador) =>
          normalizeTexto(jugador.nombreCompleto.toLowerCase()).includes(needle) ||
          (digits.length > 0 && jugador.dni.replace(/\D/g, '').includes(digits))
      )
      .slice(0, 20);
    this.showPanelSearchResults = this.panelSearchResults.length > 0;
  }

  hidePanelSearchResults() {
    this.showPanelSearchResults = false;
  }

  ngOnDestroy() {
    this.cancelEnrollment$.next();
    this.cancelEnrollment$.complete();
  }

  ngOnInit() {
    this.cargarJugadores();
    this.cargarListas();
    this.cargarArancelesResumen();
  }

  switchPanelMode(mode: 'cuota' | 'inscripcion') {
    this.panelMode = mode;
  }

  onInscripcionPlayerSearch(term: string) {
    this.cancelEnrollment$.next(); // cancela cualquier carga en vuelo al cambiar búsqueda
    this.selectedInscripcionPlayer = null;
    this.enrollmentData = null;
    this.enrollmentSinRegistro.set(false);
    this.cargandoEnrollment.set(false);

    const needle = normalizeTexto(term.trim().toLowerCase());
    if (!needle) {
      this.inscripcionMatchingPlayers = [];
      this.inscripcionShowSuggestions = false;
      return;
    }

    const digits = needle.replace(/\D/g, '');
    this.inscripcionMatchingPlayers = this.jugadores
      .filter(
        (jugador) =>
          normalizeTexto(jugador.nombreCompleto.toLowerCase()).includes(needle) ||
          (digits.length > 0 && jugador.dni.replace(/\D/g, '').includes(digits))
      )
      .slice(0, 20);
    this.inscripcionShowSuggestions = this.inscripcionMatchingPlayers.length > 0;
  }

  selectInscripcionPlayer(jugador: JugadorResumen) {
    this.selectedInscripcionPlayer = jugador;
    this.inscripcionMatchingPlayers = [];
    this.inscripcionShowSuggestions = false;
    this.cargarEnrollment(jugador.idJugador);
  }

  hideInscripcionSuggestions() {
    this.inscripcionShowSuggestions = false;
  }

  onInscripcionMontoInput(target: HTMLInputElement) {
    const cursorPos = target.selectionStart ?? target.value.length;
    const digitsBeforeCursor = target.value.slice(0, cursorPos).replace(/\D/g, '').length;

    const digitsOnly = target.value.replace(/\D/g, '').slice(0, 12);
    const formatted = digitsOnly ? Number(digitsOnly).toLocaleString('es-AR') : '';

    target.value = formatted;
    this.inscripcionMontoDisplay = formatted;
    this.inscripcionForm.patchValue({ amount: digitsOnly });

    queueMicrotask(() => {
      let newPos = digitsBeforeCursor === 0 ? 0 : formatted.length;
      let digitsSeen = 0;
      for (let i = 0; i < formatted.length && digitsBeforeCursor > 0; i++) {
        if (/\d/.test(formatted[i])) {
          digitsSeen++;
        }
        if (digitsSeen === digitsBeforeCursor) {
          newPos = i + 1;
          break;
        }
      }
      target.setSelectionRange(newPos, newPos);
    });
  }

  private cargarEnrollment(playerId: number) {
    this.cancelEnrollment$.next(); // cancela cualquier llamada anterior en vuelo
    this.cargandoEnrollment.set(true);
    this.enrollmentData = null;
    this.enrollmentSinRegistro.set(false);
    this.enrollmentService.getEnrollmentByPlayer(playerId).pipe(takeUntil(this.cancelEnrollment$)).subscribe({
      next: (data) => {
        this.enrollmentData = data;
        if (data.pendingBalance > 0) {
          const saldoFormateado = data.pendingBalance.toLocaleString('es-AR');
          this.inscripcionForm.patchValue({ amount: String(Math.round(data.pendingBalance)) });
          this.inscripcionMontoDisplay = saldoFormateado;
        }
        this.cargandoEnrollment.set(false);
        this.cdr.detectChanges();
      },
      error: (err: HttpErrorResponse) => {
        this.cargandoEnrollment.set(false);
        if (err.status === 404) {
          this.enrollmentSinRegistro.set(true);
          if (this.currentEnrollmentFeeAmount != null) {
            const monto = Math.round(this.currentEnrollmentFeeAmount);
            this.inscripcionForm.patchValue({ amount: String(monto) });
            this.inscripcionMontoDisplay = monto.toLocaleString('es-AR');
          }
          this.notifications.notify(
            `${this.selectedInscripcionPlayer?.nombreCompleto ?? 'El jugador'} no tiene inscripción registrada. Se generará al registrar el primer pago.`,
            'cancelled'
          );
        }
      },
    });
  }

  onSubmitInscripcion() {
    if (this.inscripcionForm.invalid || !this.selectedInscripcionPlayer) {
      this.inscripcionForm.markAllAsTouched();
      return;
    }

    const jugador = this.selectedInscripcionPlayer;
    const { amount, method } = this.inscripcionForm.value;

    this.enviandoInscripcion = true;

    this.enrollmentService.registerEnrollmentPayment(jugador.idJugador, {
      amount: Number(amount),
      paymentMethod: method,
    }).subscribe({
      next: () => {
        this.enviandoInscripcion = false;
        this.inscripcionForm.reset({ amount: '', method: '' });
        this.inscripcionMontoDisplay = '';
        this.selectedInscripcionPlayer = null;
        this.enrollmentData = null;
        this.enrollmentSinRegistro.set(false);
        this.notifications.notify(`Pago de inscripción de ${jugador.nombreCompleto} registrado correctamente.`, 'success');
        this.cargarListas();
        this.cdr.detectChanges();
      },
      error: (err: HttpErrorResponse) => {
        this.enviandoInscripcion = false;
        const mensaje =
          (typeof err.error === 'string' ? err.error : null) ??
          err.error?.mensaje ??
          'No se pudo registrar el pago de inscripción. Intentá de nuevo.';
        this.notifications.notify(mensaje, 'error');
        this.cdr.detectChanges();
      },
    });
  }

  get inscripcionPlayerText(): string {
    return this.selectedInscripcionPlayer?.nombreCompleto ?? '';
  }

  get selectedInscripcionPlayerName(): string {
    return this.selectedInscripcionPlayer?.nombreCompleto ?? '';
  }

  initialsOfPublic(nombreCompleto: string): string {
    return initialsOf(nombreCompleto);
  }

  get inscripcionFormValid(): boolean {
    const tieneJugador = this.selectedInscripcionPlayer !== null;
    const tienesDatos = this.enrollmentData !== null || this.enrollmentSinRegistro();
    return this.inscripcionForm.valid && tieneJugador && tienesDatos;
  }

  formatCurrencyLocal(value: number): string {
    return CURRENCY_FULL.format(value);
  }

  formatDateLocal(isoDate: string): string {
    const [year, month, day] = isoDate.slice(0, 10).split('-');
    return `${day}/${month}/${year}`;
  }

  private cargarArancelesResumen() {
    this.arancelesService.getResumen().subscribe({
      next: (resumen) => {
        this.arancelMasculinoTexto = resumen.arancelMasculinoVigente != null
          ? `Arancel Masculino ${CURRENCY_ARANCEL.format(resumen.arancelMasculinoVigente)}`
          : '';
        this.arancelFemeninoTexto = resumen.arancelFemeninoVigente != null
          ? `Arancel Femenino ${CURRENCY_ARANCEL.format(resumen.arancelFemeninoVigente)}`
          : '';
        this.cdr.detectChanges();
      },
      error: () => {},
    });

    this.enrollmentFeeService.getAllEnrollmentFees().subscribe({
      next: (fees) => {
        const current = findCurrentEnrollmentFee(fees);
        this.currentEnrollmentFeeAmount = current?.amount ?? null;
        this.inscripcionTexto = current != null
          ? `Inscripción ${CURRENCY_ARANCEL.format(current.amount)}`
          : '';
        this.cdr.detectChanges();
      },
      error: () => {},
    });
  }

  private cargarJugadores() {
    this.cargandoJugadores = true;
    this.pagosService.getJugadores().subscribe({
      next: (jugadores) => {
        this.jugadores = jugadores;
        this.cargandoJugadores = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.cargandoJugadores = false;
        this.cdr.detectChanges();
      },
    });
  }

  private cargarListas() {
    this.cargandoListas = true;

    // detectChanges() explícito: HttpClient usa FetchBackend por default y sus respuestas no
    // siempre disparan la detección de cambios basada en zone.js, así que sin esto los campos
    // se actualizan en el componente pero la vista queda mostrando los valores viejos.
    this.pagosService.getPendientes().subscribe({
      next: (pendientes) => {
        this.pendingRows = pendientes.map(mapPendiente);
        this.cdr.detectChanges();
      },
      error: () => {},
    });

    this.pagosService.getRecientes(10).subscribe({
      next: (recientes) => {
        this.paymentRows = recientes.map(mapReciente);
        this.cdr.detectChanges();
      },
      error: () => {},
    });

    this.pagosService.getResumen().subscribe({
      next: (resumen) => {
        this.headerMetrics = mapResumen(resumen);
        // HU-019: formato completo (no compacto) para el monto, tal como pide el
        // ejemplo del criterio de aceptación ("$1.480.000", no "$1,48 M").
        this.deudaGlobalTotalTexto = CURRENCY_FULL.format(resumen.deudaGlobalTotal);
        this.jugadoresMorososTexto =
          `${resumen.jugadoresMorosos} ${resumen.jugadoresMorosos === 1 ? 'jugador moroso' : 'jugadores morosos'}`;
        this.cdr.detectChanges();
      },
      error: () => {},
      complete: () => (this.cargandoListas = false),
    });
  }

  // El control solo es válido si su texto coincide exactamente con el jugador elegido en
  // selectPlayer(): escribir un nombre "a mano" que calce con uno real no alcanza.
  private knownPlayerValidator = (control: AbstractControl): ValidationErrors | null => {
    const value = (control.value ?? '').toString().trim();
    if (!value) {
      return null;
    }
    return this.selectedPlayer && this.selectedPlayer.nombreCompleto === value ? null : { unknownPlayer: true };
  };

  get showInscripcionMethod(): boolean {
    return (this.enrollmentData !== null || this.enrollmentSinRegistro()) &&
           Number(this.inscripcionForm.get('amount')?.value) > 0;
  }

  get showPeriodYear(): boolean {
    return this.selectedPlayer !== null;
  }

  get showAmount(): boolean {
    return this.showPeriodYear && !!this.paymentForm.get('period')?.value;
  }

  get showMethod(): boolean {
    return this.showAmount && Number(this.paymentForm.get('amount')?.value) > 0;
  }

  onPlayerSearch(term: string) {
    if (this.selectedPlayer !== null) {
      this.paymentForm.patchValue({ period: '', amount: '', method: '' });
      this.montoDisplay = '';
    }
    this.selectedPlayer = null;

    const needle = normalizeTexto(term.trim().toLowerCase());
    if (!needle) {
      this.matchingPlayers = [];
      this.showSuggestions = false;
      return;
    }

    const digits = needle.replace(/\D/g, '');
    this.matchingPlayers = this.jugadores
      .filter(
        (jugador) =>
          normalizeTexto(jugador.nombreCompleto.toLowerCase()).includes(needle) ||
          (digits.length > 0 && jugador.dni.replace(/\D/g, '').includes(digits))
      )
      .slice(0, 20);
    this.showSuggestions = this.matchingPlayers.length > 0;
  }

  selectPlayer(jugador: JugadorResumen) {
    this.selectedPlayer = jugador;
    this.paymentForm.patchValue({ player: jugador.nombreCompleto });
    this.matchingPlayers = [];
    this.showSuggestions = false;
  }

  hideSuggestions() {
    this.showSuggestions = false;
  }

  onSubmit() {
    if (this.paymentForm.invalid || !this.selectedPlayer) {
      this.paymentForm.markAllAsTouched();
      return;
    }

    const { period, year, amount, method } = this.paymentForm.value;
    const jugador = this.selectedPlayer;

    this.enviando = true;

    this.pagosService.registrarPago(jugador.idJugador, period, Number(year), Number(amount), method).subscribe({
      next: () => {
        this.enviando = false;
        this.paymentForm.reset({ player: '', period: '', year: new Date().getFullYear(), amount: '', method: '' });
        this.montoDisplay = '';
        this.selectedPlayer = null;
        this.matchingPlayers = [];
        this.showSuggestions = false;
        this.notifications.notify(`Pago de ${jugador.nombreCompleto} registrado correctamente.`, 'success');
        this.cargarListas();
        this.cdr.detectChanges();
      },
      error: (err: HttpErrorResponse) => {
        this.enviando = false;
        const mensaje = err.error?.mensaje ?? 'No se pudo registrar el pago. Intentá de nuevo.';
        this.notifications.notify(mensaje, 'error');
        this.cdr.detectChanges();
      },
    });
  }
}

// Saca los acentos (á->a, ñ->n, etc.) para que buscar "guzman" encuentre "GUZMÁN" y
// buscar "nino" encuentre "NIÑO". Sin esto, cualquier tilde que el usuario no tipee hace
// fallar la búsqueda por completo.
// Regex para el rango Unicode de marcas diacríticas combinantes (U+0300-U+036F), construida
// por código de caracter en vez de escribir el literal para evitar que el editor la reinterprete
// como el caracter combinante real en vez del patrón de regex.
const DIACRITICS_PATTERN = String.fromCharCode(91, 92, 117, 48, 51, 48, 48, 45, 92, 117, 48, 51, 54, 102, 93);
const DIACRITICS_REGEX = new RegExp(DIACRITICS_PATTERN, 'g');

function normalizeTexto(texto: string): string {
  return texto.normalize('NFD').replace(DIACRITICS_REGEX, '');
}

// Año actual y los 2 anteriores, más nuevo primero. Calculado en el momento (no hardcodeado)
// para que la ventana se corra sola cada 1° de enero sin que haga falta tocar código.
function buildYearOptions(): number[] {
  const actual = new Date().getFullYear();
  return [actual, actual - 1, actual - 2];
}

function initialsOf(nombreCompleto: string): string {
  const [apellido, nombre] = nombreCompleto.split(',').map((p) => p.trim());
  return `${(apellido?.[0] ?? '')}${(nombre?.[0] ?? '')}`.toUpperCase();
}

function mapPendiente(p: PendienteJugador): PendingRow {
  return {
    id: p.idJugador,
    initials: initialsOf(p.nombreCompleto),
    name: p.nombreCompleto,
    category: p.categoria,
    amount: CURRENCY_FULL.format(p.montoTotal),
    installments: `${p.cantidadCuotas} ${p.cantidadCuotas === 1 ? 'cuota' : 'cuotas'}`,
  };
}

function mapReciente(p: PagoReciente): PaymentRow {
  return {
    id: p.idPago,
    idJugador: p.idJugador,
    initials: initialsOf(p.nombreCompleto),
    name: p.nombreCompleto,
    method: p.metodoPago,
    amount: CURRENCY_FULL.format(p.monto),
    elapsed: formatElapsed(p.fechaPago),
    concepto: p.concepto ?? 'Cuota',
  };
}

// PAGOS.fecha_pago es DATE (sin hora) en la base real: solo se puede mostrar granularidad de
// días, no "hace 5 min" como en el mock original.
function formatElapsed(fechaPagoIso: string): string {
  const fecha = new Date(fechaPagoIso);
  const hoy = new Date();
  const unDia = 24 * 60 * 60 * 1000;
  const diffDias = Math.round(
    (new Date(hoy.getFullYear(), hoy.getMonth(), hoy.getDate()).getTime() -
      new Date(fecha.getFullYear(), fecha.getMonth(), fecha.getDate()).getTime()) /
      unDia
  );

  if (diffDias === 0) return 'Hoy';
  if (diffDias === 1) return 'Ayer';
  if (diffDias > 1 && diffDias < 30) return `Hace ${diffDias} días`;
  return fecha.toLocaleDateString('es-AR', { day: '2-digit', month: '2-digit', year: 'numeric' });
}

function mapResumen(r: ResumenPagos): HeaderMetric[] {
  return [
    { value: formatCompactCurrency(r.recaudadoAnioActual), label: `Recaudado ${new Date().getFullYear()}` },
    { value: String(r.pagosDelMes), label: 'Pagos del mes' },
    { value: String(r.cantidadPendientes), label: 'Pendientes' },
  ];
}
