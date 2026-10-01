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

// El menú se mueve a <body> al abrirse (para que ningún panel lo tape), así que las
// opciones se buscan en el documento y no dentro del fixture.
function optionTexts(_fixture: ComponentFixture<CustomSelect>): string[] {
  return Array.from(document.body.querySelectorAll('.cs-option')).map(
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

    (document.body.querySelectorAll('.cs-option')[1] as HTMLElement).click();
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

    (document.body.querySelectorAll('.cs-option')[0] as HTMLElement).click();
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

describe('CustomSelect - menu placement', () => {
  it('renders the open menu in <body>, outside the component, and removes it on close', async () => {
    const fixture = await create((c) => {
      c.options = ['Enero', 'Febrero'];
    });

    trigger(fixture).click();
    fixture.detectChanges();

    const menu = document.body.querySelector('.cs-menu') as HTMLElement;
    expect(menu).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.cs-menu')).toBeNull();
    expect(menu.parentElement).toBe(document.body);
    expect(menu.style.position === '' || menu.style.position === 'fixed').toBe(true);

    (document.body.querySelectorAll('.cs-option')[0] as HTMLElement).click();
    fixture.detectChanges();

    expect(document.body.querySelector('.cs-menu')).toBeNull();
  });

  it('removes the menu from <body> if the component is destroyed while open', async () => {
    const fixture = await create((c) => {
      c.options = ['Enero'];
    });

    trigger(fixture).click();
    fixture.detectChanges();
    expect(document.body.querySelector('.cs-menu')).not.toBeNull();

    fixture.destroy();

    expect(document.body.querySelector('.cs-menu')).toBeNull();
  });
});
