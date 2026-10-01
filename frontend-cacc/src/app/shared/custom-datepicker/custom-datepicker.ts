import {
  Component,
  ElementRef,
  HostListener,
  Input,
  NgZone,
  OnDestroy,
  ViewChild,
  forwardRef,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { FloatingPanel } from '../floating-panel';

const MONTHS = [
  'Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio',
  'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre',
];
const MONTHS_SHORT = ['Ene', 'Feb', 'Mar', 'Abr', 'May', 'Jun', 'Jul', 'Ago', 'Sep', 'Oct', 'Nov', 'Dic'];
// La semana empieza el lunes, como en el resto del club.
const WEEKDAYS = ['Lu', 'Ma', 'Mi', 'Ju', 'Vi', 'Sá', 'Do'];

interface DayCell {
  iso: string;
  day: number;
  inMonth: boolean;
  today: boolean;
  selected: boolean;
  disabled: boolean;
}

const pad = (n: number, width = 2): string => String(n).padStart(width, '0');

// Las fechas viajan como texto ISO 'aaaa-mm-dd' (el mismo formato que devolvía <input type="date">),
// así este componente reemplaza al input nativo sin tocar la lógica de cada pantalla.
function toIso(year: number, month: number, day: number): string {
  return `${pad(year, 4)}-${pad(month)}-${pad(day)}`;
}

function parseIso(iso: string): { year: number; month: number; day: number } | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso);
  if (!match) {
    return null;
  }
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  // Rechaza fechas que no existen (31/02, 29/02 en año no bisiesto…).
  const date = new Date(year, month - 1, day);
  if (date.getFullYear() !== year || date.getMonth() !== month - 1 || date.getDate() !== day) {
    return null;
  }
  return { year, month, day };
}

function formatDisplay(iso: string): string {
  const parts = parseIso(iso);
  return parts ? `${pad(parts.day)}/${pad(parts.month)}/${pad(parts.year, 4)}` : '';
}

function todayIso(): string {
  const now = new Date();
  return toIso(now.getFullYear(), now.getMonth() + 1, now.getDate());
}

// Reemplaza al <input type="date"> nativo: el calendario que abre el navegador lo dibuja el
// sistema y no se puede estilizar. Este es un ControlValueAccessor, así que funciona con
// formControlName / ngModel igual que el input, y su estilo acompaña al de app-custom-select.
// Se puede escribir la fecha (dd/mm/aaaa) o elegirla en el calendario.
@Component({
  selector: 'app-custom-datepicker',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './custom-datepicker.html',
  styleUrl: './custom-datepicker.css',
  // El "id" recibido se reenvía al <input> interno para que <label for="..."> lo siga enfocando.
  host: { '[attr.id]': 'null' },
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => CustomDatepicker),
      multi: true,
    },
  ],
})
export class CustomDatepicker implements ControlValueAccessor, OnDestroy {
  @Input() id: string | null = null;
  @Input() placeholder = 'dd/mm/aaaa';
  // Rango permitido, en ISO ('aaaa-mm-dd'). Vacío = sin límite.
  @Input() min = '';
  @Input() max = '';
  // Marca el campo en rojo (ej. rango de fechas inválido en Becados).
  @Input() invalid = false;

  readonly monthsShort = MONTHS_SHORT;
  readonly weekdays = WEEKDAYS;

  value = ''; // ISO, o '' si no hay fecha válida
  text = ''; // lo que se ve en el campo (dd/mm/aaaa, o a medio escribir)
  isOpen = false;
  disabled = false;

  // 'days': grilla del mes. 'months': elegir mes y año.
  view: 'days' | 'months' = 'days';
  viewYear = new Date().getFullYear();
  viewMonth = new Date().getMonth() + 1; // 1-12

  private onChange: (value: string) => void = () => {};
  private onTouched: () => void = () => {};

  @ViewChild('field') private fieldRef?: ElementRef<HTMLElement>;

  // El calendario se muestra en <body>, pegado al campo (ver FloatingPanel), para que ningún
  // panel de la pantalla lo tape o recorte.
  private readonly panel: FloatingPanel;

  @ViewChild('panel')
  set panelRef(ref: ElementRef<HTMLElement> | undefined) {
    if (ref) {
      this.panel.attach(ref.nativeElement);
    } else {
      this.panel.detach();
    }
  }

  private cellsKey = '';
  private cellsCache: DayCell[] = [];

  constructor(
    private elementRef: ElementRef<HTMLElement>,
    private ngZone: NgZone,
  ) {
    this.panel = new FloatingPanel(ngZone, () => this.fieldRef?.nativeElement, { matchAnchorWidth: false });
  }

  ngOnDestroy(): void {
    this.panel.detach();
  }

  // ===== ControlValueAccessor =====

  writeValue(value: string | null): void {
    const iso = (value ?? '').toString().slice(0, 10);
    this.value = parseIso(iso) ? iso : '';
    this.text = formatDisplay(this.value);
  }

