import { ChangeDetectorRef, Component, OnDestroy, OnInit } from '@angular/core';
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
import { formatCompactCurrency } from '../../shared/format-currency';

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
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './cuotas-pagos.html',
  styleUrl: './cuotas-pagos.css',
})
export class CuotasPagos implements OnInit, OnDestroy {

  headerMetrics: HeaderMetric[] = [
    { value: '—', label: `Recaudado ${new Date().getFullYear()}` },
    { value: '—', label: 'Pagos del mes' },
    { value: '—', label: 'Pendientes' },
  ];

  paymentForm: FormGroup;

  periods = [
    'Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio',
    'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre',
  ];

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

  successMessage = '';
  successLeaving = false;
  errorMessage = '';
  enviando = false;

  cargandoJugadores = false;
  cargandoListas = false;

  pendingRows: PendingRow[] = [];
  paymentRows: PaymentRow[] = [];

  private fadeTimer?: ReturnType<typeof setTimeout>;
  private clearTimer?: ReturnType<typeof setTimeout>;

  // Lo que se ve en el input de Monto ("1.000"). El control del form (paymentForm.get('amount'))
  // guarda el número limpio sin puntos ("1000"), que es lo que se valida y se manda al backend.
  montoDisplay = '';

  constructor(
    private fb: FormBuilder,
    private pagosService: PagosService,
    private cdr: ChangeDetectorRef,
    private router: Router
  ) {
    this.paymentForm = this.fb.group({
      player: ['', [Validators.required, this.knownPlayerValidator]],
      period: ['', [Validators.required]],
      amount: ['', [Validators.required, Validators.min(1)]],
      method: ['', [Validators.required]],
    });
  }

  irAJugador(idJugador: number) {
    this.router.navigate(['/admin/portal/jugadores', idJugador]);
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

  ngOnInit() {
    this.cargarJugadores();
    this.cargarListas();
  }

  ngOnDestroy() {
    clearTimeout(this.fadeTimer);
    clearTimeout(this.clearTimer);
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

  onPlayerSearch(term: string) {
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

    const { period, amount, method } = this.paymentForm.value;
    const jugador = this.selectedPlayer;

    this.enviando = true;
    this.errorMessage = '';
    this.clearSuccessMessage();

    this.pagosService.registrarPago(jugador.idJugador, period, Number(amount), method).subscribe({
      next: () => {
        this.enviando = false;
        this.paymentForm.reset({ player: '', period: '', amount: '', method: '' });
        this.montoDisplay = '';
        this.selectedPlayer = null;
        this.matchingPlayers = [];
        this.showSuggestions = false;
        this.showConfirmation(`Pago de ${jugador.nombreCompleto} registrado correctamente.`);
        this.cargarListas();
        this.cdr.detectChanges();
      },
      error: (err: HttpErrorResponse) => {
        this.enviando = false;
        this.errorMessage = err.error?.mensaje ?? 'No se pudo registrar el pago. Intentá de nuevo.';
        this.cdr.detectChanges();
      },
    });
  }

  private showConfirmation(message: string) {
    this.clearSuccessMessage();

    this.successMessage = message;
    this.successLeaving = false;

    this.fadeTimer = setTimeout(() => (this.successLeaving = true), 2700);
    this.clearTimer = setTimeout(() => {
      this.successMessage = '';
      this.successLeaving = false;
    }, 3000);
  }

  private clearSuccessMessage() {
    clearTimeout(this.fadeTimer);
    clearTimeout(this.clearTimer);
    this.successMessage = '';
    this.successLeaving = false;
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
