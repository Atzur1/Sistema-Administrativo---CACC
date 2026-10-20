import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  AbstractControl,
  FormBuilder,
  FormGroup,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { catchError, forkJoin, of } from 'rxjs';
import { DiscountBadge } from '../../shared/discount-badge/discount-badge';
import { Ventana } from '../../shared/ventana/ventana';
import { CustomSelect } from '../../shared/custom-select/custom-select';
import { HelpHint } from '../../shared/help-hint/help-hint';
import { NotificationService } from '../../shared/notifications/notification.service';
import { DiscountService } from '../../services/discounts';
import {
  BenefitValueType,
  DiscountModel,
  DiscountRequest,
  DiscountStatus,
  FirstPeriodModel,
  PeriodOption,
  addMonthsToPeriod,
  formatBenefitValue,
  formatIsoDate,
  formatValidity,
  lastDayOfMonth,
  periodLabel,
  statusLabel,
  statusToneClass,
} from '../../models/DiscountModel';
import { PagosService } from '../../services/pagos';
import { AuthService } from '../../services/auth';
import { normalizeText } from '../../shared/normalize-text';
import { PlayerSummaryModel } from '../../models/PlayerSummaryModel';

// Player shape this screen was originally built against (repo de Atzur1).
// Se mantiene local en vez de traer PlayerModel/PlayerService: Laura ya tiene
// su propio JugadoresService/PlayerSummaryModel, así que solo se mapea una vez acá
// y el resto del archivo (y el HTML) sigue usando los mismos nombres de
// siempre (fullName, document, category, id) sin tocar nada más.
interface PlayerModel {
  id: number;
  fullName: string;
  document: string;
  category: string;
  // "yyyy-MM-dd" si está dado de baja: no se le asignan becas ni descuentos.
  leaveDate: string | null;
}

function toPlayerModel(jugador: PlayerSummaryModel): PlayerModel {
  return {
    id: jugador.idJugador,
    fullName: jugador.nombreCompleto,
    document: jugador.dni,
    category: jugador.categoria,
    leaveDate: jugador.fechaBaja ?? null,
  };
}

// One counter on the page header
interface HeaderMetric {
  value: string;
  label: string;
}

// What the popup is showing at any moment. Keeping it in one field instead of a
// handful of booleans means the dialog can never be in two states at once.
type DialogView = 'loading' | 'form' | 'active' | 'confirmCancel' | 'confirmVoid';

@Component({
  selector: 'app-becados-descuentos',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, DiscountBadge, CustomSelect, HelpHint, Ventana],
  templateUrl: './becados-descuentos.html',
  styleUrl: './becados-descuentos.css',
})
export class BecadosDescuentos implements OnInit {
  // Header counters, derived from what the API returns. They start as dashes
  // so the screen never shows a made-up number while the request is in flight.
  headerMetrics = signal<HeaderMetric[]>([
    { value: '—', label: 'Beneficios asignados' },
    { value: '—', label: 'Vigentes' },
    { value: '—', label: 'No vigentes' },
  ]);

  // Player lookup on the page, the entry point to the popup
  lookupForm: FormGroup;

  // Assignment form, lives inside the popup
  benefitForm: FormGroup;

  // Filled from the catalogue endpoint, never hardcoded: the API validates
  // against the same table, so an option offered here is always accepted.
  benefitReasons = signal<string[]>([]);

  valueTypes: { value: BenefitValueType; label: string }[] = [
    { value: '%', label: 'Porcentaje' },
    { value: '$', label: 'Monto fijo' },
  ];

  // Squad the lookup searches, loaded from the API on entry
  private players = signal<PlayerModel[]>([]);

  // Suggestions shown under the search field
  matchingPlayers: PlayerModel[] = [];
  showSuggestions = false;
  selectedPlayer: PlayerModel | null = null;

  // ===== POPUP STATE =====
  dialogOpen = signal(false);
  dialogView = signal<DialogView>('loading');
  dialogPlayer = signal<PlayerModel | null>(null);
  currentDiscount = signal<DiscountModel | null>(null);
  dialogError = signal('');
  saving = signal(false);

  // Every benefit of the player the popup is about: what expired, what runs
  // today and what is scheduled. Since HU-012 a player can hold several.
  playerDiscounts = signal<DiscountModel[]>([]);

  // Desde qué cuota se le puede dar una bonificación a este jugador hoy (la dice el servidor, porque depende
  // de si la cuota del mes en curso ya tiene pagos). Con esto se arman los selectores de "desde" y "hasta".
  firstPeriod = signal<FirstPeriodModel | null>(null);
  startOptions = signal<PeriodOption[]>([]);
  endOptions = signal<PeriodOption[]>([]);

  // Cuántos meses ofrecen los selectores: el inicio, hasta dos años hacia adelante; el fin, tres años desde el inicio.
  private static readonly START_MONTHS = 24;
  private static readonly END_MONTHS = 36;

