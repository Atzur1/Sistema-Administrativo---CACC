import { ChangeDetectorRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { UNDO_WINDOW_MS, UndoNotices, UndoNoticesService } from './undo-notices';

// HU-105: avisos con "Deshacer" para acciones reversibles. Aparte del toast (que se reemplaza con el próximo) y de la
// campanita: cada operación tiene su propio aviso, que dura 5000 ms desde que el servidor confirmó la acción.

describe('UndoNoticesService', () => {
  let avisos: UndoNoticesService;

  beforeEach(() => {
    vi.useFakeTimers();
    avisos = TestBed.inject(UndoNoticesService);
  });

  afterEach(() => {
    avisos.clear();
    vi.useRealTimers();
  });

  const ofrecer = (message = 'Ana Pérez ahora es SuperAdmin.') => {
    const onUndo = vi.fn();
    const onExpire = vi.fn();
    const id = avisos.offer({ message, undoLabel: 'Deshacer el cambio de rol de Ana Pérez', onUndo, onExpire });
    return { id, onUndo, onExpire };
  };

  it('el aviso dura exactamente 5000 ms y al vencer lo informa', () => {
    const { onExpire } = ofrecer();

    vi.advanceTimersByTime(UNDO_WINDOW_MS - 1);
    expect(avisos.notices()).toHaveLength(1);
    expect(onExpire).not.toHaveBeenCalled();

    vi.advanceTimersByTime(1);
    expect(avisos.notices()).toEqual([]);
    expect(onExpire).toHaveBeenCalledTimes(1);
    expect(UNDO_WINDOW_MS).toBe(5000);
  });

  it('deshacer frena el plazo, lo pide una sola vez y el aviso queda "deshaciendo" hasta que se resuelva', () => {
    const { id, onUndo, onExpire } = ofrecer();

    avisos.undo(id);
    avisos.undo(id);
    vi.advanceTimersByTime(UNDO_WINDOW_MS * 2);

    expect(onUndo).toHaveBeenCalledTimes(1);
    expect(onExpire).not.toHaveBeenCalled();
    expect(avisos.notices()).toEqual([expect.objectContaining({ id, state: 'undoing' })]);

    avisos.dismiss(id);
    expect(avisos.notices()).toEqual([]);
  });

  it('cada operación tiene su aviso y su plazo', () => {
    const primero = ofrecer('Ana Pérez ahora es SuperAdmin.');
    vi.advanceTimersByTime(3000);
    const segundo = ofrecer('Luis Gómez ahora es Administrador.');

    expect(primero.id).not.toBe(segundo.id);
    expect(avisos.notices().map((n) => n.message)).toEqual(['Ana Pérez ahora es SuperAdmin.', 'Luis Gómez ahora es Administrador.']);

    vi.advanceTimersByTime(2000);
    expect(avisos.notices().map((n) => n.id)).toEqual([segundo.id]);
    expect(segundo.onExpire).not.toHaveBeenCalled();

    vi.advanceTimersByTime(3000);
    expect(avisos.notices()).toEqual([]);
  });

  it('al cerrar sesión se borran todos, sin que venza ni se deshaga ninguno después', () => {
    const { id, onUndo, onExpire } = ofrecer();

    avisos.clear();
    vi.advanceTimersByTime(UNDO_WINDOW_MS);
    avisos.undo(id);

    expect(avisos.notices()).toEqual([]);
    expect(onExpire).not.toHaveBeenCalled();
    expect(onUndo).not.toHaveBeenCalled();
  });
});

describe('UndoNotices (componente)', () => {
  afterEach(() => {
    TestBed.inject(UndoNoticesService).clear();
    vi.useRealTimers();
  });

  async function crear() {
    vi.useFakeTimers();
    await TestBed.configureTestingModule({ imports: [UndoNotices] }).compileComponents();
    const fixture = TestBed.createComponent(UndoNotices);
    fixture.detectChanges();
    const refrescar = () => {
      fixture.componentRef.injector.get(ChangeDetectorRef).markForCheck();
      fixture.detectChanges();
    };
    return { fixture, el: fixture.nativeElement as HTMLElement, refrescar, avisos: TestBed.inject(UndoNoticesService) };
  }

  it('la región de avisos existe siempre y se anuncia sin interrumpir', async () => {
    const { el } = await crear();

    const region = el.querySelector('.undo-notices')!;
    expect(region.getAttribute('aria-live')).toBe('polite');
    expect(region.children).toHaveLength(0);
  });

  it('muestra el mensaje y un botón "Deshacer" cuyo nombre dice qué deshace', async () => {
    const { el, avisos, refrescar } = await crear();
    const onUndo = vi.fn();
    avisos.offer({ message: 'Ana Pérez ahora es SuperAdmin.', undoLabel: 'Deshacer el cambio de rol de Ana Pérez', onUndo, onExpire: vi.fn() });
    refrescar();

    expect(el.querySelector('.undo-notice-message')!.textContent!.trim()).toBe('Ana Pérez ahora es SuperAdmin.');
    const boton = el.querySelector<HTMLButtonElement>('.undo-notice-action')!;
    expect(boton.textContent!.trim()).toBe('Deshacer');
    expect(boton.getAttribute('aria-label')).toBe('Deshacer el cambio de rol de Ana Pérez');

    boton.click();
    refrescar();

    expect(onUndo).toHaveBeenCalledTimes(1);
    expect(boton.textContent!.trim()).toBe('Deshaciendo…');
    expect(boton.getAttribute('aria-disabled')).toBe('true');
  });

  it('al vencer el plazo el aviso desaparece', async () => {
    const { el, avisos, refrescar } = await crear();
    avisos.offer({ message: 'Ana Pérez ahora es SuperAdmin.', undoLabel: 'Deshacer', onUndo: vi.fn(), onExpire: vi.fn() });
    refrescar();
    expect(el.querySelectorAll('.undo-notice')).toHaveLength(1);

    vi.advanceTimersByTime(UNDO_WINDOW_MS);
    refrescar();

    expect(el.querySelectorAll('.undo-notice')).toHaveLength(0);
  });
});
