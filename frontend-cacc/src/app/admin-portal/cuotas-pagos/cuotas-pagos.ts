import { ChangeDetectorRef, Component, ElementRef, HostListener, OnDestroy, OnInit, ViewChild, signal } from '@angular/core';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, FormGroup, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { PagosService } from '../../services/pagos';
import { ArancelesService } from '../../services/aranceles';
import { EnrollmentFeeService } from '../../services/enrollment-fees';
import { EnrollmentFeeGender, findCurrentEnrollmentFee } from '../../models/EnrollmentFeeModel';
import { EnrollmentService } from '../../services/enrollment';
import { EnrollmentModel } from '../../models/EnrollmentModel';
import { formatCompactCurrency } from '../../shared/format-currency';
import { normalizeText } from '../../shared/normalize-text';
import { CustomSelect } from '../../shared/custom-select/custom-select';
import { Cargando } from '../../shared/cargando/cargando';
import { EstadoVacio } from '../../shared/estado-vacio/estado-vacio';
import { NotificationService } from '../../shared/notifications/notification.service';
import { financialLoadErrorMessage } from '../../shared/http-error-message';
import { DIA_DE_CORTE } from '../../shared/calendario-cuotas';
import { LatestRequest } from '../../shared/latest-request';
import { MEDIOS_DE_PAGO, guardarUltimoMedioPago, leerUltimoMedioPago } from '../../shared/ultimo-medio-pago';
import { PlayerSummaryModel } from '../../models/PlayerSummaryModel';
import { PaymentSummaryModel, PendingDebtorModel, PendingFeeDetailModel } from '../../models/PaymentModel';

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
  dni: string;
  category: string;
  amount: string;
  installments: string;
  // Crudo, para el filtro por cuotas (installments ya viene formateado como "2 cuotas").
  cuotasCount: number;
  // Dado de baja: se sigue listando porque debe, con la etiqueta "Baja".
  dadoDeBaja: boolean;
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

const MESES = [
  'Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio',
  'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre',
];

// HU-100: "Cuotas y Pagos" como mostrador. Un solo buscador arriba (atajo "/"); al elegir un alumno se ven, lado a
// lado, sus cuotas pendientes y el cobro, con el botón "Registrar cobro" siempre a la vista. El medio de pago arranca
// en el último usado y, después de un cobro, todo se limpia y el foco vuelve al buscador para atender al siguiente.
// Las reglas y los montos del cobro siguen siendo los del backend (mismos endpoints de siempre).
@Component({
  selector: 'app-cuotas-pagos',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, FormsModule, CustomSelect, Cargando, EstadoVacio],
  templateUrl: './cuotas-pagos.html',
  styleUrl: './cuotas-pagos.css',
})
export class CuotasPagos implements OnInit, OnDestroy {

  @ViewChild('buscador') private buscador?: ElementRef<HTMLInputElement>;
  @ViewChild('montoCuota') private montoCuota?: ElementRef<HTMLInputElement>;
  @ViewChild('montoInscripcion') private montoInscripcion?: ElementRef<HTMLInputElement>;

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

  periods = MESES;

  // Año actual + los 2 anteriores. Se recalcula solo con la fecha de hoy, así que la ventana
  // se corre sola con el correr de los años (ej. en 2027 pasa a ser 2027/2026/2025) sin tocar código.
  years = buildYearOptions();

  methods: string[] = [...MEDIOS_DE_PAGO];

  private jugadores: PlayerSummaryModel[] = [];

  // ===== BUSCADOR DEL MOSTRADOR =====
  // Lo que se ve en el buscador. El jugador elegido va aparte (selectedPlayer): es lo único que se manda al backend,
  // así dos jugadores con el mismo nombre nunca se confunden.
  busqueda = '';
  matchingPlayers: PlayerSummaryModel[] = [];
  showSuggestions = false;
  // Sugerencia resaltada con las flechas (-1 = ninguna).
  sugerenciaActiva = -1;
  selectedPlayer: PlayerSummaryModel | null = null;

