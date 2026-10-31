import { Component, EventEmitter, Input, OnDestroy, Output } from '@angular/core';

// Lo que espera la búsqueda por texto a que se termine de escribir antes de buscar (HU-097).
export const SEARCH_DEBOUNCE_MS = 300;

// Un filtro puesto, mostrado como etiqueta con una × que lo quita solo a él.
export interface ActiveFilter {
  key: string;
  label: string;
}

// Las pantallas arman la lista de filtros activos con un getter. Si cada lectura devolviera un arreglo nuevo, Angular
// vería cambiar la entrada [filtrosActivos] en cada revisión (NG0100) y cortaría el redibujado. Esto devuelve el
// mismo arreglo mientras las etiquetas no cambien.
export class ActiveFiltersCache {
  private last: ActiveFilter[] = [];

  get(next: ActiveFilter[]): ActiveFilter[] {
    const same = next.length === this.last.length
      && next.every((f, i) => f.key === this.last[i].key && f.label === this.last[i].label);
    if (!same) this.last = next;
    return this.last;
  }
}

// Barra de filtros del Portal Administrativo (HU-097): el mismo comportamiento en todas las pantallas.
//  - Búsqueda por texto (opcional): busca 300 ms después de la última tecla, o al instante con Enter.
//  - Los desplegables los pone cada pantalla adentro (contenido proyectado) y se aplican al instante.
//  - Cada filtro activo es una etiqueta con ×; "Limpiar filtros" está siempre en el mismo lugar, al final de la fila,
//    y queda deshabilitado si no hay nada que limpiar (así no aparece y desaparece moviendo la fila).
//  - Una fila en pantallas anchas; en columna en las angostas.
//
// Uso:
//   <app-filter-bar textoBusqueda="Buscar por nombre o DNI…" [busqueda]="busqueda" (busquedaChange)="..."
//                   [filtrosActivos]="filtrosActivos" (quitarFiltro)="quitarFiltro($event)" (limpiar)="limpiarFiltros()">
//     <app-custom-select ... />                                <!-- en la fila, antes de "Limpiar filtros" -->
//     <div filterBarMore>…</div>                               <!-- debajo de la fila (ej. "Más filtros") -->
//     <span filterBarNote>…</span>                             <!-- junto a las etiquetas (una aclaración) -->
//   </app-filter-bar>
@Component({
  selector: 'app-filter-bar',
  standalone: true,
  templateUrl: './filter-bar.html',
  styleUrl: './filter-bar.css',
})
export class FilterBar implements OnDestroy {
  // Sin texto de búsqueda no hay buscador.
  @Input() textoBusqueda = '';
  @Input() etiqueta = 'Filtros';
  @Input() filtrosActivos: ActiveFilter[] = [];

  // El valor de la búsqueda que tiene la pantalla. Si cambia desde afuera (al limpiar), el buscador lo copia y
  // descarta lo que estaba por buscar.
  @Input()
  set busqueda(valor: string | null | undefined) {
    const texto = valor ?? '';
    if (texto !== this.ultimoEmitido || texto !== this.texto) {
      this.cancelarEspera();
      this.texto = texto;
      this.ultimoEmitido = texto;
    }
  }

  @Output() readonly busquedaChange = new EventEmitter<string>();
  @Output() readonly quitarFiltro = new EventEmitter<string>();
  @Output() readonly limpiar = new EventEmitter<void>();

  // Lo que se ve en el buscador (puede ir adelantado a lo que ya se buscó).
  texto = '';
  private ultimoEmitido = '';
  private espera: ReturnType<typeof setTimeout> | undefined;

  alEscribir(valor: string): void {
    this.texto = valor;
    this.cancelarEspera();
    this.espera = setTimeout(() => this.emitirBusqueda(), SEARCH_DEBOUNCE_MS);
  }

  // Enter busca sin esperar.
  buscarAhora(event: Event): void {
    event.preventDefault();
    this.cancelarEspera();
    this.emitirBusqueda();
  }

  ngOnDestroy(): void {
    this.cancelarEspera();
  }

  private emitirBusqueda(): void {
    this.espera = undefined;
    const valor = this.texto;
    if (valor === this.ultimoEmitido) return;
    this.ultimoEmitido = valor;
    this.busquedaChange.emit(valor);
  }

  private cancelarEspera(): void {
    clearTimeout(this.espera);
    this.espera = undefined;
  }
}
