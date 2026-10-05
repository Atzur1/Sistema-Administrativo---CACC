import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable, Subject, of, throwError } from 'rxjs';

import { PlayerSearch } from './player-search';
import { PlayerSearchService } from '../../../services/player-search';
import { PlayerSearchResultModel } from '../../../models/PlayerSearchResultModel';
import { NotificationService } from '../../../shared/notifications/notification.service';

// HU-023: búsqueda de alumnos por DNI o apellido en el panel de Cuotas.

const BAZAN: PlayerSearchResultModel = {
  id: 10,
  firstName: 'NICOLÁS FERNANDO',
  lastName: 'BAZÁN',
  dni: '47970803',
  categoryName: 'AFA 20067',
};

describe('PlayerSearch (HU-023)', () => {
  let fixture: ComponentFixture<PlayerSearch>;
  let search: ReturnType<typeof vi.fn<(term: string) => Observable<PlayerSearchResultModel[]>>>;
  let notify: ReturnType<typeof vi.fn>;

  async function setup(response: (term: string) => Observable<PlayerSearchResultModel[]>) {
    vi.useFakeTimers();
    search = vi.fn(response);
    notify = vi.fn();

    await TestBed.configureTestingModule({
      imports: [PlayerSearch],
      providers: [
        { provide: PlayerSearchService, useValue: { search } },
        { provide: NotificationService, useValue: { notify } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(PlayerSearch);
    fixture.detectChanges();
  }

  afterEach(() => vi.useRealTimers());

  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => el().textContent ?? '';

  function type(value: string) {
    const input = el().querySelector('input') as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function waitDebounce() {
    vi.advanceTimersByTime(PlayerSearch.DEBOUNCE_MS);
    fixture.detectChanges();
  }

  it('waits for the debounce before asking the server', async () => {
    await setup(() => of([BAZAN]));

    type('baz');
    vi.advanceTimersByTime(PlayerSearch.DEBOUNCE_MS - 1);
    expect(search).not.toHaveBeenCalled();

    waitDebounce();
    expect(search).toHaveBeenCalledExactlyOnceWith('baz');
  });

  it('only sends the last term when the user keeps typing', async () => {
    await setup(() => of([BAZAN]));

    type('b');
    type('ba');
    type('baz');
    waitDebounce();

    expect(search).toHaveBeenCalledExactlyOnceWith('baz');
  });

  it('does not search with fewer than 2 characters and says why', async () => {
    await setup(() => of([BAZAN]));

    type('b');
    waitDebounce();

    expect(search).not.toHaveBeenCalled();
    expect(text()).toContain('Ingresá al menos 2 caracteres');
  });

  it('renders Nombre, Apellido, DNI and Categoría for each match', async () => {
    await setup(() => of([BAZAN]));

    type('bazan');
    waitDebounce();

    const headers = [...el().querySelectorAll('th')].map((th) => th.textContent?.trim());
    expect(headers.slice(0, 4)).toEqual(['Nombre', 'Apellido', 'DNI', 'Categoría']);
    const cells = [...el().querySelectorAll('tbody td')].map((td) => td.textContent?.trim());
    expect(cells.slice(0, 4)).toEqual(['NICOLÁS FERNANDO', 'BAZÁN', '47970803', 'AFA 20067']);
  });

  it('shows the no-results message when nothing matches', async () => {
    await setup(() => of([]));

    type('zzzz');
    waitDebounce();

    expect(el().querySelector('.search-empty')?.textContent?.trim())
      .toBe('No se encontraron alumnos con ese criterio de búsqueda');
    expect(el().querySelector('table')).toBeNull();
  });

  it('shows a spinner while the request is in flight', async () => {
    const pending = new Subject<PlayerSearchResultModel[]>();
    await setup(() => pending);

    type('bazan');
    waitDebounce();

    expect(el().querySelector('.search-spinner')).not.toBeNull();
    expect(text()).toContain('Buscando alumnos…');

    pending.next([BAZAN]);
    pending.complete();
    fixture.detectChanges();

    expect(el().querySelector('.search-spinner')).toBeNull();
  });

  it('searches right away on Enter, without waiting for the debounce', async () => {
    await setup(() => of([BAZAN]));

    type('47970803');
    el().querySelector('form')!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    expect(search).toHaveBeenCalledWith('47970803');
    expect(text()).toContain('BAZÁN');
  });

  it('on a connection error shows a toast in Spanish and offers to retry', async () => {
    let fails = true;
    await setup(() => (fails ? throwError(() => new HttpErrorResponse({ status: 0 })) : of([BAZAN])));

    type('bazan');
    waitDebounce();

    expect(notify).toHaveBeenCalledExactlyOnceWith(
      'No se pudo conectar con el servidor. Verificá tu conexión e intentá de nuevo.',
      'error',
    );
    expect(text()).toContain('No se pudo completar la búsqueda');
    expect(text()).not.toContain('No se encontraron alumnos');

    fails = false;
    (el().querySelector('.search-retry') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(search).toHaveBeenCalledTimes(2);
    expect(text()).toContain('BAZÁN');
  });

  it('a server error uses the search-specific message', async () => {
    await setup(() => throwError(() => new HttpErrorResponse({ status: 500 })));

    type('bazan');
    waitDebounce();

    expect(notify).toHaveBeenCalledWith('No se pudo realizar la búsqueda de alumnos. Intentá de nuevo.', 'error');
  });

  it('a slow older response never overwrites the newer one', async () => {
    const first = new Subject<PlayerSearchResultModel[]>();
    await setup((term) => (term === 'ba' ? first : of([BAZAN])));

    type('ba');
    waitDebounce();
    type('bazan');
    waitDebounce();
    first.next([{ ...BAZAN, id: 99, lastName: 'BARRIOS' }]);
    fixture.detectChanges();

    expect(text()).toContain('BAZÁN');
    expect(text()).not.toContain('BARRIOS');
  });

  it('"Ver cuotas" emits the chosen player', async () => {
    await setup(() => of([BAZAN]));
    const chosen: PlayerSearchResultModel[] = [];
    fixture.componentInstance.selected.subscribe((p) => chosen.push(p));

    type('bazan');
    waitDebounce();
    (el().querySelector('.select-btn') as HTMLButtonElement).click();

    expect(chosen).toEqual([BAZAN]);
  });

  it('clearing the field hides the previous results', async () => {
    await setup(() => of([BAZAN]));

    type('bazan');
    waitDebounce();
    (el().querySelector('.search-clear') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(el().querySelector('table')).toBeNull();
    expect(text()).not.toContain('No se encontraron');
  });
});