  registerOnChange(fn: (value: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled = isDisabled;
  }

  // ===== Escritura a mano =====

  // Da formato dd/mm/aaaa mientras se escribe y solo informa una fecha cuando está completa,
  // existe y entra en el rango; si no, informa vacío (igual que el input nativo).
  onTextInput(input: HTMLInputElement): void {
    const digits = input.value.replace(/\D/g, '').slice(0, 8);
    let formatted = digits;
    if (digits.length > 4) {
      formatted = `${digits.slice(0, 2)}/${digits.slice(2, 4)}/${digits.slice(4)}`;
    } else if (digits.length > 2) {
      formatted = `${digits.slice(0, 2)}/${digits.slice(2)}`;
    }
    input.value = formatted;
    this.text = formatted;

    let next = '';
    if (digits.length === 8) {
      const iso = toIso(Number(digits.slice(4)), Number(digits.slice(2, 4)), Number(digits.slice(0, 2)));
      if (parseIso(iso) && this.inRange(iso)) {
        next = iso;
      }
    }

    if (next !== this.value) {
      this.value = next;
      this.onChange(next);
    }
    if (next) {
      this.syncViewTo(next);
    }
  }

  onBlur(): void {
    this.onTouched();
  }

  onInputKeydown(event: KeyboardEvent): void {
    if (event.key === 'ArrowDown' && !this.isOpen) {
      event.preventDefault();
      this.open();
    }
  }

  // ===== Calendario =====

  toggle(): void {
    if (this.disabled) {
      return;
    }
    this.ngZone.run(() => (this.isOpen ? this.close() : this.open()));
  }

  private open(): void {
    this.syncViewTo(this.value || todayIso());
    this.view = 'days';
    this.isOpen = true;
  }

  private close(): void {
    if (this.isOpen) {
      this.isOpen = false;
      this.onTouched();
    }
  }

  private syncViewTo(iso: string): void {
    const parts = parseIso(iso);
    if (parts) {
      this.viewYear = parts.year;
      this.viewMonth = parts.month;
    }
  }

  get title(): string {
    return this.view === 'days' ? `${MONTHS[this.viewMonth - 1]} ${this.viewYear}` : String(this.viewYear);
  }

  toggleView(): void {
    this.view = this.view === 'days' ? 'months' : 'days';
  }

  previous(): void {
    if (this.view === 'months') {
      this.viewYear--;
    } else if (this.viewMonth === 1) {
      this.viewMonth = 12;
      this.viewYear--;
    } else {
      this.viewMonth--;
    }
  }

  next(): void {
    if (this.view === 'months') {
      this.viewYear++;
    } else if (this.viewMonth === 12) {
      this.viewMonth = 1;
      this.viewYear++;
    } else {
      this.viewMonth++;
    }
  }

  pickMonth(month: number): void {
    this.viewMonth = month;
    this.view = 'days';
  }

  isCurrentMonth(month: number): boolean {
    const today = new Date();
    return today.getFullYear() === this.viewYear && today.getMonth() + 1 === month;
  }

  isSelectedMonth(month: number): boolean {
    const parts = parseIso(this.value);
    return !!parts && parts.year === this.viewYear && parts.month === month;
  }

  // 42 celdas (6 semanas), con los días de los meses vecinos atenuados. Se recalcula solo si
  // cambia el mes mostrado, la fecha elegida o el rango.
  get cells(): DayCell[] {
    const key = `${this.viewYear}-${this.viewMonth}|${this.value}|${this.min}|${this.max}`;
    if (key === this.cellsKey) {
      return this.cellsCache;
    }

    const first = new Date(this.viewYear, this.viewMonth - 1, 1);
    const offset = (first.getDay() + 6) % 7; // lunes = 0
    const today = todayIso();
    const cells: DayCell[] = [];
    for (let i = 0; i < 42; i++) {
      const date = new Date(this.viewYear, this.viewMonth - 1, 1 - offset + i);
      const iso = toIso(date.getFullYear(), date.getMonth() + 1, date.getDate());
      cells.push({
        iso,
        day: date.getDate(),
        inMonth: date.getMonth() === this.viewMonth - 1,
        today: iso === today,
        selected: iso === this.value,
        disabled: !this.inRange(iso),
      });
    }

    this.cellsKey = key;
    this.cellsCache = cells;
    return cells;
  }

  get todayDisabled(): boolean {
    return !this.inRange(todayIso());
  }

  selectDay(cell: DayCell): void {
    if (cell.disabled) {
      return;
    }
    this.commit(cell.iso);
    this.close();
  }

  selectToday(): void {
    if (this.todayDisabled) {
      return;
    }
    this.commit(todayIso());
    this.close();
  }

  clear(): void {
    this.commit('');
    this.close();
  }

  private commit(iso: string): void {
    this.value = iso;
    this.text = formatDisplay(iso);
    this.onChange(iso);
    this.onTouched();
  }

  private inRange(iso: string): boolean {
    return (!this.min || iso >= this.min) && (!this.max || iso <= this.max);
  }

  // Cierra el calendario si el click fue afuera del campo y del propio calendario (que vive en <body>).
  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    const target = event.target as Node;
    const inside = this.elementRef.nativeElement.contains(target) || this.panel.contains(target);
    if (this.isOpen && !inside) {
      this.ngZone.run(() => this.close());
    }
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.isOpen) {
      this.ngZone.run(() => (this.isOpen = false));
    }
  }
}
