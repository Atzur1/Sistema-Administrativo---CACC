import { Component, ElementRef, EventEmitter, HostListener, Input, Output, inject, signal } from '@angular/core';

export type ExportFormat = 'pdf' | 'csv';

// Menú "Exportar Reporte" del Portal Administrativo (HU-097): el mismo botón y las mismas dos opciones en todas las
// pantallas que exportan (Deudas y Morosidad, Recaudación, Reportes). El menú solo avisa qué formato se eligió; el
// archivo lo pide la pantalla, que sabe qué filtros tiene puestos.
//
// Uso:
//   <app-export-menu [enCurso]="exportando" (exportar)="exportarDeudores($event)" />
//
// Se cierra al elegir una opción, al hacer clic afuera y con Esc. Mientras enCurso es true el botón dice
// "Generando…" y no se puede volver a abrir.
@Component({
  selector: 'app-export-menu',
  standalone: true,
  templateUrl: './export-menu.html',
  styleUrl: './export-menu.css',
})
export class ExportMenu {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  @Input() texto = 'Exportar Reporte';
  // Hay una exportación de este menú en curso.
  @Input() enCurso = false;
  // Para bloquearlo mientras otro menú de la misma pantalla exporta (Reportes tiene uno por tarjeta).
  @Input() deshabilitado = false;
  // Más chico, para ir dentro de una tarjeta.
  @Input() compacto = false;

  @Output() readonly exportar = new EventEmitter<ExportFormat>();

  readonly abierto = signal(false);

  readonly opciones: { formato: ExportFormat; texto: string; detalle: string }[] = [
    { formato: 'pdf', texto: 'Exportar a PDF', detalle: 'Planilla imprimible' },
    { formato: 'csv', texto: 'Exportar a CSV', detalle: 'Se abre en Excel' },
  ];

  alternar(): void {
    if (this.enCurso || this.deshabilitado) return;
    this.abierto.update((abierto) => !abierto);
  }

  elegir(formato: ExportFormat): void {
    this.abierto.set(false);
    this.exportar.emit(formato);
  }

  @HostListener('document:click', ['$event'])
  alHacerClicAfuera(event: MouseEvent): void {
    if (this.abierto() && !this.host.nativeElement.contains(event.target as Node)) {
      this.abierto.set(false);
    }
  }

  @HostListener('document:keydown.escape')
  alApretarEsc(): void {
    this.abierto.set(false);
  }
}
