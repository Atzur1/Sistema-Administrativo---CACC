import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { HistorialPagosResultado, JugadoresService, PagoHistorialItem } from '../../services/jugadores';
import { JugadorResumen } from '../../services/pagos';
import { formatCompactCurrency } from '../../shared/format-currency';

const CURRENCY_FULL = new Intl.NumberFormat('es-AR', {
  style: 'currency',
  currency: 'ARS',
  maximumFractionDigits: 0,
});

@Component({
  selector: 'app-jugador-perfil',
  standalone: true,
  imports: [CommonModule],
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
    private cdr: ChangeDetectorRef
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

  volver() {
    this.router.navigate(['/admin/portal/cuotas-pagos']);
  }

  formatFecha(iso: string): string {
    return new Date(iso).toLocaleDateString('es-AR', { day: '2-digit', month: '2-digit', year: 'numeric' });
  }

  formatMonto(valor: number): string {
    return CURRENCY_FULL.format(valor);
  }

  formatMontoCompacto(valor: number): string {
    return formatCompactCurrency(valor);
  }

  // Texto del badge de beneficio: "Media Beca ($5.000)" o "Descuento por Hermanos (20%)".
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
