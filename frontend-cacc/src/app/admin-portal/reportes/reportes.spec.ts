import '@angular/compiler';
import { TestBed } from '@angular/core/testing';
import { AuthService } from '../../services/auth';
import { ReportesService } from '../../services/reportes';
import { PagosService } from '../../services/pagos';
import { DiscountService } from '../../services/discounts';
import { ArancelesService } from '../../services/aranceles';
import { NotificationService } from '../../shared/notifications/notification.service';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { of } from 'rxjs';
import { Reportes } from './reportes';

describe('Historial de exportaciones', () => {
  function crear(superadmin: boolean) {
    const historial = vi.fn(() => of({items: [], total: 41, pagina: 2, tamanioPagina: 20}));
    const componente = new Reportes(
      {isSuperAdmin: () => superadmin} as never,
      {historial} as never, {} as never, {} as never, {} as never, {} as never,
    );
    return {componente, historial};
  }
  it('no consulta historial para administradores sin rol superadmin', () => {
    const {componente, historial} = crear(false);
    componente.cargarHistorial();
    expect(historial).not.toHaveBeenCalled();
  });
  it('pide una página de 20 y conserva la hora del club sin conversión', () => {
    const {componente, historial} = crear(true);
    componente.cargarHistorial(2);
    expect(historial).toHaveBeenCalledWith(2, 20);
    expect(componente.paginaHistorial).toBe(2);
    expect(componente.nombreReporte('pagos-recientes')).toBe('Cuotas y pagos');
    expect(componente.nombreReporte('recaudacion')).toBe('Recaudación mensual');
    expect(componente.historial()?.total).toBe(41);
    expect(componente.fechaHistorial('2026-10-31T23:58:07')).toBe('31/10/2026 23:58:07');
  });
});


describe('Reportes plantilla Angular', () => {
  afterEach(() => TestBed.resetTestingModule());
  async function render(superadmin: boolean) {
    const aranceles = vi.fn(() => of({arancelMasculinoVigente: 100, arancelFemeninoVigente: 200}));
    const historial = vi.fn(() => of({items: [{id: 1, fechaHora: '2026-10-31T23:58:07', usuarioId: 42, persona: 'Ana Pérez', tipo: 'pagos-recientes', formato: 'csv', filtros: 'Últimos 500', cantidadFilas: 1}], total: 1, pagina: 1, tamanioPagina: 20}));
    await TestBed.configureTestingModule({imports: [Reportes], providers: [
      {provide: AuthService, useValue: {isSuperAdmin: () => superadmin}},
      {provide: ReportesService, useValue: {historial, contar: () => of({tipo: 'deudores', cantidadFilas: 1})}},
      {provide: PagosService, useValue: {getResumen: () => of({deudaGlobalTotal: 100, jugadoresMorosos: 1, recaudadoAnioActual: 200, pagosDelMes: 1})}},
      {provide: DiscountService, useValue: {getAllDiscounts: () => of([])}},
      {provide: ArancelesService, useValue: {getResumen: aranceles}},
      {provide: NotificationService, useValue: {notify: vi.fn()}},
    ]}).compileComponents();
    const fixture = TestBed.createComponent(Reportes);
    fixture.detectChanges();
    await new Promise(resolve => setTimeout(resolve, 230));
    fixture.detectChanges();
    return {fixture, el: fixture.nativeElement as HTMLElement, aranceles, historial};
  }
  it('rol2 renderiza tres tarjetas con estadísticas sin consultar aranceles ni historial', async () => {
    const {el, aranceles, historial} = await render(false);
    expect(el.querySelectorAll('.report-card')).toHaveLength(3);
    expect(el.textContent).not.toContain('Actualización de Aranceles');
    expect(el.textContent).toContain('Deuda total');
    expect(el.textContent).toContain('1 registro a exportar');
    expect(aranceles).not.toHaveBeenCalled();
    expect(historial).not.toHaveBeenCalled();
    expect(el.querySelector('.historial-reportes')).toBeNull();
  });
  it('superadmin renderiza historial con persona, formato, nombre amigable y hora del club', async () => {
    const {el} = await render(true);
    expect(el.querySelectorAll('.report-card')).toHaveLength(4);
    const texto = el.querySelector('.historial-reportes')!.textContent;
    expect(texto).toContain('Ana Pérez');
    expect(texto).toContain('Cuotas y pagos');
    expect(texto).toContain('CSV');
    expect(texto).toContain('31/10/2026 23:58:07');
  });
});
