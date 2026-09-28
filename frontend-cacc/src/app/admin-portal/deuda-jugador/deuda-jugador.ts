import { ChangeDetectorRef, Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';
import { JugadoresService } from '../../services/jugadores';
import { CuotaPendienteDetalle, JugadorResumen, PagosService } from '../../services/pagos';
import { CustomSelect } from '../../shared/custom-select/custom-select';

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
  imports: [CommonModule, FormsModule, CustomSelect],
  templateUrl: './deuda-jugador.html',
  styleUrl: './deuda-jugador.css',
})
export class DeudaJugador implements OnInit, OnDestroy {
  jugador: JugadorResumen | null = null;
  cuotas: CuotaPendienteDetalle[] = [];

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

  private idJugador = 0;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private jugadoresService: JugadoresService,
    private pagosService: PagosService,
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
  }

  get iniciales(): string {
    if (!this.jugador) {
      return '';
    }
    return `${this.jugador.apellido[0] ?? ''}${this.jugador.nombre[0] ?? ''}`.toUpperCase();
  }

  get montoTotalAdeudado(): number {
    return this.cuotas.reduce((total, cuota) => total + cuota.saldoPendiente, 0);
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

  volver() {
    this.router.navigate(['/admin/portal/cuotas-pagos']);
  }

  ngOnDestroy() {
    clearTimeout(this.mensajeExitoTimer);
  }

  abrirPago(cuota: CuotaPendienteDetalle) {
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

  confirmarPago(cuota: CuotaPendienteDetalle) {
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
          this.cargarDeuda(); // refresca saldos/abonos (y hace desaparecer la cuota si quedó completa)
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

  // La cuota no trae una fecha de vencimiento propia, solo "Agosto 2026" — se asume
  // vencida al cierre del mes que nombra (mismo criterio que usa el club para cobrar:
  // la cuota de un mes se paga durante ese mes).
  private vencimientoDe(periodo: string): Date | null {
    const [nombreMes, anioTexto] = periodo.split(' ');
    const mesIndex = NOMBRES_MES.indexOf(nombreMes.toLowerCase());
    const anio = Number(anioTexto);
    if (mesIndex === -1 || !anio) {
      return null;
    }
    return new Date(anio, mesIndex + 1, 0); // día 0 del mes siguiente = último día de "mesIndex"
  }

  // VENCIDA: ya pasó el cierre del mes y todavía tiene saldo. PARCIAL: no venció aún pero
  // ya tiene algún abono cargado. PRÓXIMA: todavía no venció y no se abonó nada.
  // (Cubierta por beneficio se maneja aparte, en el template, con saldoPendiente <= 0.)
  estadoCuota(cuota: CuotaPendienteDetalle): EstadoCuota {
    const vencimiento = this.vencimientoDe(cuota.periodo);
    if (vencimiento && new Date() > vencimiento) {
      return 'vencida';
    }
    return cuota.abonos.length > 0 ? 'parcial' : 'proxima';
  }

  diasVencida(cuota: CuotaPendienteDetalle): number {
    const vencimiento = this.vencimientoDe(cuota.periodo);
    if (!vencimiento) {
      return 0;
    }
    return Math.max(0, Math.floor((Date.now() - vencimiento.getTime()) / (1000 * 60 * 60 * 24)));
  }

  // Texto del badge de beneficio: "Becado (100%)" o "Descuento ($15.000)".
  beneficioTexto(cuota: CuotaPendienteDetalle): string {
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
