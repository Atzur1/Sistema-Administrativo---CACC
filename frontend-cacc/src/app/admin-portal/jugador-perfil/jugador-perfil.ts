import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';
import { Observable } from 'rxjs';
import {
  HistorialPagosResultado,
  JugadoresService,
  PagoHistorialAbono,
  PagoHistorialItem,
} from '../../services/jugadores';
import { JugadorResumen } from '../../services/pagos';
import { formatCompactCurrency } from '../../shared/format-currency';
import { NotificationService } from '../../shared/notifications/notification.service';
import { CustomDatepicker } from '../../shared/custom-datepicker/custom-datepicker';
import { HelpHint } from '../../shared/help-hint/help-hint';

type DialogoPerfil = 'baja' | 'reactivar' | 'anular';

const CURRENCY_FULL = new Intl.NumberFormat('es-AR', {
  style: 'currency',
  currency: 'ARS',
  maximumFractionDigits: 0,
});

@Component({
  selector: 'app-jugador-perfil',
  standalone: true,
  imports: [CommonModule, FormsModule, CustomDatepicker, HelpHint],
  templateUrl: './jugador-perfil.html',
  styleUrl: './jugador-perfil.css',
})
export class JugadorPerfil implements OnInit {
  jugador: JugadorResumen | null = null;
  historial: HistorialPagosResultado | null = null;

  cargandoJugador = true;
  cargandoHistorial = true;
  errorMessage = '';

  page = 1;
  readonly pageSize = 10;

