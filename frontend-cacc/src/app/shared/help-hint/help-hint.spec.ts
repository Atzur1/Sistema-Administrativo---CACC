import { ComponentFixture, TestBed } from '@angular/core/testing';

import { HelpHint } from './help-hint';

async function create(text = 'Explica la acción', label = 'Anular'): Promise<ComponentFixture<HelpHint>> {
  await TestBed.configureTestingModule({ imports: [HelpHint] }).compileComponents();
  const fixture = TestBed.createComponent(HelpHint);
  fixture.componentInstance.text = text;
  fixture.componentInstance.label = label;
  fixture.detectChanges();
  return fixture;
}

function trigger(fixture: ComponentFixture<HelpHint>): HTMLButtonElement {
  return fixture.nativeElement.querySelector('.help-trigger');
}

// La ayuda se mueve a <body> al abrirse (igual que los desplegables), así que se busca en el documento.
function tooltip(): HTMLElement | null {
  return document.body.querySelector('.help-tooltip');
}

describe('HelpHint', () => {
  afterEach(() => {
    vi.useRealTimers();
    TestBed.resetTestingModule();
  });

  it('starts closed and names the action for screen readers', async () => {
    const fixture = await create();

    expect(tooltip()).toBeNull();
    expect(trigger(fixture).getAttribute('aria-label')).toBe('Ayuda: Anular');
    expect(trigger(fixture).getAttribute('aria-expanded')).toBe('false');
  });

  it('opens after resting the mouse on it, and closes when the mouse leaves', async () => {
    vi.useFakeTimers();
    const fixture = await create();

    trigger(fixture).dispatchEvent(new MouseEvent('mouseenter'));
    fixture.detectChanges();
    expect(tooltip()).toBeNull();

    vi.advanceTimersByTime(300);
    fixture.detectChanges();
    expect(tooltip()?.textContent).toContain('Explica la acción');

    trigger(fixture).dispatchEvent(new MouseEvent('mouseleave'));
    fixture.detectChanges();
    expect(tooltip()).toBeNull();
  });

  it('does not open when the mouse only passes over it', async () => {
    vi.useFakeTimers();
    const fixture = await create();

    trigger(fixture).dispatchEvent(new MouseEvent('mouseenter'));
    vi.advanceTimersByTime(100);
    trigger(fixture).dispatchEvent(new MouseEvent('mouseleave'));
    vi.advanceTimersByTime(500);
    fixture.detectChanges();

    expect(tooltip()).toBeNull();
  });

  it('toggles on click (touch screens) without the click reaching the parent', async () => {
    const fixture = await create();
    const parentClick = vi.fn();
    fixture.nativeElement.addEventListener('click', parentClick);

    trigger(fixture).click();
    fixture.detectChanges();
    expect(tooltip()).not.toBeNull();
    expect(parentClick).not.toHaveBeenCalled();

    trigger(fixture).click();
    fixture.detectChanges();
    expect(tooltip()).toBeNull();
  });

  it('closes with Escape', async () => {
    const fixture = await create();

    trigger(fixture).click();
    fixture.detectChanges();
    trigger(fixture).dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();

    expect(tooltip()).toBeNull();
  });

  it('closes when clicking anywhere else', async () => {
    const fixture = await create();

    trigger(fixture).click();
    fixture.detectChanges();
    document.body.click();
    fixture.detectChanges();

    expect(tooltip()).toBeNull();
  });
});
