import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CustomSelect } from './custom-select';

// Nota: el primer detectChanges() de un fixture recién creado refleja bien
// cualquier estado inicial, pero una SEGUNDA pasada sobre un binding que usa
// getters (hasValue/selectedLabel) no repinta el texto en este setup de
// Vitest — por eso create() setea todo antes de esa primera pasada, en vez
// de crear con estado default y mutar después.
async function create(
  setup?: (component: CustomSelect) => void,
): Promise<ComponentFixture<CustomSelect>> {
  await TestBed.configureTestingModule({ imports: [CustomSelect] }).compileComponents();
  const fixture = TestBed.createComponent(CustomSelect);
  setup?.(fixture.componentInstance);
  fixture.detectChanges();
  return fixture;
}

function trigger(fixture: ComponentFixture<CustomSelect>): HTMLButtonElement {
  return fixture.nativeElement.querySelector('.cs-trigger');
}

function optionTexts(fixture: ComponentFixture<CustomSelect>): string[] {
  return Array.from(fixture.nativeElement.querySelectorAll('.cs-option')).map(
    (el) => (el as HTMLElement).querySelector('span')?.textContent?.trim() ?? '',
  );
}

describe('CustomSelect - simple mode (options as plain strings/numbers)', () => {
  it('shows the placeholder until a value is set', async () => {
    const fixture = await create((c) => {
      c.options = ['Enero', 'Febrero'];
      c.placeholder = 'Seleccionar mes';
    });

    expect(trigger(fixture).textContent).toContain('Seleccionar mes');
  });

  it('lists every option by its own text and reports the same text back as the value on selection', async () => {
    let emitted: unknown;
    const fixture = await create((c) => {
      c.options = ['Enero', 'Febrero'];
      c.registerOnChange((v) => (emitted = v));
    });

    trigger(fixture).click();
    fixture.detectChanges();
    expect(optionTexts(fixture)).toEqual(['Enero', 'Febrero']);

    fixture.nativeElement.querySelectorAll('.cs-option')[1].click();
    fixture.detectChanges();

    expect(emitted).toBe('Febrero');
    expect(trigger(fixture).textContent).toContain('Febrero');
  });
});

describe('CustomSelect - {value, label} mode (display text different from the stored value)', () => {
  const options = [
    { value: '%', label: 'Porcentaje' },
    { value: '$', label: 'Monto fijo' },
  ];

  it('shows each label in the menu but reports the underlying value on selection', async () => {
    let emitted: unknown;
    const fixture = await create((c) => {
      c.options = options;
      c.registerOnChange((v) => (emitted = v));
    });

    trigger(fixture).click();
    fixture.detectChanges();
    expect(optionTexts(fixture)).toEqual(['Porcentaje', 'Monto fijo']);

    fixture.nativeElement.querySelectorAll('.cs-option')[0].click();
    fixture.detectChanges();

    // El value que viaja al form es '%', no "Porcentaje" — eso es lo que distingue
    // este modo del simple, donde texto y valor son la misma cosa.
    expect(emitted).toBe('%');
  });

  it('shows the label of the pre-set value (writeValue), not the raw value, on the closed trigger', async () => {
    const fixture = await create((c) => {
      c.options = options;
      c.writeValue('$');
    });

    expect(trigger(fixture).textContent).toContain('Monto fijo');
    expect(trigger(fixture).textContent).not.toContain('$');
  });
});
