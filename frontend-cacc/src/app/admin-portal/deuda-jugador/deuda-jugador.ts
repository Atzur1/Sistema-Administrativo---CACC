import { ChangeDetectorRef, Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';
import { forkJoin } from 'rxjs';
import { JugadoresService } from '../../services/jugadores';
import { PagosService, feeToCuotaJugador } from '../../services/pagos';
import { CustomSelect } from '../../shared/custom-select/custom-select';
import { NotificationService } from '../../shared/notifications/notification.service';
import { financialLoadErrorMessage } from '../../shared/http-error-message';
import { estaVencida, vencimientoDelPeriodo } from '../../shared/calendario-cuotas';
import { Ventana } from '../../shared/ventana/ventana';
import { PlayerSummaryModel } from '../../models/PlayerSummaryModel';
import { PendingFeeDetailModel, PlayerFeeModel, PlayerStatementModel } from '../../models/PaymentModel';

const CURRENCY_FULL = new Intl.NumberFormat('es-AR', {
  style: 'currency',
  currency: 'ARS',
  maximumFractionDigits: 0,
});

const NOMBRES_MES = [
  'enero',
  'febrero',
  'marzo',
  'abril',
  'mayo',
  'junio',
  'julio',
  'agosto',
  'septiembre',
  'octubre',
  'noviembre',
  'diciembre',
];

export type EstadoCuota = 'vencida' | 'parcial' | 'proxima';

// Deuda pendiente de un jugador: mismo layout que Perfil de Jugador (Historial de Pagos), pero
// mostrando lo que TODAVÍA debe en vez de lo que ya pagó. Se llega acá haciendo click en un
// jugador desde "Pendientes de cobro" en Cuotas y Pagos.
@Component({
  selector: 'app-deuda-jugador',
  standalone: true,
  imports: [CommonModule, FormsModule, CustomSelect, Ventana],
  templateUrl: './deuda-jugador.html',
  styleUrl: './deuda-jugador.css',
})
export class DeudaJugador implements OnInit, OnDestroy {
  jugador: PlayerSummaryModel | null = null;
  cuotas: PendingFeeDetailModel[] = [];

  cargandoJugador = true;
  cargandoDeuda = true;
  errorMessage = '';

  // ===== "Pagar saldo" inline, sin salir de esta pantalla =====
  // Pega contra el mismo POST /api/pagos/registrar que usa "Registrar pago" en Cuotas y Pagos
  // (ya soporta pagos parciales, rechazo de sobre-pago, etc.)
  pagandoId: number | null = null;
  montoPagoDisplay = '';
  metodoPago = '';
  enviandoPago = false;
  errorPago = '';
  // Mensaje a nivel de página (no por cuota): si el pago completó el saldo, la tarjeta de esa
  // cuota desaparece al refrescar la lista, así que un mensaje atado a esa cuota nunca se vería.
  mensajeExito = '';
  private mensajeExitoTimer?: ReturnType<typeof setTimeout>;

  // ===== HU-025: cobro de una o varias cuotas completas (POST /api/pagos/cobro) =====
  // Convive con "Pagar saldo" (abono parcial de una cuota). La tabla sale del estado de cuenta
  // (HU-024) y, después de cada cobro, se vuelve a leer: el estado "Pagado" siempre lo informa
  // el backend, nunca se pinta localmente.
  cuotasJugador: PlayerFeeModel[] = [];
  cargandoCuotas = true;
  errorCuotas = '';
  readonly metodosCobro = ['Efectivo', 'Transferencia'];
  metodoCobro = '';
  confirmacionAbierta = false;
  enviandoCobro = false;
  errorCobro = '';
  private seleccion = new Set<number>();

  // ===== HU-024: estado de cuenta (GET /api/pagos/jugador/{id}/estado-de-cuenta) =====
  // La tarjeta "Total a abonar" y la tabla de cuotas salen de esa misma respuesta, así el total
  // siempre coincide con la tabla. Solo cuotas mensuales: la inscripción se abona por separado.
  estadoCuentaCargado = false; // La tarjeta se muestra (incluido $0) solo con datos reales del backend
  private totalAAbonar = 0;

  // ===== HU-026: estado de cuenta después de un cobro =====
  actualizandoEstadoCuenta = false;
  avisoActualizacion = '';
  private ultimaActualizacionTrasPago = false;

  private idJugador = 0;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private jugadoresService: JugadoresService,
    private pagosService: PagosService,
    private notifications: NotificationService,
    private cdr: ChangeDetectorRef,
  ) {}

  ngOnInit() {
    this.idJugador = Number(this.route.snapshot.paramMap.get('id'));
    if (!this.idJugador) {
      this.router.navigate(['/admin/portal/cuotas-pagos']);
      return;
    }

    this.cargarJugador();
    this.cargarDeuda();
    this.cargarCuotas();
  }

  get iniciales(): string {
    if (!this.jugador) {
      return '';
    }
    return `${this.jugador.apellido[0] ?? ''}${this.jugador.nombre[0] ?? ''}`.toUpperCase();
  }

  // HU-024: lo calcula el backend (saldo de las cuotas Pendientes y Vencidas).
  get montoTotalAdeudado(): number {
    return this.totalAAbonar;
  }

  get cuotasAdeudadas(): number {
    return this.cuotasJugador.filter((c) => c.estado !== 'Pagado' && c.saldoPendiente > 0).length;
  }

  private cargarJugador() {
    this.jugadoresService.getJugador(this.idJugador).subscribe({
      next: (jugador) => {
        this.jugador = jugador;
        this.cargandoJugador = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.cargandoJugador = false;
        this.errorMessage = 'No se encontró el jugador solicitado.';
        this.cdr.detectChanges();
      },
    });
  }

  private cargarDeuda() {
    this.cargandoDeuda = true;
    this.pagosService.getDeuda(this.idJugador).subscribe({
      next: (cuotas) => {
        this.cuotas = cuotas;
        this.cargandoDeuda = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.cargandoDeuda = false;
        this.errorMessage = 'No se pudo cargar la deuda de este jugador.';
        this.cdr.detectChanges();
      },
    });
  }

  private cargarCuotas() {
    this.cargandoCuotas = true;
    this.pagosService.getEstadoDeCuenta(this.idJugador).subscribe({
      next: (estado) => {
        this.aplicarEstadoDeCuenta(estado);
        this.cargandoCuotas = false;
        this.errorCuotas = '';
        this.cdr.detectChanges();
      },
      error: (err: unknown) => {
        this.cargandoCuotas = false;
        this.errorCuotas = financialLoadErrorMessage(err, 'No se pudo cargar el estado de cuenta de este jugador.');
        this.notifications.notify(this.errorCuotas, 'error');
        this.cdr.detectChanges();
      },
    });
  }

  private aplicarEstadoDeCuenta(estado: PlayerStatementModel) {
    this.totalAAbonar = estado.totalDebtAmount;
    this.estadoCuentaCargado = true;
    this.aplicarCuotas(estado.fees.map(feeToCuotaJugador));
  }

  private aplicarCuotas(cuotas: PlayerFeeModel[]) {
    this.cuotasJugador = cuotas;
    // Una cuota que dejó de ser cobrable (por ejemplo, la cobró otro administrador) sale
    // de la selección: el total nunca incluye algo que el backend ya no aceptaría.
    this.seleccion = new Set(
      [...this.seleccion].filter((id) =>
        cuotas.some((c) => c.idPago === id && this.esSeleccionable(c)),
      ),
    );
  }

  // HU-026: tras un cobro se vuelven a leer del backend el estado de cuenta y la deuda, juntos,
  // sin recargar la página. Si esa lectura falla, el cobro NO se reintenta (ya quedó persistido):
  // se avisa que la vista puede estar desactualizada y se ofrece repetir solo la lectura.
  private actualizarEstadoDeCuenta(trasPagoConfirmado: boolean) {
    this.ultimaActualizacionTrasPago = trasPagoConfirmado;
    this.actualizandoEstadoCuenta = true;
    this.cdr.detectChanges();

    forkJoin({
      deuda: this.pagosService.getDeuda(this.idJugador),
      estado: this.pagosService.getEstadoDeCuenta(this.idJugador),
    }).subscribe({
      next: ({ deuda, estado }) => {
        this.cuotas = deuda;
        this.aplicarEstadoDeCuenta(estado);
        this.errorCuotas = '';
        this.avisoActualizacion = '';
        this.actualizandoEstadoCuenta = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.actualizandoEstadoCuenta = false;
        this.avisoActualizacion = trasPagoConfirmado
          ? 'El pago se registró correctamente, pero no se pudo actualizar el estado de cuenta en pantalla. Los datos mostrados pueden estar desactualizados.'
          : 'No se pudo actualizar el estado de cuenta. Los datos mostrados pueden estar desactualizados.';
        this.cdr.detectChanges();
      },
    });
  }

  // Solo vuelve a LEER cuotas y deuda; nunca repite el cobro.
  reintentarActualizacion() {
    if (this.actualizandoEstadoCuenta) {
      return;
    }
    this.actualizarEstadoDeCuenta(this.ultimaActualizacionTrasPago);
  }

  esSeleccionable(cuota: PlayerFeeModel): boolean {
    return cuota.estado !== 'Pagado' && !cuota.cubiertaPorBeneficio && cuota.saldoPendiente > 0;
  }

  estaSeleccionada(cuota: PlayerFeeModel): boolean {
    return this.seleccion.has(cuota.idPago);
  }

  alternarCuota(cuota: PlayerFeeModel) {
    if (!this.esSeleccionable(cuota) || this.enviandoCobro) {
      return;
    }
    if (this.seleccion.has(cuota.idPago)) {
      this.seleccion.delete(cuota.idPago);
    } else {
      this.seleccion.add(cuota.idPago);
    }
    this.cdr.detectChanges();
  }

  // Las que se pueden cobrar ahora: las pendientes o vencidas con saldo (no las pagadas ni las cubiertas por beneficio).
  get cuotasSeleccionables(): PlayerFeeModel[] {
    return this.cuotasJugador.filter((c) => this.esSeleccionable(c));
  }

  get todasSeleccionadas(): boolean {
    const seleccionables = this.cuotasSeleccionables;
    return seleccionables.length > 0 && seleccionables.every((c) => this.seleccion.has(c.idPago));
  }

  // Casilla del encabezado: queda "a medias" cuando hay algunas elegidas pero no todas.
  get seleccionParcial(): boolean {
    return this.cuotasSeleccionadas.length > 0 && !this.todasSeleccionadas;
  }

  // Selecciona todas las cuotas cobrables de una vez (para pagar toda la deuda); si ya estaban todas
  // seleccionadas, las deselecciona. Mismas reglas que elegirlas una por una: solo las seleccionables.
  alternarTodas() {
    if (this.enviandoCobro || this.cuotasSeleccionables.length === 0) {
      return;
    }
    if (this.todasSeleccionadas) {
      this.seleccion.clear();
    } else {
      this.cuotasSeleccionables.forEach((c) => this.seleccion.add(c.idPago));
    }
    this.cdr.detectChanges();
  }

  get cuotasSeleccionadas(): PlayerFeeModel[] {
    return this.cuotasJugador.filter((c) => this.seleccion.has(c.idPago));
  }

  get totalSeleccionado(): number {
    return this.cuotasSeleccionadas.reduce((total, c) => total + c.saldoPendiente, 0);
  }

  get puedeRegistrarPago(): boolean {
    return (
      this.cuotasSeleccionadas.length > 0 &&
      this.metodosCobro.includes(this.metodoCobro) &&
      !this.enviandoCobro
    );
  }

  abrirConfirmacion() {
    if (!this.puedeRegistrarPago) {
      return;
    }
    this.errorCobro = '';
    this.confirmacionAbierta = true;
    this.cdr.detectChanges();
  }

  cerrarConfirmacion() {
    if (this.enviandoCobro) {
      return;
    }
    this.confirmacionAbierta = false;
    this.errorCobro = '';
    this.cdr.detectChanges();
  }

  confirmarCobro() {
    const cuotas = this.cuotasSeleccionadas;
    if (cuotas.length === 0 || !this.metodosCobro.includes(this.metodoCobro) || this.enviandoCobro) {
      return;
    }

    this.enviandoCobro = true;
    this.errorCobro = '';
    this.cdr.detectChanges();

    this.pagosService
      .cobrarCuotas(
        this.idJugador,
        cuotas.map((c) => c.idPago),
        this.metodoCobro,
      )
      .subscribe({
        next: (respuesta) => {
          this.enviandoCobro = false;
          this.confirmacionAbierta = false;
          this.seleccion.clear();
          this.metodoCobro = '';
          // HU-026: el éxito se informa recién acá, con la respuesta 200 del backend, y con la
          // cantidad y el total que devolvió la API (no los calculados en pantalla).
          const cantidad = respuesta.cuotas.length;
          const detalle =
            cantidad === 1
              ? `Se abonó 1 cuota por ${this.formatMonto(respuesta.montoTotal)}.`
              : `Se abonaron ${cantidad} cuotas por ${this.formatMonto(respuesta.montoTotal)}.`;
          this.notifications.notify(`Pago registrado correctamente. ${detalle}`, 'success');
          this.actualizarEstadoDeCuenta(true);
        },
        error: (err: HttpErrorResponse) => {
          this.enviandoCobro = false;
          this.errorCobro = this.mensajeErrorCobro(err);
          this.notifications.notify(this.errorCobro, 'error');
          // Un rechazo de negocio suele significar datos viejos en pantalla (otra sesión cobró
          // una cuota): se relee el estado real, sin marcar nada como pagado localmente.
          if (err.status === 400) {
            this.actualizarEstadoDeCuenta(false);
          }
          this.cdr.detectChanges();
        },
      });
  }

  private mensajeErrorCobro(err: HttpErrorResponse): string {
    const mensajeApi: string | undefined = err.error?.mensaje;
    switch (err.status) {
      case 0:
        return 'No se pudo conectar con el servidor. Revisá tu conexión e intentá de nuevo.';
      case 400:
        return mensajeApi ?? 'La operación fue rechazada. Revisá las cuotas seleccionadas.';
      case 401:
        return 'Tu sesión expiró. Volvé a iniciar sesión para registrar el pago.';
      case 403:
        return 'No tenés permisos para registrar pagos.';
      case 500:
        return mensajeApi ?? 'Error de base de datos al registrar el pago. No se cobró ninguna cuota.';
      default:
        return 'Ocurrió un error inesperado. No se cobró ninguna cuota.';
    }
  }

  // "Vence" el día que informa la API: el 10 del mes siguiente al que cubre la cuota.
  vencimientoCuota(cuota: PlayerFeeModel): string {
    const [anio, mes, dia] = cuota.fechaVencimiento.slice(0, 10).split('-').map(Number);
    return new Date(anio, mes - 1, dia).toLocaleDateString('es-AR', { day: '2-digit', month: '2-digit', year: 'numeric' });
  }

  // HU-093: tono de la etiqueta de estado compartida (styles.css).
  estadoCuotaClase(cuota: PlayerFeeModel): string {
    if (cuota.estado === 'Pagado' || cuota.cubiertaPorBeneficio) {
      return 'tono-exito';
    }
    return cuota.estado === 'Vencido' ? 'tono-peligro' : 'tono-info';
  }

  // Vencida es un problema, parcial pide atención y próxima es un dato a futuro (todavía no vence).
  tonoCuotaPendiente(estado: EstadoCuota): string {
    if (estado === 'vencida') {
      return 'tono-peligro';
    }
    return estado === 'parcial' ? 'tono-aviso' : 'tono-info';
  }

  estadoCuotaTexto(cuota: PlayerFeeModel): string {
    return cuota.estado !== 'Pagado' && cuota.cubiertaPorBeneficio ? 'Cubierta' : cuota.estado;
  }

  volver() {
    this.router.navigate(['/admin/portal/cuotas-pagos']);
  }

  ngOnDestroy() {
    clearTimeout(this.mensajeExitoTimer);
  }

  abrirPago(cuota: PendingFeeDetailModel) {
    this.pagandoId = cuota.idPago;
    this.montoPagoDisplay = cuota.saldoPendiente.toLocaleString('es-AR');
    this.metodoPago = '';
    this.cdr.detectChanges();
  }

  cancelarPago() {
    this.pagandoId = null;
    this.montoPagoDisplay = '';
    this.metodoPago = '';
    this.errorPago = '';
    this.cdr.detectChanges();
  }

  // Mismo formateo con puntos de miles que "Registrar pago" en Cuotas y Pagos.
  onMontoPagoInput(target: HTMLInputElement) {
    const cursorPos = target.selectionStart ?? target.value.length;
    const digitsBeforeCursor = target.value.slice(0, cursorPos).replace(/\D/g, '').length;

    const digitsOnly = target.value.replace(/\D/g, '').slice(0, 12);
    const formatted = digitsOnly ? Number(digitsOnly).toLocaleString('es-AR') : '';

    target.value = formatted;
    this.montoPagoDisplay = formatted;

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

  confirmarPago(cuota: PendingFeeDetailModel) {
    const monto = Number(this.montoPagoDisplay.replace(/\D/g, ''));
    if (!monto || !this.metodoPago) {
      return;
    }

    // cuota.periodo es "Enero 2026": el endpoint necesita mes y año por separado.
    const [nombreMes, anioTexto] = cuota.periodo.split(' ');
    const anio = Number(anioTexto);

    this.enviandoPago = true;
    this.errorPago = '';

    this.pagosService
      .registrarPago(this.idJugador, nombreMes, anio, monto, this.metodoPago)
      .subscribe({
        next: () => {
          this.enviandoPago = false;
          this.pagandoId = null;
          this.montoPagoDisplay = '';
          this.metodoPago = '';
          this.mostrarExito(`Pago de ${this.formatMonto(monto)} registrado correctamente.`);
          this.actualizarEstadoDeCuenta(true); // refresca saldos/abonos y la tabla de cuotas
          this.cdr.detectChanges();
        },
        error: (err: HttpErrorResponse) => {
          this.enviandoPago = false;
          this.errorPago = err.error?.mensaje ?? 'No se pudo registrar el pago. Intentá de nuevo.';
          this.cdr.detectChanges();
        },
      });
  }

  private mostrarExito(texto: string) {
    clearTimeout(this.mensajeExitoTimer);
    this.mensajeExito = texto;
    this.mensajeExitoTimer = setTimeout(() => {
      this.mensajeExito = '';
      this.cdr.detectChanges();
    }, 4000);
  }

  formatFecha(iso: string): string {
    return new Date(iso).toLocaleDateString('es-AR', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
    });
  }

  formatMonto(valor: number): string {
    return CURRENCY_FULL.format(valor);
  }

  // La cuota no trae una fecha de vencimiento propia, solo "Agosto 2026": vence el 10 del mes
  // siguiente al que nombra (regla del club, la misma que aplica la API).
  private vencimientoDe(periodo: string): Date | null {
    const [nombreMes, anioTexto] = periodo.split(' ');
    const mesIndex = NOMBRES_MES.indexOf(nombreMes.toLowerCase());
    const anio = Number(anioTexto);
    if (mesIndex === -1 || !anio) {
      return null;
    }
    return vencimientoDelPeriodo(anio, mesIndex);
  }

  // VENCIDA: ya llegó el día 10 del mes siguiente y todavía tiene saldo. PARCIAL: no venció aún pero
  // ya tiene algún abono cargado. PRÓXIMA: todavía no venció y no se abonó nada.
  // (Cubierta por beneficio se maneja aparte, en el template, con saldoPendiente <= 0.)
  estadoCuota(cuota: PendingFeeDetailModel): EstadoCuota {
    const vencimiento = this.vencimientoDe(cuota.periodo);
    if (vencimiento && estaVencida(vencimiento)) {
      return 'vencida';
    }
    return cuota.abonos.length > 0 ? 'parcial' : 'proxima';
  }

  diasVencida(cuota: PendingFeeDetailModel): number {
    const vencimiento = this.vencimientoDe(cuota.periodo);
    if (!vencimiento) {
      return 0;
    }
    return Math.max(0, Math.floor((Date.now() - vencimiento.getTime()) / (1000 * 60 * 60 * 24)));
  }

  // Texto del badge de beneficio: "Becado (100%)" o "Descuento ($15.000)".
  beneficioTexto(cuota: PendingFeeDetailModel): string {
    if (!cuota.tieneBeneficio) {
      return '';
    }
    const valor =
      cuota.tipoValorBeneficio === '%'
        ? `${cuota.porcentajeBeneficio}%`
        : this.formatMonto(cuota.montoFijoBeneficio ?? 0);
    return `${cuota.motivoBeneficio} (${valor})`;
  }
}
