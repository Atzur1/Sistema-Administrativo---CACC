import '@angular/compiler';
import { TestBed } from '@angular/core/testing';
import { Injector, runInInjectionContext } from '@angular/core';
import { SimpleChange } from '@angular/core';
import { Subject, of } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ReportExportMenu } from './report-export-menu';
import { ReportesService } from '../../services/reportes';

describe('Conteo previo a exportar', () => {
  afterEach(() => { vi.useRealTimers(); });
  it('invalida inmediatamente, ignora respuestas viejas y bloquea cero/error', async () => {
    vi.useFakeTimers();
    const anterior = new Subject<{tipo: string; cantidadFilas: number}>();
    const actual = new Subject<{tipo: string; cantidadFilas: number}>();
    const contar = vi.fn().mockReturnValueOnce(anterior).mockReturnValueOnce(actual);
    const injector = Injector.create({providers: [{provide: ReportesService, useValue: {contar}}]});
    const menu = runInInjectionContext(injector, () => new ReportExportMenu());
    const emitir = vi.spyOn(menu.exportar, 'emit');
    menu.tipo = 'deudores';
    menu.consultar();
    menu.elegir('csv');
    expect(emitir).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(200);
    anterior.next({tipo: 'deudores', cantidadFilas: 5});
    menu.elegir('csv');
    expect(emitir).toHaveBeenCalledTimes(1);
    menu.idCategoria = 8;
    menu.ngOnChanges({idCategoria: new SimpleChange(null, 8, false)});
    expect(menu.cantidad()).toBeNull();
    menu.elegir('csv');
    await vi.advanceTimersByTimeAsync(200);
    anterior.next({tipo: 'deudores', cantidadFilas: 99});
    expect(menu.cantidad()).toBeNull();
    actual.next({tipo: 'deudores', cantidadFilas: 1});
    menu.elegir('pdf');
    expect(emitir).toHaveBeenCalledTimes(2);
    actual.next({tipo: 'deudores', cantidadFilas: 0});
    menu.elegir('pdf');
    expect(emitir).toHaveBeenCalledTimes(2);
    expect(contar).toHaveBeenLastCalledWith('deudores', {idCategoria: 8});
    actual.error(new Error('sin red'));
    expect(menu.error()).toBe(true);
    menu.ngOnDestroy();
  });
  it('usa el mismo límite de 500 que la descarga de pagos', async () => {
    vi.useFakeTimers();
    const contar = vi.fn(() => new Subject());
    const injector = Injector.create({providers: [{provide: ReportesService, useValue: {contar}}]});
    const menu = runInInjectionContext(injector, () => new ReportExportMenu());
    menu.tipo = 'pagos-recientes';
    menu.consultar();
    await vi.advanceTimersByTimeAsync(200);
    expect(contar).toHaveBeenCalledWith('pagos-recientes', {top: 500});
    menu.ngOnDestroy();
  });
});


describe('ReportExportMenu plantilla Angular', () => {
  afterEach(() => TestBed.resetTestingModule());
  it('renderiza cero y bloquea el menú, luego habilita un registro', async () => {
    const contar = vi.fn(() => of({tipo: 'deudores', cantidadFilas: 0}));
    await TestBed.configureTestingModule({imports: [ReportExportMenu], providers: [{provide: ReportesService, useValue: {contar}}]}).compileComponents();
    const fixture = TestBed.createComponent(ReportExportMenu);
    fixture.componentRef.setInput('tipo', 'deudores');
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector<HTMLButtonElement>('.export-menu-trigger')!.disabled).toBe(true);
    await new Promise(resolve => setTimeout(resolve, 230));
    fixture.detectChanges();
    expect(el.textContent).toContain('0 registros. El reporte quedaría vacío con los filtros seleccionados.');
    expect(el.querySelector<HTMLButtonElement>('.export-menu-trigger')!.disabled).toBe(true);
    contar.mockReturnValue(of({tipo: 'deudores', cantidadFilas: 1}));
    fixture.componentRef.setInput('idCategoria', 5);
    fixture.detectChanges();
    expect(el.textContent).toContain('Consultando');
    await new Promise(resolve => setTimeout(resolve, 230));
    fixture.detectChanges();
    expect(el.textContent).toContain('1 registro a exportar');
    expect(el.querySelector<HTMLButtonElement>('.export-menu-trigger')!.disabled).toBe(false);
  });
});