  // Every benefit ever granted: the table mixes current and cancelled ones.
  // benefitsLoaded tells "still loading" apart from "there is nothing to show".
  benefitRows = signal<DiscountModel[]>([]);
  benefitsLoaded = signal(false);

  // ===== FILTROS DE LA TABLA =====
  // Con ~700 jugadores en el club, "todos los beneficios de siempre" sin filtrar es
  // inmanejable. Reactive Forms en vez de ngModel simple: es el mismo patrón que ya usa
  // el resto de este componente (lookupForm/benefitForm), no hace falta importar
  // FormsModule aparte solo para esto. Se arma en el constructor (como las otras dos),
  // no acá arriba: this.fb todavía no está asignado en este punto de la clase.
  filterForm: FormGroup;

  estadoOptions: { value: DiscountStatus; label: string }[] = [
    { value: 'Active', label: 'Activa' },
    { value: 'Scheduled', label: 'Programada' },
    // "Finalizados": llegó a su fecha de cierre sin que nadie la cortara antes. Distinto
    // de Cancelada (interrumpida antes de tiempo) y de Anulada (nunca debió otorgarse) —
    // ese filtro es el suyo propio, este es solo para las que corrieron su curso completo.
    { value: 'Expired', label: 'Finalizados' },
    { value: 'Cancelled', label: 'Cancelada' },
    { value: 'Voided', label: 'Anulada' },
  ];

  motivoOptions = ['Becado', 'Descuento'];

  page = 1;
  readonly pageSize = 10;

  // Active discounts indexed by player, resolved by the backend.
  // Held in a signal so the table repaints when the response arrives.
  private discounts = signal(new Map<number, DiscountModel>());

  constructor(
    private fb: FormBuilder,
    private discountService: DiscountService,
    private pagosService: PagosService,
    private notifications: NotificationService,
    private authService: AuthService,
  ) {
    this.lookupForm = this.fb.group({
      player: ['', [Validators.required, this.knownPlayerValidator]],
    });

    this.benefitForm = this.fb.group(
      {
        reason: ['', [Validators.required]],
        valueType: ['', [Validators.required]],
        percentage: [null as number | null],
        fixedAmount: [null as number | null],
        // Mandatory since HU-012: no benefit without an authorised period. Se elige por cuota: startDate es el
        // primer día del mes de la primera cuota y endDate el último día del mes de la última.
        startDate: ['', [Validators.required]],
        endDate: ['', [Validators.required]],
      },
      { validators: [this.benefitValueValidator, this.dateRangeValidator] },
    );

    this.filterForm = this.fb.group({
      busqueda: [''],
      estado: [null as DiscountStatus | null],
      motivo: [null as string | null],
    });

    // Cualquier cambio de filtro vuelve a la página 1: si estabas en la página 3 de
    // "Cancelada" y filtrás por "Activa", esa página 3 puede ni existir en el nuevo
    // resultado.
    this.filterForm.valueChanges.subscribe(() => (this.page = 1));

    // Antes esto colgaba de (change) en el <select> nativo del template; se mueve acá
    // porque CustomSelect (ControlValueAccessor) no emite un evento (change) del DOM —
    // valueChanges es la forma correcta en Reactive Forms de reaccionar a esto sin
    // importar qué control de UI esté detrás.
    this.benefitForm.get('valueType')!.valueChanges.subscribe(() => this.onValueTypeChange());

    // Las cuotas finales posibles dependen de la inicial: al mover el inicio se rearman y, si la final elegida
    // quedó antes, se corrige sola a la misma cuota.
    this.benefitForm.get('startDate')!.valueChanges.subscribe(() => this.rebuildEndOptions());
  }

  ngOnInit() {
    this.loadDiscountMap();
    this.loadBenefitRows();

    this.pagosService.getJugadores().subscribe({
      next: (jugadores) => this.players.set(jugadores.map(toPlayerModel)),
      error: () => this.players.set([]),
    });

    this.discountService.getDiscountTypes().subscribe({
      next: (reasons) => this.benefitReasons.set(reasons),
      // Without the catalogue the form cannot offer a valid reason, so the
      // popup says so instead of showing an empty dropdown.
      error: () => this.benefitReasons.set([]),
    });
  }

  // ===== DATA LOADING =====

  private loadDiscountMap() {
    this.discountService.getDiscountMap().subscribe({
      next: (discountMap) => this.discounts.set(discountMap),
      // If the API fails the table still renders, just without badges
      error: () => this.discounts.set(new Map<number, DiscountModel>()),
    });
  }

