import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { CuotasFaltantesService } from '../../../services/cuotas-faltantes';
import { NotificationService } from '../../../shared/notifications/notification.service';
import { estaVencida, vencimientoDelPeriodo } from '../../../shared/calendario-cuotas';
import { MissingFeeModel, MonthWithMissingFeesModel } from '../../../models/MissingFeeModel';

const NOMBRES_MES = [
  'enero', 'febrero', 'marzo', 'abril', 'mayo', 'junio',
  'julio', 'agosto', 'septiembre', 'octubre', 'noviembre', 'diciembre',
];

const CURRENCY_FULL = new Intl.NumberFormat('es-AR', {
  style: 'currency',
  currency: 'ARS',
  maximumFractionDigits: 0,
});

// Aviso del Resumen General: meses de los últimos 12 que quedaron con cuotas sin generar (por ejemplo,
// porque el sistema estuvo apagado el día 10). Nada se genera solo: "Generar" abre un detalle que explica
// exactamente qué se va a crear, para quiénes y por cuánto, y recién se graba al confirmar.
@Component({
  selector: 'app-aviso-cuotas-faltantes',
  standalone: true,
  templateUrl: './aviso-cuotas-faltantes.html',
  styleUrl: './aviso-cuotas-faltantes.css',
})
export class AvisoCuotasFaltantes implements OnInit {
  meses: MonthWithMissingFeesModel[] = [];

  // La franja arranca resumida (una línea); el listado de meses se despliega a pedido.
  expandido = false;

  // Mes abierto en el popup de confirmación, con su detalle.
  mesElegido: MonthWithMissingFeesModel | null = null;
  detalle: MissingFeeModel[] = [];
  cargandoDetalle = false;
  generando = false;
  errorDialogo: string | null = null;

  constructor(
    private service: CuotasFaltantesService,
    private notifications: NotificationService,
    private cdr: ChangeDetectorRef,
  ) {}

  ngOnInit(): void {
    this.cargarMeses();
  }

  nombreMes(mes: MonthWithMissingFeesModel): string {
    const [anio, numero] = this.partes(mes);
    return `${NOMBRES_MES[numero - 1]} ${anio}`;
  }

  // "Julio 2026" para encabezar cada fila (nombreMes va en minúscula porque se usa dentro de frases).
  tituloMes(mes: MonthWithMissingFeesModel): string {
    const texto = this.nombreMes(mes);
    return texto.charAt(0).toUpperCase() + texto.slice(1);
  }

  get totalCuotas(): number {
    return this.meses.reduce((suma, mes) => suma + mes.cantidad, 0);
  }

  get totalMonto(): number {
    return this.meses.reduce((suma, mes) => suma + mes.montoTotal, 0);
  }

  // Con un solo mes no hay nada que desplegar: "Revisar" abre directo el detalle para confirmar.
  revisar(): void {
    if (this.meses.length === 1) {
      this.abrir(this.meses[0]);
      return;
    }
    this.expandido = !this.expandido;
    this.cdr.detectChanges();
  }

  formatMonto(valor: number): string {
    return CURRENCY_FULL.format(valor);
  }

  // Día en que vencen las cuotas de ese mes (el 10 del mes siguiente) y si ya pasó: en ese caso quedan
  // vencidas en el momento de generarlas y cuentan para el estado deportivo.
  vencimiento(mes: MonthWithMissingFeesModel): string {
    const [anio, numero] = this.partes(mes);
    return vencimientoDelPeriodo(anio, numero - 1).toLocaleDateString('es-AR', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
    });
  }

  quedanVencidas(mes: MonthWithMissingFeesModel): boolean {
    const [anio, numero] = this.partes(mes);
    return estaVencida(vencimientoDelPeriodo(anio, numero - 1));
  }

  abrir(mes: MonthWithMissingFeesModel): void {
    const [anio, numero] = this.partes(mes);
    this.mesElegido = mes;
    this.detalle = [];
    this.errorDialogo = null;
    this.cargandoDetalle = true;
    this.service.getDetalle(anio, numero).subscribe({
      next: (detalle) => {
        this.detalle = detalle;
        this.cargandoDetalle = false;
        this.cdr.detectChanges();
      },
      error: (err: HttpErrorResponse) => {
        this.cargandoDetalle = false;
        this.errorDialogo = err.error?.mensaje ?? 'No se pudo cargar el detalle de las cuotas faltantes.';
        this.cdr.detectChanges();
      },
    });
  }

  cerrar(): void {
    if (this.generando) return;
    this.mesElegido = null;
    this.detalle = [];
    this.errorDialogo = null;
  }

  confirmar(): void {
    const mes = this.mesElegido;
    if (!mes || this.generando || this.cargandoDetalle || this.detalle.length === 0) return;
    const [anio, numero] = this.partes(mes);
    this.generando = true;
    this.errorDialogo = null;
    this.service.generar(anio, numero).subscribe({
      next: (respuesta) => {
        this.generando = false;
        this.notifications.notify(respuesta.mensaje, 'success');
        this.cerrar();
        this.cargarMeses();
      },
      error: (err: HttpErrorResponse) => {
        this.generando = false;
        this.errorDialogo = err.error?.mensaje ?? 'No se pudieron generar las cuotas. No se creó ninguna.';
        this.cdr.detectChanges();
      },
    });
  }

  private cargarMeses(): void {
    this.service.getMeses().subscribe({
      next: (meses) => {
        this.meses = meses;
        this.cdr.detectChanges();
      },
      // Si el aviso no se puede cargar, el resto del Resumen General sigue funcionando.
      error: () => {
        this.meses = [];
        this.cdr.detectChanges();
      },
    });
  }

  private partes(mes: MonthWithMissingFeesModel): [number, number] {
    const [anio, numero] = mes.periodo.slice(0, 7).split('-').map(Number);
    return [anio, numero];
  }
}