  // Cuotas pendientes del alumno elegido (columna izquierda del mostrador).
  cuotasJugador: PendingFeeDetailModel[] = [];
  cargandoCuotas = false;
  errorCuotas = false;
  private readonly consultaCuotas = new LatestRequest();

  enviando = false;

  cargandoJugadores = false;
  cargandoListas = false;

  pendingRows: PendingRow[] = [];
  pendingLoaded = false;
  pendingFailed = false;

  // HU-022: error al leer pendientes o métricas (sin conexión, 403, 500). Se muestra
  // arriba de todo con "Reintentar"; null = sin error.
  errorCarga: string | null = null;

  // Filtros de "Pendientes de cobro": por nombre/DNI, categoría y cantidad de cuotas
  // impagas — todo sobre la lista ya traída (no piden de nuevo al servidor).
  busquedaPendientes = '';
  categoriaFiltro: string | null = null;
  cuotasFiltro: number | null = null;

  // Catálogo completo (HU-020): incluye categorías sin pendientes hoy, igual que en
  // Deudas y Morosidad.
  categoriaOptions: string[] = [];

  // Fijo, no armado desde los datos: "+3" agrupa a cualquiera con 3 cuotas o más (ver
  // filteredPendingRows), así la opción está disponible aunque hoy nadie la tenga —
  // puede pasar en cualquier momento.
  cuotasOptions: { value: number; label: string }[] = [
    { value: 1, label: '1 cuota' },
    { value: 2, label: '2 cuotas' },
    { value: 3, label: '+3 cuotas' },
  ];

  // Lo que se ve en el input de Monto ("1.000"). El control del form (paymentForm.get('amount'))
  // guarda el número limpio sin puntos ("1000"), que es lo que se valida y se manda al backend.
  montoDisplay = '';

  // Referencia fija arriba del formulario: los aranceles vigentes ahora mismo, uno por género.
  arancelMasculinoTexto = '';
  arancelFemeninoTexto = '';
  inscripcionTexto = '';
  // Solo si el club cobra inscripción a las mujeres (hoy no): con $0 o sin arancel no se muestra.
  inscripcionFemeninaTexto = '';
  // Arancel de inscripción vigente por género; sin entrada (o en 0) = ese género no paga inscripción.
  private currentEnrollmentFees: Partial<Record<EnrollmentFeeGender, number>> = {};

  // ===== CONCEPTO DEL COBRO: cuota o inscripción =====
  panelMode: 'cuota' | 'inscripcion' = 'cuota';