  private loadBenefitRows() {
    this.discountService.getAllDiscounts().subscribe({
      next: (benefits) => {
        this.benefitRows.set(benefits);
        this.benefitsLoaded.set(true);
        this.headerMetrics.set(this.buildHeaderMetrics(benefits));
      },
      error: () => {
        this.benefitRows.set([]);
        this.benefitsLoaded.set(true);
      },
    });
  }

  // Re-reads everything the screen shows from the API. Called after every
  // successful write so the view renders persisted data and never a local
  // guess of what was saved.
  private refreshFromServer() {
    this.loadDiscountMap();
    this.loadBenefitRows();
  }

  private buildHeaderMetrics(benefits: DiscountModel[]): HeaderMetric[] {
    const active = benefits.filter((benefit) => benefit.isActive).length;
    return [
      { value: benefits.length.toString(), label: 'Beneficios asignados' },
      { value: active.toString(), label: 'Vigentes' },
      { value: (benefits.length - active).toString(), label: 'No vigentes' },
    ];
  }

  // ===== FILTROS Y PAGINACIÓN DE LA TABLA =====
  // Nada de esto llama a la API de nuevo: getAllDiscounts() ya trajo todo una vez
  // (mismo criterio que el resto del sistema, ej. Deudas y Morosidad), y de acá para
  // abajo es filtrar/paginar en el cliente sobre esa misma lista en memoria.

  get filteredRows(): DiscountModel[] {
    const { busqueda, estado, motivo } = this.filterForm.value;
    const term = normalizeText((busqueda ?? '').trim().toLowerCase());

    return this.benefitRows().filter((row) => {
      if (estado && row.status !== estado) {
        return false;
      }
      if (motivo && row.type !== motivo) {
        return false;
      }
      if (term && !normalizeText(row.playerName.toLowerCase()).includes(term)) {
        return false;
      }
      return true;
    });
  }

  get totalPaginas(): number {
    return Math.max(1, Math.ceil(this.filteredRows.length / this.pageSize));
  }

  get pagedRows(): DiscountModel[] {
    const start = (this.page - 1) * this.pageSize;
    return this.filteredRows.slice(start, start + this.pageSize);
  }

  get hayFiltrosActivos(): boolean {
    const { busqueda, estado, motivo } = this.filterForm.value;
    return !!(busqueda ?? '').trim() || !!estado || !!motivo;
  }

  limpiarFiltros() {
    this.filterForm.reset({ busqueda: '', estado: null, motivo: null });
  }

  irAPagina(nuevaPagina: number) {
    if (nuevaPagina < 1 || nuevaPagina > this.totalPaginas || nuevaPagina === this.page) {
      return;
    }
    this.page = nuevaPagina;
  }

  // ===== PLAYER LOOKUP =====

  initialsOf(fullName: string): string {
    const [lastName = '', firstName = ''] = fullName.split(',').map((part) => part.trim());
    return ((lastName.charAt(0) || '') + (firstName.charAt(0) || '')).toUpperCase();
  }

  // Null cuando el jugador no tiene beneficio vigente: el badge no se dibuja
  getDiscount(playerId: number): DiscountModel | null {
    return this.discounts().get(playerId) ?? null;
  }

  // The typed text must resolve to a real player, not just be non-empty
  private knownPlayerValidator = (control: AbstractControl): ValidationErrors | null => {
    const value = (control.value ?? '').toString().trim();
    if (!value) {
      return null;
    }
    return this.findPlayer(value) ? null : { unknownPlayer: true };
  };

  private findPlayer(value: string): PlayerModel | undefined {
    const needle = normalizeText(value);
    return this.players().find((player) => normalizeText(player.fullName) === needle);
  }

  // Minimum length before suggesting: below this, a single letter like "s"
  // matches half the squad and the dropdown is more noise than help.
  private static readonly MIN_SEARCH_LENGTH = 3;

  // One unified field: the same term is matched against name and document
  onPlayerSearch(term: string) {
    this.selectedPlayer = this.findPlayer(term.trim()) ?? null;

    const needle = normalizeText(term.trim());
    if (needle.length < BecadosDescuentos.MIN_SEARCH_LENGTH) {
      this.matchingPlayers = [];
      this.showSuggestions = false;
      return;
    }

    // Digits are compared without dots so "48221" also finds "48.221.107"
    const digits = needle.replace(/\D/g, '');
    this.matchingPlayers = this.players().filter(
      (player) =>
        normalizeText(player.fullName).includes(needle) ||
        (digits.length > 0 && player.document.replace(/\D/g, '').includes(digits)),
    );
    this.showSuggestions = this.matchingPlayers.length > 0;
  }

  selectPlayer(player: PlayerModel) {
    this.lookupForm.patchValue({ player: player.fullName });
    this.selectedPlayer = player;
    this.matchingPlayers = [];
    this.showSuggestions = false;
  }

  hideSuggestions() {
    this.showSuggestions = false;
  }

  // ===== POPUP =====

