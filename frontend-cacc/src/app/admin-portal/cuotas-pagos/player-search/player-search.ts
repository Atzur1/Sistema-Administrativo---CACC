import { Component, DestroyRef, EventEmitter, OnInit, Output, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, Subject, catchError, debounceTime, map, merge, of, switchMap } from 'rxjs';
import { PlayerSearchService } from '../../../services/player-search';
import { PlayerSearchResultModel } from '../../../models/PlayerSearchResultModel';
import { NotificationService } from '../../../shared/notifications/notification.service';
import { financialLoadErrorMessage } from '../../../shared/http-error-message';

// Resultado de una búsqueda: la lista (vacía = sin coincidencias), o null si falló.
type SearchOutcome = { term: string; results: PlayerSearchResultModel[] | null };

// HU-023: buscador de alumnos por DNI o apellido, arriba del panel de Cuotas.
// Busca en el servidor mientras se escribe (con debounce) o al apretar Enter/Buscar;
// al elegir un alumno avisa al panel, que abre su deuda y su historial de cuotas.
@Component({
  selector: 'app-player-search',
  standalone: true,
  templateUrl: './player-search.html',
  styleUrl: './player-search.css',
})
export class PlayerSearch implements OnInit {
  static readonly DEBOUNCE_MS = 350;
  readonly minLength = PlayerSearchService.MIN_TERM_LENGTH;
  readonly maxResults = PlayerSearchService.MAX_RESULTS;

  @Output() selected = new EventEmitter<PlayerSearchResultModel>();

  readonly term = signal('');
  readonly loading = signal(false);
  readonly failed = signal(false);
  // null = todavía no se buscó nada (o el término es muy corto): no hay grilla ni mensaje.
  readonly results = signal<PlayerSearchResultModel[] | null>(null);

  private readonly typed$ = new Subject<string>();
  private readonly submitted$ = new Subject<string>();

  private readonly searchService = inject(PlayerSearchService);
  private readonly notifications = inject(NotificationService);
  private readonly destroyRef = inject(DestroyRef);

  ngOnInit() {
    // switchMap: si llega un término nuevo, la respuesta de la búsqueda anterior se descarta
    // y nunca pisa a la más reciente.
    merge(this.typed$.pipe(debounceTime(PlayerSearch.DEBOUNCE_MS)), this.submitted$)
      .pipe(
        map((term) => term.trim()),
        switchMap((term) => this.runSearch(term)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((outcome) => {
        this.loading.set(false);
        this.failed.set(outcome !== null && outcome.results === null);
        this.results.set(outcome?.results ?? null);
      });
  }

  onInput(value: string) {
    this.term.set(value);
    if (value.trim().length < this.minLength) {
      // Limpia al instante: no tiene sentido esperar el debounce para vaciar la grilla.
      this.results.set(null);
      this.failed.set(false);
    }
    this.typed$.next(value);
  }

  submit() {
    this.submitted$.next(this.term());
  }

  clear() {
    this.onInput('');
  }

  select(player: PlayerSearchResultModel) {
    this.selected.emit(player);
  }

  get tooShort(): boolean {
    const length = this.term().trim().length;
    return length > 0 && length < this.minLength;
  }

  private runSearch(term: string): Observable<SearchOutcome | null> {
    if (term.length < this.minLength) {
      return of(null);
    }

    this.loading.set(true);
    return this.searchService.search(term).pipe(
      map((results): SearchOutcome => ({ term, results })),
      catchError((error: unknown) => {
        this.notifications.notify(
          financialLoadErrorMessage(error, 'No se pudo realizar la búsqueda de alumnos. Intentá de nuevo.'),
          'error',
        );
        return of<SearchOutcome>({ term, results: null });
      }),
    );
  }
}
