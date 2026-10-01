import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CustomDatepicker } from './custom-datepicker';

async function create(setup?: (component: CustomDatepicker) => void): Promise<ComponentFixture<CustomDatepicker>> {
  await TestBed.configureTestingModule({ imports: [CustomDatepicker] }).compileComponents();
  const fixture = TestBed.createComponent(CustomDatepicker);
  setup?.(fixture.componentInstance);
  fixture.detectChanges();
  return fixture;
}

function input(fixture: ComponentFixture<CustomDatepicker>): HTMLInputElement {
  return fixture.nativeElement.querySelector('.dp-input');
}

function toggle(fixture: ComponentFixture<CustomDatepicker>): HTMLButtonElement {
  return fixture.nativeElement.querySelector('.dp-toggle');
}

// El calendario se mueve a <body> al abrirse (para que ningún panel lo tape), así que sus
// elementos se buscan en el documento y no dentro del fixture.
function panel(): HTMLElement | null {
  return document.body.querySelector('.dp-panel');
}

function type(fixture: ComponentFixture<CustomDatepicker>, text: string): void {
  const el = input(fixture);
  el.value = text;
  el.dispatchEvent(new Event('input'));
  fixture.detectChanges();
}

function dayButtons(): HTMLButtonElement[] {
  return Array.from(document.body.querySelectorAll('.dp-day')) as HTMLButtonElement[];
}

function dayButton(text: string, inMonth = true): HTMLButtonElement {
  const found = dayButtons().find(
    (b) => b.textContent?.trim() === text && b.classList.contains('dp-outside') !== inMonth,
  );
  expect(found).toBeDefined();
  return found as HTMLButtonElement;
}

describe('CustomDatepicker - texto del campo', () => {
  it('shows the placeholder until a value is set', async () => {
    const fixture = await create();

    expect(input(fixture).placeholder).toBe('dd/mm/aaaa');
    expect(input(fixture).value).toBe('');
  });

  it('shows an ISO value as dd/mm/aaaa', async () => {
    const fixture = await create((c) => c.writeValue('2026-09-30'));

    expect(input(fixture).value).toBe('30/09/2026');
  });

  it('ignores a value that is not a real date', async () => {
    const fixture = await create((c) => c.writeValue('2026-02-31'));

    expect(input(fixture).value).toBe('');
  });
});

describe('CustomDatepicker - escribir la fecha', () => {
  it('formats while typing and reports the ISO date once it is complete', async () => {
    let emitted: unknown;
    const fixture = await create((c) => c.registerOnChange((v) => (emitted = v)));

    type(fixture, '30092026');

    expect(input(fixture).value).toBe('30/09/2026');
    expect(emitted).toBe('2026-09-30');
  });

  it('reports an empty value while the date is incomplete or does not exist', async () => {
    const emitted: unknown[] = [];
    const fixture = await create((c) => c.registerOnChange((v) => emitted.push(v)));

    type(fixture, '30092026');
    type(fixture, '3009202'); // borró un dígito: ya no es una fecha completa
    type(fixture, '31022026'); // 31 de febrero no existe

    expect(emitted).toEqual(['2026-09-30', '']);
  });

  it('refuses a typed date outside min/max', async () => {
    const emitted: unknown[] = [];
    const fixture = await create((c) => {
      c.min = '2026-10-01';
      c.registerOnChange((v) => emitted.push(v));
    });

    type(fixture, '30092026');

    expect(emitted).toEqual([]);
  });
});

describe('CustomDatepicker - calendario', () => {
  it('opens in <body>, outside the component, on the month of the current value', async () => {
    const fixture = await create((c) => c.writeValue('2026-09-30'));

    toggle(fixture).click();
    fixture.detectChanges();

    const element = panel();
    expect(element).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.dp-panel')).toBeNull();
    expect(element!.parentElement).toBe(document.body);
    expect(element!.querySelector('.dp-title')?.textContent).toContain('Septiembre 2026');
    expect(dayButtons()).toHaveLength(42);
  });

  it('starts the week on Monday', async () => {
    const fixture = await create((c) => c.writeValue('2026-09-30'));

    toggle(fixture).click();
    fixture.detectChanges();

    const weekdays = Array.from(document.body.querySelectorAll('.dp-weekday')).map((e) => e.textContent?.trim());
    expect(weekdays).toEqual(['Lu', 'Ma', 'Mi', 'Ju', 'Vi', 'Sá', 'Do']);
    // 1/9/2026 es martes: la primera celda es el lunes 31/8, atenuado por ser del mes anterior.
    expect(dayButtons()[0].textContent?.trim()).toBe('31');
    expect(dayButtons()[0].classList.contains('dp-outside')).toBe(true);
  });

  it('picks a day, reports the ISO date, shows it and closes', async () => {
    let emitted: unknown;
    const fixture = await create((c) => {
      c.writeValue('2026-09-30');
      c.registerOnChange((v) => (emitted = v));
    });

    toggle(fixture).click();
    fixture.detectChanges();
    dayButton('15').click();
    fixture.detectChanges();

    expect(emitted).toBe('2026-09-15');
    expect(input(fixture).value).toBe('15/09/2026');
    expect(panel()).toBeNull();
  });

  it('navigates between months and years', async () => {
    const fixture = await create((c) => c.writeValue('2026-12-10'));

    toggle(fixture).click();
    fixture.detectChanges();

    (document.body.querySelectorAll('.dp-nav')[1] as HTMLButtonElement).click(); // siguiente
    fixture.detectChanges();
    expect(panel()!.querySelector('.dp-title')?.textContent).toContain('Enero 2027');

    (document.body.querySelector('.dp-title') as HTMLButtonElement).click(); // vista de meses
    fixture.detectChanges();
    (Array.from(document.body.querySelectorAll('.dp-month')).find((e) => e.textContent?.trim() === 'Mar') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(panel()!.querySelector('.dp-title')?.textContent).toContain('Marzo 2027');
  });

  it('disables the days outside min/max', async () => {
    const fixture = await create((c) => {
      c.writeValue('2026-09-15');
      c.min = '2026-09-10';
      c.max = '2026-09-20';
    });

    toggle(fixture).click();
    fixture.detectChanges();

    expect(dayButton('9').disabled).toBe(true);
    expect(dayButton('10').disabled).toBe(false);
    expect(dayButton('20').disabled).toBe(false);
    expect(dayButton('21').disabled).toBe(true);
  });

  it('clears the value with "Borrar"', async () => {
    let emitted: unknown = 'sin cambios';
    const fixture = await create((c) => {
      c.writeValue('2026-09-30');
      c.registerOnChange((v) => (emitted = v));
    });

    toggle(fixture).click();
    fixture.detectChanges();
    (document.body.querySelector('.dp-link-muted') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(emitted).toBe('');
    expect(input(fixture).value).toBe('');
    expect(panel()).toBeNull();
  });

  it('removes the calendar from <body> if the component is destroyed while open', async () => {
    const fixture = await create();

    toggle(fixture).click();
    fixture.detectChanges();
    expect(panel()).not.toBeNull();

    fixture.destroy();

    expect(panel()).toBeNull();
  });

  it('does not open when disabled', async () => {
    const fixture = await create((c) => c.setDisabledState(true));

    toggle(fixture).click();
    fixture.detectChanges();

    expect(panel()).toBeNull();
  });
});
