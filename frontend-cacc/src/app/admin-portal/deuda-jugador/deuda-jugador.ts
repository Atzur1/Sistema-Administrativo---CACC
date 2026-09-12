import { ChangeDetectorRef, Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';
import { JugadoresService } from '../../services/jugadores';
import { CuotaPendienteDetalle, JugadorResumen, PagosService } from '../../services/pagos';

const CURRENCY_FULL = new Intl.NumberFormat('es-AR', {
  style: 'currency',
  currency: 'ARS',
  maximumFractionDigits: 0,
});

// Deuda pendiente de un jugador: mismo layout que Perfil de Jugador (Historial de Pagos), pero
// mostrando lo que TODAVÍA debe en vez de lo que ya pagó. Se llega acá haciendo click en un
// jugador desde "Pendientes de cobro" en Cuotas y Pagos.
@Component({
  selector: 'app-deuda-jugador',
  standalone: true,
  imports: [CommonModule, FormsModule],
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
    private cdr: ChangeDetectorRef
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

    // cuota.periodo es "Enero 2026": el endpoint solo necesita el nombre del mes.
    const nombreMes = cuota.periodo.split(' ')[0];

    this.enviandoPago = true;
    this.errorPago = '';

    this.pagosService.registrarPago(this.idJugador, nombreMes, monto, this.metodoPago).subscribe({
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
    return new Date(iso).toLocaleDateString('es-AR', { day: '2-digit', month: '2-digit', year: 'numeric' });
  }

  formatMonto(valor: number): string {
    return CURRENCY_FULL.format(valor);
  }
}