  // Entry point from the lookup panel
  openDialogForSelectedPlayer() {
    if (this.selectedPlayer === null) {
      return;
    }
    this.openDialog(this.selectedPlayer);
  }

  // Entry point from a row of the table. Pasa el id de esa fila puntual: si el jugador tiene
  // varios beneficios (uno activo y otros cancelados/anulados), "Gestionar" tiene que abrir
  // el que se clickeó, no cualquiera que esté vigente.
  openDialogForRow(row: DiscountModel) {
    const player = this.players().find((candidate) => candidate.id === row.playerId);

    // The table row already carries the name, so the popup can open even if
    // the squad list has not arrived yet
    this.openDialog(
      player ?? {
        id: row.playerId,
        fullName: row.playerName,
        document: '',
        category: row.category,
        leaveDate: null, // si estuviera de baja, el servidor rechaza cualquier cambio
      },
      row.id,
    );
  }

  private openDialog(player: PlayerModel, targetDiscountId?: number) {
    this.dialogPlayer.set(player);
    this.dialogError.set('');
    this.currentDiscount.set(null);
    this.playerDiscounts.set([]);
    this.dialogView.set('loading');
    this.dialogOpen.set(true);

    // The popup always asks the API what the player holds right now: the grid
    // may be stale and a benefit assigned from another screen has to show up.
    // Since HU-012 that is a list, not one record, and each item carries the
    // state the server resolved.
    // La primera cuota que se puede becar se pide junto con las bonificaciones. Si esa consulta falla no se
    // bloquea nada: se usa un cálculo conservador local (la próxima cuota que se emite) y el servidor valida igual.
    forkJoin({
      discounts: this.discountService.getDiscountsByPlayer(player.id),
      first: this.discountService.getFirstPeriod(player.id).pipe(catchError(() => of(null))),
    }).subscribe({
      next: ({ discounts, first }) => {
        this.firstPeriod.set(first ?? this.localFirstPeriod());
        this.playerDiscounts.set(discounts);

        // Si se abrió desde una fila puntual de la tabla, esa es la que se muestra, sea cual
        // sea su estado. Si no (se abrió desde el buscador de arriba), se cae al criterio de
        // siempre: la que aplica hoy o, si no hay, la próxima programada.
        const target = targetDiscountId != null ? discounts.find((b) => b.id === targetDiscountId) : undefined;
        const current =
          target ??
          discounts.find((benefit) => benefit.status === 'Active') ??
          discounts.find((benefit) => benefit.status === 'Scheduled') ??
          null;

        this.currentDiscount.set(current);

        if (current === null) {
          // Nothing in force or coming: straight to the assignment form
          this.resetBenefitForm();
          this.dialogView.set('form');
          return;
        }

        this.dialogView.set('active');
      },
      error: () => {
        this.dialogError.set('No se pudo cargar la bonificación del jugador. Intentá de nuevo.');
        this.firstPeriod.set(this.localFirstPeriod());
        this.resetBenefitForm();
        this.dialogView.set('form');
      },
    });
  }

