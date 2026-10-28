import { ChangeDetectorRef, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { ActiveFilter, ActiveFiltersCache, FilterBar, SEARCH_DEBOUNCE_MS } from './filter-bar';

// HU-097: la barra de filtros compartida.
@Component({
  standalone: true,
  imports: [FilterBar],
  template: `
    <app-filter-bar [textoBusqueda]="textoBusqueda" [busqueda]="busqueda" (busquedaChange)="buscadas.push($event); busqueda = $event"
                    [filtrosActivos]="filtros" (quitarFiltro)="quitados.push($event)" (limpiar)="limpiezas = limpiezas + 1">
      <select class="proyectado"><option>Categoría</option></select>
      <div class="mas" filterBarMore>Más filtros</div>
      <span class="nota" filterBarNote>Aclaración</span>
    </app-filter-bar>
  `,
})
class Host {
  textoBusqueda = 'Buscar por nombre';
  busqueda = '';
  filtros: ActiveFilter[] = [];
  buscadas: string[] = [];
  quitados: string[] = [];
  limpiezas = 0;
}

async function crear() {
  await TestBed.configureTestingModule({ imports: [Host] }).compileComponents();
  const fixture = TestBed.createComponent(Host);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const refrescar = () => {
    fixture.componentRef.injector.get(ChangeDetectorRef).markForCheck();
    fixture.detectChanges();
  };
  return { fixture, host: fixture.componentInstance, el, refrescar };
}

const escribir = (el: HTMLElement, texto: string) => {
  const input = el.querySelector<HTMLInputElement>('.filter-bar-search input')!;
  input.value = texto;
  input.dispatchEvent(new Event('input'));
  return input;
};

describe('FilterBar - búsqueda con espera', () => {
  afterEach(() => vi.useRealTimers());

  it('waits 300 ms after the last key before searching, and searches once', async () => {
    const { host, el } = await crear();
    vi.useFakeTimers();

    escribir(el, 'a');
    escribir(el, 'an');
    escribir(el, 'ana');
    vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS - 1);
    expect(host.buscadas).toEqual([]);

    vi.advanceTimersByTime(1);
    expect(host.buscadas).toEqual(['ana']);
    expect(SEARCH_DEBOUNCE_MS).toBe(300);
  });

  it('Enter searches right away', async () => {
    const { host, el } = await crear();
    vi.useFakeTimers();

    const input = escribir(el, 'juan');
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));

    expect(host.buscadas).toEqual(['juan']);
    vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS);
    expect(host.buscadas).toEqual(['juan']);
  });

  it('when the screen clears the search, the box empties and the pending search is dropped', async () => {
    const { host, el, refrescar } = await crear();
    vi.useFakeTimers();
    host.busqueda = 'ana';
    refrescar();

    escribir(el, 'anab');
    host.busqueda = '';
    refrescar();
    vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS);

    expect(el.querySelector<HTMLInputElement>('.filter-bar-search input')!.value).toBe('');
    expect(host.buscadas).toEqual([]);
  });

  it('without a search text there is no search box', async () => {
    const { host, el, refrescar } = await crear();
    host.textoBusqueda = '';
    refrescar();

    expect(el.querySelector('.filter-bar-search')).toBeNull();
  });
});

describe('FilterBar - etiquetas y Limpiar filtros', () => {
  it('shows each active filter as a chip whose × removes only that one', async () => {
    const { host, el, refrescar } = await crear();
    host.filtros = [{ key: 'categoria', label: 'Categoría: AFA' }, { key: 'estado', label: 'Estado: Inhabilitado' }];
    refrescar();

    const chips = Array.from(el.querySelectorAll('.filter-bar-chip'));
    expect(chips.map((c) => c.textContent!.replace('×', '').trim())).toEqual(['Categoría: AFA', 'Estado: Inhabilitado']);
    const quitar = chips[1].querySelector('button')!;
    expect(quitar.getAttribute('aria-label')).toBe('Quitar filtro: Estado: Inhabilitado');

    quitar.click();
    expect(host.quitados).toEqual(['estado']);
  });

  it('"Limpiar filtros" is always there with that exact text, disabled when nothing is filtered', async () => {
    const { host, el, refrescar } = await crear();
    const limpiar = () => el.querySelector<HTMLButtonElement>('.filter-bar-clear')!;

    expect(limpiar().textContent!.trim()).toBe('Limpiar filtros');
    expect(limpiar().disabled).toBe(true);

    host.filtros = [{ key: 'categoria', label: 'Categoría: AFA' }];
    refrescar();
    expect(limpiar().disabled).toBe(false);
    limpiar().click();
    expect(host.limpiezas).toBe(1);
  });

  it('"Limpiar filtros" sits at the end of the row, after the screen own controls', async () => {
    const { el } = await crear();

    const fila = el.querySelector('.filter-bar-row')!;
    expect(fila.lastElementChild!.classList).toContain('filter-bar-clear');
    expect(fila.querySelector('.proyectado')).not.toBeNull();
  });

  it('places the extra panel below the row and the note next to the chips', async () => {
    const { el } = await crear();

    expect(el.querySelector('.filter-bar-row .mas')).toBeNull();
    expect(el.querySelector('.filter-bar > .mas')).not.toBeNull();
    expect(el.querySelector('.filter-bar-status .nota')).not.toBeNull();
  });
});

describe('ActiveFiltersCache', () => {
  it('returns the same array while the chips do not change', () => {
    const cache = new ActiveFiltersCache();
    const primera = cache.get([{ key: 'a', label: 'A' }]);

    expect(cache.get([{ key: 'a', label: 'A' }])).toBe(primera);
    expect(cache.get([{ key: 'a', label: 'A2' }])).not.toBe(primera);
    expect(cache.get([])).toEqual([]);
  });
});