  // ===== INSCRIPCIÓN =====
  inscripcionForm: FormGroup;
  inscripcionMontoDisplay = '';
  enrollmentData: EnrollmentModel | null = null;
  enviandoInscripcion = false;
  readonly cargandoEnrollment = signal(false);
  readonly enrollmentSinRegistro = signal(false);
  private enrollmentDe: number | null = null;
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
    // HU-100: el medio de pago arranca en el último usado (se puede cambiar en el momento).
    const ultimoMedio = leerUltimoMedioPago();
    this.paymentForm = this.fb.group({
      period: ['', [Validators.required]],
      year: ['', [Validators.required]],
      amount: ['', [Validators.required, Validators.min(1)]],
      method: [ultimoMedio, [Validators.required]],
    });
    this.inscripcionForm = this.fb.group({
      amount: ['', [Validators.required, Validators.min(1)]],
      method: [ultimoMedio, [Validators.required]],
    });
  }

  // ===== ATAJO "/" =====

  // "/" enfoca el buscador desde cualquier lugar de la pantalla, salvo que se esté escribiendo en un campo (ahí la
  // barra se escribe normalmente).
  @HostListener('document:keydown', ['$event'])
  onAtajo(event: KeyboardEvent) {
    if (event.key !== '/' || event.ctrlKey || event.metaKey || event.altKey || esCampoEditable(event.target)) {
      return;
    }
    event.preventDefault();
    this.enfocarBuscador();
  }

  enfocarBuscador() {
    const input = this.buscador?.nativeElement;
    if (!input) return;
    input.focus();
    input.select();
  }

  // ===== BUSCADOR =====

  onPlayerSearch(term: string) {
    this.busqueda = term;
    // Cambiar el texto deja de lado al alumno elegido: su panel de cobro se cierra.
    if (this.selectedPlayer && term !== this.selectedPlayer.nombreCompleto) {
      this.limpiarSeleccion();
    }

    this.matchingPlayers = buscarJugadores(this.jugadores, term);
    this.showSuggestions = this.matchingPlayers.length > 0;
    this.sugerenciaActiva = this.showSuggestions ? 0 : -1;
  }

  // Flechas para moverse entre las sugerencias, Enter para elegir y Esc para cerrarlas.
  onBuscadorKeydown(event: KeyboardEvent) {
    switch (event.key) {
      case 'ArrowDown':
        if (!this.showSuggestions) return;
        event.preventDefault();
        this.sugerenciaActiva = Math.min(this.sugerenciaActiva + 1, this.matchingPlayers.length - 1);
        break;
      case 'ArrowUp':
        if (!this.showSuggestions) return;
        event.preventDefault();
        this.sugerenciaActiva = Math.max(this.sugerenciaActiva - 1, 0);
        break;
      case 'Enter': {
        const jugador = this.matchingPlayers[this.sugerenciaActiva];
        if (this.showSuggestions && jugador) {
          event.preventDefault();
          this.selectPlayer(jugador);
        }
        break;
      }
      case 'Escape':
        this.hideSuggestions();
        break;
    }
  }

  hideSuggestions() {
    this.showSuggestions = false;
    this.sugerenciaActiva = -1;
  }

  // Elegir un alumno abre el mostrador: sus cuotas a la izquierda y el cobro a la derecha. Se propone su cuota
  // pendiente más vieja (o el mes en curso si no debe nada, para un adelanto) y el foco va al monto.
  selectPlayer(jugador: PlayerSummaryModel) {
    this.selectedPlayer = jugador;
    this.busqueda = jugador.nombreCompleto;
    this.matchingPlayers = [];
    this.hideSuggestions();

    this.reiniciarFormularios();
    const hoy = new Date();
    this.paymentForm.patchValue({ period: MESES[hoy.getMonth()], year: hoy.getFullYear() });

    this.cargarCuotasJugador(jugador.idJugador);
    if (this.panelMode === 'inscripcion') {
      this.cargarEnrollment(jugador.idJugador);
    }
    this.cdr.detectChanges();
    this.enfocarMonto();
  }

  // Volver a buscar otro alumno sin cobrar.
  cambiarJugador() {
    this.limpiarSeleccion();
    this.busqueda = '';
    this.cdr.detectChanges();
    this.enfocarBuscador();
  }

  // Desde "Pendientes de cobro": el alumno se carga en el mostrador, sin salir de la pantalla.
  cobrarDesdePendientes(idJugador: number) {
    const jugador = this.jugadores.find((j) => j.idJugador === idJugador);
    if (!jugador) {
      this.irADeuda(idJugador);
      return;
    }
    this.selectPlayer(jugador);
    this.buscador?.nativeElement.scrollIntoView?.({ behavior: 'smooth', block: 'center' });
  }

  // La deuda en detalle y el historial del alumno (pantalla aparte).
  irADeuda(idJugador: number) {
    this.router.navigate(['/admin/portal/jugadores', idJugador, 'deuda']);
  }

  // ===== CUOTAS DEL ALUMNO =====

  private cargarCuotasJugador(idJugador: number) {
    this.cargandoCuotas = true;
    this.errorCuotas = false;
    this.cuotasJugador = [];
    this.consultaCuotas.run(this.pagosService.getDeuda(idJugador), {
      next: (cuotas) => {
        this.cuotasJugador = cuotas;
        this.cargandoCuotas = false;
        // Si todavía no tocó el monto, se propone la cuota más vieja que debe.
        const primera = cuotas.find((c) => parsePeriodo(c.periodo) !== null && c.saldoPendiente > 0);
        if (primera && !this.paymentForm.get('amount')?.value) {
          this.aplicarCuota(primera);
        }
        this.cdr.detectChanges();
      },
      error: () => {
        this.cargandoCuotas = false;
        this.errorCuotas = true;
        this.cdr.detectChanges();
      },
    });
  }

  get totalAdeudado(): number {
    return this.cuotasJugador.reduce((total, c) => total + c.saldoPendiente, 0);
  }

  // Tocar una cuota la carga en el cobro (período, año y su saldo). Para un pago parcial alcanza con cambiar el monto.
  elegirCuota(cuota: PendingFeeDetailModel) {
    if (parsePeriodo(cuota.periodo) === null) {
      this.switchPanelMode('inscripcion');
      return;
    }
    this.panelMode = 'cuota';
    this.aplicarCuota(cuota);
    this.cdr.detectChanges();
    this.enfocarMonto();
  }

  cuotaElegida(cuota: PendingFeeDetailModel): boolean {
    const p = parsePeriodo(cuota.periodo);
    const { period, year } = this.paymentForm.value;
    return this.panelMode === 'cuota' && p !== null && p.mes === period && p.anio === Number(year);
  }

  private aplicarCuota(cuota: PendingFeeDetailModel) {
    const p = parsePeriodo(cuota.periodo);
    if (!p) return;
    const monto = Math.round(cuota.saldoPendiente);
    this.paymentForm.patchValue({ period: p.mes, year: p.anio, amount: String(monto) });
    this.montoDisplay = monto.toLocaleString('es-AR');
  }

  // ===== CONCEPTO =====

  switchPanelMode(mode: 'cuota' | 'inscripcion') {
    this.panelMode = mode;
    if (mode === 'inscripcion' && this.selectedPlayer && this.enrollmentDe !== this.selectedPlayer.idJugador) {
      this.cargarEnrollment(this.selectedPlayer.idJugador);
    }
    this.cdr.detectChanges();
    this.enfocarMonto();
  }

  // ===== FILTROS DE "PENDIENTES DE COBRO" =====

  get filteredPendingRows(): PendingRow[] {
    const term = normalizeText(this.busquedaPendientes.trim());
    const digits = term.replace(/\D/g, '');

    return this.pendingRows.filter((row) => {
      if (this.categoriaFiltro && row.category !== this.categoriaFiltro) {
        return false;
      }
      if (this.cuotasFiltro) {
        // El "3" del combo es un balde abierto ("+3 cuotas"), no un valor exacto: agrupa
        // a cualquiera con 3 o más, sin importar si llega a 4, 7 o 10.
        const coincide =
          this.cuotasFiltro >= 3 ? row.cuotasCount >= 3 : row.cuotasCount === this.cuotasFiltro;
        if (!coincide) {
          return false;
        }
      }
      if (
        term &&
        !normalizeText(row.name).includes(term) &&
        !(digits.length > 0 && row.dni.replace(/\D/g, '').includes(digits))
      ) {
        return false;
      }
      return true;
    });
  }

  get hayFiltrosPendientesActivos(): boolean {
    return (
      this.busquedaPendientes.trim().length > 0 ||
      !!this.categoriaFiltro ||
      !!this.cuotasFiltro
    );
  }

  limpiarFiltrosPendientes() {
    this.busquedaPendientes = '';
    this.categoriaFiltro = null;
    this.cuotasFiltro = null;
  }

  // ===== MONTOS =====

  // Reformatea el Monto con puntos de miles a medida que se escribe, preservando la posición
  // del cursor por cantidad de dígitos (no por índice de caracter, que cambia cada vez que se
  // agrega o saca un punto) — así corregir un número a mitad de la escritura no hace saltar
  // el cursor al final.
  onMontoInput(target: HTMLInputElement) {
    this.montoDisplay = formatearMonto(target, (digits) => this.paymentForm.patchValue({ amount: digits }));
  }

  onInscripcionMontoInput(target: HTMLInputElement) {
    this.inscripcionMontoDisplay = formatearMonto(target, (digits) => this.inscripcionForm.patchValue({ amount: digits }));
  }

  ngOnDestroy() {
    this.cancelEnrollment$.next();
    this.cancelEnrollment$.complete();
    this.consultaCuotas.cancel();
  }

  ngOnInit() {
    this.cargarJugadores();
    this.cargarListas();
    this.cargarArancelesResumen();
    this.cargarCategorias();
  }

  private cargarCategorias() {
    this.pagosService.getCategorias().subscribe({
      next: (categorias) => {
        this.categoriaOptions = categorias.map((c) => c.nombre);
        this.cdr.detectChanges();
      },
      error: () => {},
    });
  }

  private cargarEnrollment(playerId: number) {
    this.cancelEnrollment$.next(); // cancela cualquier llamada anterior en vuelo
    this.enrollmentDe = playerId;
    this.cargandoEnrollment.set(true);
    this.enrollmentData = null;
    this.enrollmentSinRegistro.set(false);
    this.inscripcionForm.patchValue({ amount: '' });
    this.inscripcionMontoDisplay = '';
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
          const nombre = this.selectedPlayer?.nombreCompleto ?? 'El jugador';
          const genero: EnrollmentFeeGender = this.selectedPlayer?.genero === 'Femenino' ? 'Femenino' : 'Masculino';
          const vigente = this.currentEnrollmentFees[genero];
          if (vigente != null && vigente > 0) {
            const monto = Math.round(vigente);
            this.inscripcionForm.patchValue({ amount: String(monto) });
            this.inscripcionMontoDisplay = monto.toLocaleString('es-AR');
            this.notifications.notify(`${nombre} no tiene inscripción registrada. Se generará al registrar el primer pago.`, 'cancelled');
          } else {
            this.notifications.notify(mensajeSinInscripcion(nombre, genero, vigente), 'cancelled');
          }
        }
        this.cdr.detectChanges();
      },
    });
  }

  onSubmitInscripcion() {
    if (!this.inscripcionFormValid || !this.selectedPlayer) {
      this.inscripcionForm.markAllAsTouched();
      return;
    }

    const jugador = this.selectedPlayer;
    const { amount, method } = this.inscripcionForm.value;

    this.enviandoInscripcion = true;

    this.enrollmentService.registerEnrollmentPayment(jugador.idJugador, {
      amount: Number(amount),
      paymentMethod: method,
    }).subscribe({
      next: () => {
        this.enviandoInscripcion = false;
        guardarUltimoMedioPago(method);
        this.notifications.notify(`Pago de inscripción de ${jugador.nombreCompleto} registrado correctamente.`, 'success');
        this.finalizarCobro();
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

  get inscripcionFormValid(): boolean {
    const tieneJugador = this.selectedPlayer !== null;
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
        const masculina = findCurrentEnrollmentFee(fees, 'Masculino');
        const femenina = findCurrentEnrollmentFee(fees, 'Femenino');
        this.currentEnrollmentFees = {
          ...(masculina != null ? { Masculino: masculina.amount } : {}),
          ...(femenina != null ? { Femenino: femenina.amount } : {}),
        };
        this.inscripcionTexto = masculina != null && masculina.amount > 0
          ? `Inscripción masculina ${CURRENCY_ARANCEL.format(masculina.amount)}`
          : '';
        this.inscripcionFemeninaTexto = femenina != null && femenina.amount > 0
          ? `Inscripción femenina ${CURRENCY_ARANCEL.format(femenina.amount)}`
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

  reintentarCarga() {
    this.cargarListas();
  }

  private cargarListas() {
    this.cargandoListas = true;
    this.errorCarga = null;
    this.pendingLoaded = false;
    this.pendingFailed = false;

    // detectChanges() explícito: HttpClient usa FetchBackend por default y sus respuestas no
    // siempre disparan la detección de cambios basada en zone.js, así que sin esto los campos
    // se actualizan en el componente pero la vista queda mostrando los valores viejos.
    this.pagosService.getPendientes().subscribe({
      next: (pendientes) => {
        this.pendingRows = pendientes.map(mapPendiente);
        this.pendingLoaded = true;
        this.cdr.detectChanges();
      },
      // Antes el error caía en "No hay cobros pendientes", que es falso: no se sabe.
      error: (err: unknown) => {
        this.pendingRows = [];
        this.pendingLoaded = true;
        this.pendingFailed = true;
        this.errorCarga ??= financialLoadErrorMessage(err);
        this.cdr.detectChanges();
      },
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
      error: (err: unknown) => {
        this.cargandoListas = false;
        this.errorCarga ??= financialLoadErrorMessage(err);
        this.cdr.detectChanges();
      },
      complete: () => (this.cargandoListas = false),
    });
  }

  // Antes del día 10 la cuota del mes en curso todavía no existe: pagarla es un adelanto y queda imputado a
  // ese mes (la API la emite en el momento con el arancel vigente; el 10 ya no se vuelve a generar).
  get avisoAdelanto(): string | null {
    const hoy = new Date();
    const { period, year } = this.paymentForm.value;
    if (!this.selectedPlayer || hoy.getDate() >= DIA_DE_CORTE) {
      return null;
    }
    if (period !== this.periods[hoy.getMonth()] || Number(year) !== hoy.getFullYear()) {
      return null;
    }
    return `La cuota de ${period.toLowerCase()} se genera el ${DIA_DE_CORTE}: este pago queda como adelanto de ${period.toLowerCase()} y ese día ya no se le vuelve a generar.`;
  }

  // El botón "Registrar cobro" está siempre a la vista; se habilita cuando el cobro está completo.
  get puedeRegistrarCuota(): boolean {
    return this.selectedPlayer !== null && this.paymentForm.valid && !this.enviando;
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
        guardarUltimoMedioPago(method);
        this.notifications.notify(`Pago de ${jugador.nombreCompleto} registrado correctamente.`, 'success');
        this.finalizarCobro();
      },
      error: (err: HttpErrorResponse) => {
        this.enviando = false;
        const mensaje = err.error?.mensaje ?? 'No se pudo registrar el pago. Intentá de nuevo.';
        this.notifications.notify(mensaje, 'error');
        this.cdr.detectChanges();
      },
    });
  }

  // Después de un cobro (completo, parcial, adelanto o inscripción): se limpia el mostrador, se vuelven a leer los
  // pendientes y el foco vuelve al buscador para atender al siguiente alumno.
  private finalizarCobro() {
    this.limpiarSeleccion();
    this.busqueda = '';
    this.cargarListas();
    this.cdr.detectChanges();
    this.enfocarBuscador();
  }

  private limpiarSeleccion() {
    this.selectedPlayer = null;
    this.consultaCuotas.cancel();
    this.cuotasJugador = [];
    this.cargandoCuotas = false;
    this.errorCuotas = false;
    this.reiniciarFormularios();
  }

  // Deja los dos formularios vacíos, con el medio de pago en el último usado.
  private reiniciarFormularios() {
    const ultimoMedio = leerUltimoMedioPago();
    this.paymentForm.reset({ period: '', year: '', amount: '', method: ultimoMedio });
    this.montoDisplay = '';
    this.cancelEnrollment$.next();
    this.enrollmentDe = null;
    this.inscripcionForm.reset({ amount: '', method: ultimoMedio });
    this.inscripcionMontoDisplay = '';
    this.enrollmentData = null;
    this.enrollmentSinRegistro.set(false);
    this.cargandoEnrollment.set(false);
  }

  private enfocarMonto() {
    const input = this.panelMode === 'cuota' ? this.montoCuota : this.montoInscripcion;
    input?.nativeElement.focus();
  }
}

// "Juan Bazán", "bazan juan" o "47970803": cada palabra tiene que aparecer en el nombre (sin importar tildes ni orden),
// o los dígitos en el DNI. Hasta 20 sugerencias.
export function buscarJugadores(jugadores: PlayerSummaryModel[], term: string): PlayerSummaryModel[] {
  const needle = normalizeTexto(term.trim().toLowerCase());
  if (!needle) return [];
  const palabras = needle.split(/[\s,]+/).filter(Boolean);
  const digits = needle.replace(/\D/g, '');
  return jugadores
    .filter((jugador) => {
      const nombre = normalizeTexto(jugador.nombreCompleto.toLowerCase());
      return palabras.every((p) => nombre.includes(p)) ||
        (digits.length > 0 && digits.length === needle.replace(/[\s.]/g, '').length && jugador.dni.replace(/\D/g, '').includes(digits));
    })
    .slice(0, 20);
}

// "Marzo 2026" -> { mes: 'Marzo', anio: 2026 }; cualquier otra cosa (por ejemplo una inscripción) -> null.
export function parsePeriodo(periodo: string): { mes: string; anio: number } | null {
  const [mes, anio] = periodo.trim().split(/\s+/);
  return MESES.includes(mes) && /^\d{4}$/.test(anio ?? '') ? { mes, anio: Number(anio) } : null;
}

// Un campo editable: ahí "/" se escribe, no enfoca el buscador.
function esCampoEditable(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false;
  const tag = target.tagName;
  return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || target.isContentEditable;
}

// Puntos de miles mientras se escribe, sin que el cursor salte al final. Devuelve lo que muestra el input.
function formatearMonto(target: HTMLInputElement, guardar: (digits: string) => void): string {
  const cursorPos = target.selectionStart ?? target.value.length;
  const digitsBeforeCursor = target.value.slice(0, cursorPos).replace(/\D/g, '').length;

  const digitsOnly = target.value.replace(/\D/g, '').slice(0, 12);
  const formatted = digitsOnly ? Number(digitsOnly).toLocaleString('es-AR') : '';

  // Se escribe el DOM a mano, no solo vía el binding [value]: si el texto recalculado da
  // igual al anterior (ej. al borrar un punto de formato, que reaparece solo), Angular no vuelve
  // a tocar el input porque "no cambió" desde su óptica.
  target.value = formatted;
  guardar(digitsOnly);

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
  return formatted;
}

// Saca los acentos (á->a, ñ->n, etc.) para que buscar "guzman" encuentre "GUZMÁN" y
// buscar "nino" encuentre "NIÑO". Sin esto, cualquier tilde que el usuario no tipee hace
// fallar la búsqueda por completo.
// Regex para el rango Unicode de marcas diacríticas combinantes (U+0300-U+036F), construida
// por código de caracter en vez de escribir el literal para evitar que el editor la reinterprete
// como el caracter combinante real en vez del patrón de regex.
const DIACRITICS_PATTERN = String.fromCharCode(91, 92, 117, 48, 51, 48, 48, 45, 92, 117, 48, 51, 54, 102, 93);
const DIACRITICS_REGEX = new RegExp(DIACRITICS_PATTERN, 'g');

// Un arancel de inscripción en $0 existe: ese género simplemente no la paga. "Sin arancel cargado" es otro caso.
export function mensajeSinInscripcion(nombre: string, genero: EnrollmentFeeGender, vigente: number | undefined): string {
  if (vigente === 0) {
    return `${nombre} no paga inscripción: la inscripción ${genero === 'Femenino' ? 'femenina' : 'masculina'} está en $0 (no se cobra).`;
  }
  return `${nombre} no paga inscripción: no hay un arancel de inscripción vigente para su género.`;
}

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

function mapPendiente(p: PendingDebtorModel): PendingRow {
  return {
    id: p.idJugador,
    initials: initialsOf(p.nombreCompleto),
    name: p.nombreCompleto,
    dni: p.dni,
    category: p.categoria,
    amount: CURRENCY_FULL.format(p.montoTotal),
    installments: `${p.cantidadCuotas} ${p.cantidadCuotas === 1 ? 'cuota' : 'cuotas'}`,
    cuotasCount: p.cantidadCuotas,
    dadoDeBaja: !!p.fechaBaja,
  };
}

function mapResumen(r: PaymentSummaryModel): HeaderMetric[] {
  return [
    { value: formatCompactCurrency(r.recaudadoAnioActual), label: `Recaudado ${new Date().getFullYear()}` },
    { value: String(r.pagosDelMes), label: 'Pagos del mes' },
    { value: String(r.cantidadPendientes), label: 'Pendientes' },
  ];
}