  // Cálculo de respaldo si el servidor no contestó: desde el día 10 la cuota del mes ya se emitió, así que la
  // primera que se puede becar es la del mes que viene; antes del 10, la del mes en curso. No ofrece la cuota
  // ya emitida (eso depende de los pagos y solo lo sabe el servidor).
  private localFirstPeriod(): FirstPeriodModel {
    const now = new Date();
    const thisMonth = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-01`;
    const period = now.getDate() >= 10 ? addMonthsToPeriod(thisMonth, 1) : thisMonth;
    return { period, issueDate: `${period.slice(0, 8)}10`, alreadyIssued: false };
  }

  closeDialog() {
    this.dialogOpen.set(false);
    this.dialogError.set('');
    this.saving.set(false);
  }

  // ===== VENTANA (HU-094) =====
  // El popup es la ventana compartida (app-ventana); cambia de título y de botones según la vista.

  get tituloDialogo(): string {
    switch (this.dialogView()) {
      case 'active':
        return 'Bonificación activa';
      case 'confirmCancel':
        return 'Cancelar bonificación';
      case 'confirmVoid':
        return 'Anular bonificación';
      default:
        return this.isEditing ? 'Editar bonificación' : 'Asignar bonificación';
    }
  }

  // Volver / Confirmar solo en las confirmaciones y en el formulario; la vista de la bonificación activa
  // trae sus propios botones y la de carga no tiene ninguno.
  get dialogoConAcciones(): boolean {
    const view = this.dialogView();
    return view === 'confirmCancel' || view === 'confirmVoid' || view === 'form';
  }

  get dialogoDePeligro(): boolean {
    const view = this.dialogView();
    return view === 'confirmCancel' || view === 'confirmVoid';
  }

  get textoConfirmarDialogo(): string {
    switch (this.dialogView()) {
      case 'confirmCancel':
        return 'Sí, cancelar';
      case 'confirmVoid':
        return 'Sí, anular';
      default:
        return 'Guardar bonificación';
    }
  }

  get textoEnCursoDialogo(): string {
    switch (this.dialogView()) {
      case 'confirmCancel':
        return 'Cancelando…';
      case 'confirmVoid':
        return 'Anulando…';
      default:
        return 'Guardando…';
    }
  }

  volverDialogo() {
    if (this.dialogView() === 'form') {
      this.cancelForm();
    } else {
      this.backToActive();
    }
  }

  // El formulario se envía solo (Confirmar es su botón de envío); las confirmaciones llaman a su acción.
  confirmarDialogo() {
    if (this.dialogView() === 'confirmCancel') {
      this.confirmCancelBenefit();
    } else if (this.dialogView() === 'confirmVoid') {
      this.confirmVoidBenefit();
    }
  }

  // Switches the popup from showing a benefit to editing it. Without an
  // argument it edits the one on the card; the history list passes the one
  // that was clicked.
  startEditing(discount: DiscountModel | null = null) {
    const target = discount ?? this.currentDiscount();
    if (target === null || !this.esEditable(target)) {
      return;
    }

    this.currentDiscount.set(target);

    // Las bonificaciones viejas pueden empezar o terminar a mitad de mes: se leen por cuota (mes completo).
    const start = addMonthsToPeriod(target.startDate, 0);
    const end = lastDayOfMonth(target.endDate);

    this.benefitForm.enable({ emitEvent: false });
    this.buildStartOptions(start);
    this.benefitForm.reset({
      reason: target.type,
      valueType: target.valueType,
      percentage: target.percentage,
      fixedAmount: target.fixedAmount,
      startDate: start,
      endDate: end,
    });
    this.rebuildEndOptions();

    // En curso: lo ya emitido es historia, solo se mueve hasta qué cuota rige (el servidor lo exige igual).
    if (target.status === 'Active') {
      for (const campo of ['reason', 'valueType', 'percentage', 'fixedAmount', 'startDate']) {
        this.benefitForm.get(campo)!.disable({ emitEvent: false });
      }
    }

    this.dialogError.set('');
    this.dialogView.set('form');
  }

  // ===== EDICIÓN: quién y qué =====
  // Solo el SuperAdmin edita; el rol 2 asigna, cancela y anula. Y solo lo que todavía no es historia:
  // una bonificación finalizada, cancelada o anulada queda cerrada. La regla real vive en el servidor
  // (ReglasEdicionBeneficio); acá solo se evita ofrecer lo que la API va a rechazar.
  get puedeEditar(): boolean {
    return this.authService.isSuperAdmin();
  }

  esEditable(discount: DiscountModel): boolean {
    return this.puedeEditar && (discount.status === 'Active' || discount.status === 'Scheduled');
  }

  get editandoEnCurso(): boolean {
    return this.isEditing && this.currentDiscount()?.status === 'Active';
  }

  get motivoNoEditable(): string {
    if (!this.puedeEditar) {
      return 'Solo un SuperAdmin puede editar bonificaciones';
    }
    return 'Esta bonificación está cerrada y no se puede editar';
  }

  // Grants an additional benefit for a period the player does not have covered.
  // Possible since HU-012: what the API refuses is an overlapping range, not a
  // second benefit.
  startNewBenefit() {
    this.currentDiscount.set(null);
    this.resetBenefitForm();
    this.dialogError.set('');
    this.dialogView.set('form');
  }

  askCancelConfirmation() {
    this.dialogError.set('');
    this.dialogView.set('confirmCancel');
  }

  // Para un beneficio asignado por error: a diferencia de Cancelar, esto le saca el beneficio
  // a TODOS los meses que cubrió, pasados incluidos — por eso tiene su propia confirmación,
  // con su propia advertencia, en vez de compartir la de Cancelar.
  askVoidConfirmation() {
    this.dialogError.set('');
    this.dialogView.set('confirmVoid');
  }

  // Back out of the form. With benefits already loaded it returns to the card;
  // with none there is nothing to go back to, so the popup closes.
  backToActive() {
    this.dialogError.set('');

    if (this.playerDiscounts().length === 0) {
      this.closeDialog();
      return;
    }

    this.dialogView.set('active');
  }

  // True when the form is editing something that already exists
  get hasBenefits(): boolean {
    return this.playerDiscounts().length > 0;
  }

  // Opens the form with a sensible period already filled: desde la primera cuota que se puede becar hasta la
  // cuota de diciembre, que es el ciclo para el que el club otorga beneficios. Both are editable; they are a
  // starting point, not a decision.
  private resetBenefitForm() {
    const first = this.firstPeriod() ?? this.localFirstPeriod();
    this.firstPeriod.set(first);

    this.benefitForm.enable({ emitEvent: false });
    this.buildStartOptions();
    this.benefitForm.reset({
      reason: '',
      valueType: '',
      percentage: null,
      fixedAmount: null,
      startDate: first.period,
      endDate: lastDayOfMonth(`${first.period.slice(0, 4)}-12-01`),
    });
    this.rebuildEndOptions();
  }

  // ===== SELECTORES DE CUOTA =====
  // El servidor es quien decide desde qué cuota se puede becar; acá solo se ofrece lo que va a aceptar.

  // "10/11": el día en que se emite la cuota de ese período.
  private issueDayLabel(period: string): string {
    return `10/${period.slice(5, 7)}`;
  }

  // Cuotas desde las que puede empezar la bonificación. `current` es el inicio que ya tiene una bonificación
  // que se está editando: si es anterior a la primera permitida (una vieja, o una en curso) se agrega igual para
  // que el campo muestre lo que hay, aunque no se pueda elegir para una nueva.
  private buildStartOptions(current?: string) {
    const first = this.firstPeriod() ?? this.localFirstPeriod();
    const options: PeriodOption[] = [];

    for (let i = 0; i < BecadosDescuentos.START_MONTHS; i++) {
      const period = addMonthsToPeriod(first.period, i);
      const note = i === 0 && first.alreadyIssued ? 'ya emitida' : `se emite el ${this.issueDayLabel(period)}`;
      options.push({ value: period, label: `${periodLabel(period)} · ${note}` });
    }

    if (current && !options.some((option) => option.value === current)) {
      options.unshift({ value: current, label: periodLabel(current) });
    }

    this.startOptions.set(options);
  }

  // Cuotas en las que puede terminar. Desde la inicial; si se está editando una en curso, tampoco antes de la
  // última cuota ya emitida (esa ya se descontó y no se le puede sacar el beneficio).
  private rebuildEndOptions() {
    const start: string = this.benefitForm.get('startDate')?.value ?? '';
    if (!start) {
      this.endOptions.set([]);
      return;
    }

    const first = this.firstPeriod() ?? this.localFirstPeriod();
    const lastIssued = first.alreadyIssued ? first.period : addMonthsToPeriod(first.period, -1);
    const from = this.editandoEnCurso && lastIssued > start ? lastIssued : addMonthsToPeriod(start, 0);

    const options: PeriodOption[] = [];
    for (let i = 0; i < BecadosDescuentos.END_MONTHS; i++) {
      const period = addMonthsToPeriod(from, i);
      options.push({ value: lastDayOfMonth(period), label: periodLabel(period) });
    }

    // Lo que ya está guardado se muestra aunque quede fuera de la ventana de arriba.
    const endControl = this.benefitForm.get('endDate')!;
    const currentEnd: string = endControl.value ?? '';
    if (currentEnd && currentEnd >= start && !options.some((option) => option.value === currentEnd)) {
      options.push({ value: currentEnd, label: periodLabel(currentEnd) });
      options.sort((a, b) => a.value.localeCompare(b.value));
    }

    this.endOptions.set(options);

    // Si cambió el inicio y la cuota final quedó antes, pasa a ser la misma que la inicial.
    if (currentEnd && currentEnd < from) {
      endControl.setValue(options[0].value);
    }
  }

  // Lo que se le dice al operador sobre la primera cuota disponible, debajo del selector de inicio.
  get firstPeriodNote(): string {
    const first = this.firstPeriod();
    if (first === null || this.editandoEnCurso) {
      return '';
    }
    const name = periodLabel(first.period);
    return first.alreadyIssued
      ? `La cuota de ${name} ya está emitida y nadie pagó nada de ella, así que se puede becar. Las anteriores ya vencieron.`
      : `Rige desde la próxima cuota que todavía no se emitió: ${name} (se emite el ${this.issueDayLabel(first.period)}). Las anteriores ya se emitieron, y becarlas sería condonar deuda.`;
  }

  // ===== FORM =====

  get isEditing(): boolean {
    return this.currentDiscount() !== null;
  }

  get selectedValueType(): BenefitValueType | '' {
    return this.benefitForm.get('valueType')?.value ?? '';
  }

  // Switching between percentage and fixed amount clears the field that no
  // longer applies, so a value left over from the other mode can never travel
  // in the payload.
  onValueTypeChange() {
    if (this.selectedValueType === '%') {
      this.benefitForm.patchValue({ fixedAmount: null });
    } else {
      this.benefitForm.patchValue({ percentage: null });
    }
  }

  // Mirrors the DTO rules: the value that matches the type is required and has
  // to be in range, and the other one must stay empty.
  private benefitValueValidator = (group: AbstractControl): ValidationErrors | null => {
    const valueType = group.get('valueType')?.value;
    const percentage = group.get('percentage')?.value;
    const fixedAmount = group.get('fixedAmount')?.value;

    if (valueType === '%') {
      if (percentage === null || percentage === '' || percentage === undefined) {
        return { percentageRequired: true };
      }
      const value = Number(percentage);
      if (Number.isNaN(value) || value <= 0 || value > 100) {
        return { percentageOutOfRange: true };
      }
      return null;
    }

    if (valueType === '$') {
      if (fixedAmount === null || fixedAmount === '' || fixedAmount === undefined) {
        return { fixedAmountRequired: true };
      }
      const value = Number(fixedAmount);
      if (Number.isNaN(value) || value <= 0) {
        return { fixedAmountNotPositive: true };
      }
      return null;
    }

    return null;
  };

  // La cuota final no puede ser anterior a la inicial. Las dos fechas son de mes completo (primer día del mes de
  // inicio, último día del mes de fin), así que una bonificación de una sola cuota tiene inicio y fin en el mismo
  // mes y es válida. Lives on the group so it re-runs when either one changes.
  //
  // ISO strings compare correctly as text (yyyy-MM-dd sorts chronologically),
  // so there is no Date involved and no time zone to get wrong.
  private dateRangeValidator = (group: AbstractControl): ValidationErrors | null => {
    const start = group.get('startDate')?.value;
    const end = group.get('endDate')?.value;

    if (!start || !end) {
      return null;
    }

    return end < start ? { endBeforeStart: true } : null;
  };

  // The range message, sitting next to the two fee selectors. Los selectores solo ofrecen lo que el servidor va a
  // aceptar (nada de cuotas ya vencidas, ver firstPeriod), así que acá queda solo la comparación entre las dos.
  get dateRangeErrorMessage(): string {
    const errors = this.benefitForm.errors ?? {};
    return errors['endBeforeStart'] ? 'La cuota final no puede ser anterior a la inicial.' : '';
  }

  // Single message for the rest of the form, shown above the actions
  get formErrorMessage(): string {
    if (!this.benefitForm.touched && !this.benefitForm.dirty) {
      return '';
    }

    const errors = this.benefitForm.errors ?? {};

    if (this.benefitForm.get('reason')?.invalid) {
      return 'Seleccioná el motivo de la bonificación.';
    }
    if (this.benefitForm.get('valueType')?.invalid) {
      return 'Seleccioná si el beneficio es porcentual o de monto fijo.';
    }
    if (errors['percentageRequired']) {
      return 'Ingresá el porcentaje de descuento.';
    }
    if (errors['percentageOutOfRange']) {
      return 'El porcentaje debe ser mayor a 0 y no superar 100.';
    }
    if (errors['fixedAmountRequired']) {
      return 'Ingresá el monto del descuento.';
    }
    if (errors['fixedAmountNotPositive']) {
      return 'El monto debe ser mayor a cero.';
    }
    if (this.benefitForm.get('startDate')?.invalid) {
      return 'Indicá desde qué cuota rige la bonificación.';
    }
    if (this.benefitForm.get('endDate')?.invalid) {
      return 'Indicá hasta qué cuota rige la bonificación.';
    }

    // The range message has its own place, next to the dates
    return '';
  }

  // ===== WRITES =====

  onSubmit() {
    const player = this.dialogPlayer();
    if (player === null || this.benefitForm.invalid || this.saving()) {
      this.benefitForm.markAllAsTouched();
      return;
    }

    const request = this.buildRequest();
    const editing = this.isEditing;

    this.saving.set(true);
    this.dialogError.set('');

    const editingId = this.currentDiscount()?.id;

    const call = editing
      ? this.discountService.updateDiscount(player.id, request, editingId)
      : this.discountService.assignDiscount(player.id, request);

    call.subscribe({
      next: () => {
        this.saving.set(false);
        this.closeDialog();
        // Never trust the payload just sent: the grid is rebuilt from the
        // API so what the screen shows is what the database stored.
        this.refreshFromServer();
        // The toast fires here and nowhere else: only a response that
        // actually arrived counts as a success.
        this.notify(
          editing
            ? 'Bonificación actualizada correctamente.'
            : 'Bonificación asignada correctamente.',
        );
      },
      error: (error: HttpErrorResponse) => {
        this.saving.set(false);
        const mensaje = this.messageFor(error, editing ? 'editar' : 'guardar');
        this.dialogError.set(mensaje);
        this.notifications.notify(mensaje, 'error');
      },
    });
  }

  // El único punto donde el admin descarta a propósito lo que estaba
  // completando: por eso es el único lugar de esta pantalla que dispara la
  // notificación de "cambios no guardados", a diferencia de cerrar el popup
  // desde la tarjeta activa (ahí no había nada que perder).
  cancelForm() {
    this.notifications.notify('Cambios no guardados: se canceló la operación.', 'cancelled');

    if (this.hasBenefits) {
      this.backToActive();
    } else {
      this.closeDialog();
    }
  }

  confirmCancelBenefit() {
    const player = this.dialogPlayer();
    const target = this.currentDiscount();
    if (player === null || target === null || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.dialogError.set('');

    this.discountService.cancelDiscount(player.id, target.id).subscribe({
      next: () => {
        this.saving.set(false);
        this.closeDialog();
        this.refreshFromServer();
        this.notify('Bonificación cancelada correctamente.');
      },
      error: (error: HttpErrorResponse) => {
        this.saving.set(false);
        this.dialogView.set('active');
        const mensaje = this.messageFor(error, 'cancelar');
        this.dialogError.set(mensaje);
        this.notifications.notify(mensaje, 'error');
      },
    });
  }

  confirmVoidBenefit() {
    const player = this.dialogPlayer();
    const target = this.currentDiscount();
    if (player === null || target === null || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.dialogError.set('');

    this.discountService.voidDiscount(player.id, target.id).subscribe({
      next: () => {
        this.saving.set(false);
        this.closeDialog();
        this.refreshFromServer();
        this.notify('Bonificación anulada correctamente.');
      },
      error: (error: HttpErrorResponse) => {
        this.saving.set(false);
        this.dialogView.set('active');
        const mensaje = this.messageFor(error, 'anular');
        this.dialogError.set(mensaje);
        this.notifications.notify(mensaje, 'error');
      },
    });
  }

  private buildRequest(): DiscountRequest {
    // getRawValue: al editar una bonificación en curso los campos bloqueados también viajan (con su valor actual).
    const raw = this.benefitForm.getRawValue();
    const valueType: BenefitValueType = raw.valueType;

    return {
      reason: raw.reason,
      valueType,
      // The side that does not apply travels as null, the same exclusivity
      // the API and the table constraint demand.
      percentage: valueType === '%' ? Number(raw.percentage) : null,
      fixedAmount: valueType === '$' ? Number(raw.fixedAmount) : null,
      // Both are required by the form, so by this point they carry a value
      startDate: raw.startDate,
      endDate: raw.endDate,
    };
  }

  // Turns an API failure into something the administrator can act on. The API answers { mensaje } in
  // Spanish (including which benefit is in the way and its period); the texts below are only the fallback
  // when there is no message (for example, the server could not be reached).
  private messageFor(error: HttpErrorResponse, action: string): string {
    const mensajeApi = typeof error.error === 'object' ? error.error?.mensaje : null;
    if (typeof mensajeApi === 'string' && mensajeApi) {
      return mensajeApi;
    }
    if (error.status === 409) {
      // Anular's 409 is never an overlap (it takes no date range) — it means the benefit is
      // not a cancelled one anymore (someone else voided it, or the screen was out of date).
      if (action === 'anular') {
        return 'Esta bonificación ya no se puede anular: solo se anulan las canceladas que todavía no fueron anuladas. Actualizá la pantalla.';
      }
      return 'El jugador ya tiene una bonificación que se superpone con ese período. Ajustá las fechas o cancelá la existente.';
    }
    if (error.status === 400) {
      return 'Los datos de la bonificación no son válidos. Revisá el motivo, el valor y las fechas de vigencia.';
    }
    if (error.status === 401 || error.status === 403) {
      return 'Tu sesión no tiene permiso para esta acción. Volvé a iniciar sesión.';
    }
    if (error.status === 404) {
      return 'El jugador no tiene una bonificación activa.';
    }
    if (error.status === 0) {
      return 'No se pudo conectar con el servidor. Verificá que la API esté levantada.';
    }

    return `No se pudo ${action} la bonificación. Intentá nuevamente.`;
  }

  // ===== TABLE RENDERING =====

  // Everything below reads the state the server resolved. The browser never
  // works out whether a benefit is in force: its clock is not the club's.

  statusOf(row: DiscountModel): string {
    return statusLabel(row.status);
  }

  statusClassOf(row: DiscountModel): string {
    return statusToneClass(row.status);
  }

  benefitValueOf(row: DiscountModel): string {
    return formatBenefitValue(row);
  }

  // "Octubre 2026 a Diciembre 2026": por cuota, que es como rige una bonificación
  validityOf(row: DiscountModel): string {
    return formatValidity(row);
  }

  formatDate(isoDate: string): string {
    return formatIsoDate(isoDate) || '—';
  }

  // "Octubre 2026" (la cuota que contiene esa fecha)
  periodOf(isoDate: string): string {
    return periodLabel(isoDate) || '—';
  }

  responsableNombre(discount: DiscountModel): string {
    return [discount.responsableNombre, discount.responsableApellido].filter(Boolean).join(' ') || 'Sin registro histórico';
  }

  // Only called after the API confirmed the write
  private notify(message: string) {
    this.notifications.notify(message, 'success');
  }
}