  private idJugador = 0;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private jugadoresService: JugadoresService,
    private cdr: ChangeDetectorRef,
    private notifications: NotificationService,
  ) {}

  ngOnInit() {
    this.idJugador = Number(this.route.snapshot.paramMap.get('id'));
    if (!this.idJugador) {
      this.router.navigate(['/admin/portal/cuotas-pagos']);
      return;
    }

    this.cargarJugador();
    this.cargarHistorial();
  }

  get totalPaginas(): number {
    if (!this.historial || this.historial.total === 0) {
      return 1;
    }
    return Math.ceil(this.historial.total / this.pageSize);
  }

  get iniciales(): string {
    if (!this.jugador) {
      return '';
    }
    return `${this.jugador.apellido[0] ?? ''}${this.jugador.nombre[0] ?? ''}`.toUpperCase();
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

  private cargarHistorial() {
    this.cargandoHistorial = true;
    this.jugadoresService.getHistorialPagos(this.idJugador, this.page, this.pageSize).subscribe({
      next: (historial) => {
        this.historial = historial;
        this.cargandoHistorial = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.cargandoHistorial = false;
        this.errorMessage = 'No se pudo cargar el historial de pagos.';
        this.cdr.detectChanges();
      },
    });
  }

  irAPagina(nuevaPagina: number) {
    if (nuevaPagina < 1 || nuevaPagina > this.totalPaginas || nuevaPagina === this.page) {
      return;
    }
    this.page = nuevaPagina;
    this.cargarHistorial();
  }

  // Única pantalla que enlaza acá desde que "Últimos pagos" se mudó por completo a
  // Actividad y Movimientos (antes era Cuotas y Pagos).
  volver() {
    this.router.navigate(['/admin/portal/actividad-movimientos']);
  }

  // ===== Baja / reactivación y anulación de pagos (solo SuperAdmin, igual que esta pantalla) =====

  // Mismo popup de confirmación que Becados y Descuentos y Deuda de Jugador: nada se graba hasta
  // el botón de confirmar, y el error de la API se muestra adentro del popup.
  dialogo: DialogoPerfil | null = null;
  procesando = false;
  errorDialogo = '';

  fechaBaja = '';
  abonoAnular: { abono: PagoHistorialAbono; item: PagoHistorialItem } | null = null;
  motivoAnulacion = '';
  readonly motivoMinimo = 5;
  readonly motivoMaximo = 500;

  get dadoDeBaja(): boolean {
    return !!this.jugador?.fechaBaja;
  }

  get hoy(): string {
    const ahora = new Date();
    return `${ahora.getFullYear()}-${`${ahora.getMonth() + 1}`.padStart(2, '0')}-${`${ahora.getDate()}`.padStart(2, '0')}`;
  }

  // La baja no puede ser anterior al alta (el servidor lo valida igual).
  get minFechaBaja(): string {
    return this.jugador?.fechaAlta ? this.jugador.fechaAlta.substring(0, 10) : '';
  }

  get motivoValido(): boolean {
    const largo = this.motivoAnulacion.trim().length;
    return largo >= this.motivoMinimo && largo <= this.motivoMaximo;
  }

  abrirBaja() {
    this.fechaBaja = this.hoy;
    this.abrir('baja');
  }

  abrirReactivar() {
    this.abrir('reactivar');
  }

  abrirAnulacion(abono: PagoHistorialAbono, item: PagoHistorialItem) {
    this.abonoAnular = { abono, item };
    this.motivoAnulacion = '';
    this.abrir('anular');
  }

  cerrarDialogo() {
    if (this.procesando) {
      return;
    }
    this.dialogo = null;
    this.abonoAnular = null;
    this.errorDialogo = '';
  }

  confirmarBaja() {
    if (!this.fechaBaja) {
      this.errorDialogo = 'Indicá la fecha en que el jugador dejó el club.';
      return;
    }
    this.ejecutar(this.jugadoresService.darDeBaja(this.idJugador, this.fechaBaja), 'No se pudo dar de baja al jugador.', true);
  }

  confirmarReactivar() {
    this.ejecutar(this.jugadoresService.reactivar(this.idJugador), 'No se pudo reactivar al jugador.', true);
  }

  confirmarAnulacion() {
    if (!this.abonoAnular || !this.motivoValido) {
      this.errorDialogo = `Indicá el motivo de la anulación (entre ${this.motivoMinimo} y ${this.motivoMaximo} caracteres).`;
      return;
    }
    this.ejecutar(
      this.jugadoresService.anularPago(this.abonoAnular.abono.idPago, this.motivoAnulacion.trim()),
      'No se pudo anular el pago.',
      false,
    );
  }

  private abrir(dialogo: DialogoPerfil) {
    if (this.procesando) {
      return;
    }
    this.errorDialogo = '';
    this.dialogo = dialogo;
  }

  private ejecutar(operacion: Observable<{ mensaje: string }>, mensajeError: string, recargarJugador: boolean) {
    if (this.procesando) {
      return;
    }
    this.procesando = true;
    this.errorDialogo = '';
    operacion.subscribe({
      next: (respuesta) => {
        this.procesando = false;
        this.cerrarDialogo();
        this.notifications.notify(respuesta.mensaje, 'success');
        if (recargarJugador) {
          this.cargarJugador();
        }
        this.cargarHistorial();
      },
      error: (err: HttpErrorResponse) => {
        this.procesando = false;
        this.errorDialogo = err.error?.mensaje ?? mensajeError;
        this.notifications.notify(this.errorDialogo, 'error');
        this.cdr.detectChanges();
      },
    });
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

  responsableAbono(nombre?: string | null, apellido?: string | null): string {
    return [nombre, apellido].filter(Boolean).join(' ') || 'Sin registro histórico';
  }

  formatMontoCompacto(valor: number): string {
    return formatCompactCurrency(valor);
  }

  // Texto del badge de beneficio: "Becado ($5.000)" o "Descuento (20%)".
  beneficioTexto(item: PagoHistorialItem): string {
    if (!item.tieneBeneficio) {
      return '';
    }
    const valor =
      item.tipoValorBeneficio === '%'
        ? `${item.porcentajeBeneficio}%`
        : this.formatMonto(item.montoFijoBeneficio ?? 0);
    return `${item.motivoBeneficio} (${valor})`;
  }
}
