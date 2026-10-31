import { Component, Input, Output, EventEmitter, OnChanges, OnDestroy, SimpleChanges, signal, inject } from '@angular/core';
import { Subscription, timer, switchMap } from 'rxjs';
import { ReportesService, ReportType } from '../../services/reportes';
import { ExportMenu, ExportFormat } from './export-menu';

/** El conteo y la descarga comparten exactamente los filtros del reporte. */
@Component({
  selector: 'app-report-export-menu', standalone: true, imports: [ExportMenu],
  styles: [':host { display: block; min-width: 0; max-width: 100%; } p { max-width: 24rem; font-size: .8rem; overflow-wrap: anywhere; }'],
  template: `
    <app-export-menu [compacto]="compacto" [enCurso]="enCurso"
      [deshabilitado]="deshabilitado || cantidad() === null || cantidad() === 0"
      (exportar)="elegir($event)" />
    <p role="status" aria-live="polite">{{ error() ? 'No se pudo consultar la cantidad de filas.' : cantidad() === null ? 'Consultando filas…' : cantidad() === 0 ? '0 registros. El reporte quedaría vacío con los filtros seleccionados.' : cantidad() === 1 ? '1 registro a exportar' : cantidad() + ' registros a exportar' }}</p>
    @if (error()) { <button type="button" (click)="consultar()">Reintentar</button> }
  `,
})
export class ReportExportMenu implements OnChanges, OnDestroy {
  private service = inject(ReportesService);
  @Input({required: true}) tipo!: ReportType;
  @Input() idCategoria: number | null = null;
  @Input() anio: number | null = null;
  @Input() mes: number | null = null;
  @Input() enCurso = false;
  @Input() deshabilitado = false;
  @Input() compacto = false;
  @Output() exportar = new EventEmitter<ExportFormat>();
  cantidad = signal<number | null>(null);
  error = signal(false);
  private consulta?: Subscription;
  ngOnChanges(changes: SimpleChanges): void {
    if (['tipo', 'idCategoria', 'anio', 'mes'].some(key => changes[key])) this.consultar();
  }
  consultar(): void {
    this.consulta?.unsubscribe();
    this.cantidad.set(null);
    this.error.set(false);
    const params: Record<string, number> = {};
    if (this.idCategoria !== null) params['idCategoria'] = this.idCategoria;
    if (this.anio !== null) params['anio'] = this.anio;
    if (this.mes !== null) params['mes'] = this.mes;
    if (this.tipo === 'pagos-recientes') params['top'] = 500;
    this.consulta = timer(200).pipe(switchMap(() => this.service.contar(this.tipo, params))).subscribe({
      next: result => this.cantidad.set(result.cantidadFilas),
      error: () => this.error.set(true),
    });
  }
  elegir(formato: ExportFormat): void {
    if (!this.enCurso && !this.deshabilitado && (this.cantidad() ?? 0) > 0) this.exportar.emit(formato);
  }
  ngOnDestroy(): void { this.consulta?.unsubscribe(); }
}
