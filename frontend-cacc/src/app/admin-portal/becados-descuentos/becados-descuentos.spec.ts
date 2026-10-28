import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';

import { BecadosDescuentos } from './becados-descuentos';
import { DiscountService } from '../../services/discounts';
import { PagosService } from '../../services/pagos';
import { AuthService } from '../../services/auth';
import { DiscountModel, FirstPeriodModel } from '../../models/DiscountModel';

// El formulario de bonificación se completa por cuota ("desde la cuota de" / "hasta la cuota de"), y la primera
// cuota que se ofrece la decide el servidor: la próxima que no se emitió, o la del mes en curso si está sin pagos.
const JUGADOR = { id: 1, fullName: 'PRUEBA, NICOLAS', document: '99000001', category: 'AFA 20067', leaveDate: null };

async function crear(first: FirstPeriodModel, existentes: DiscountModel[] = []) {
  await TestBed.configureTestingModule({
    imports: [BecadosDescuentos],
    providers: [
      {
        provide: DiscountService,
        useValue: {
          getDiscountMap: () => of(new Map<number, DiscountModel>()),
          getAllDiscounts: () => of([]),
          getDiscountTypes: () => of(['Becado', 'Descuento']),
          getDiscountsByPlayer: () => of(existentes),
          getFirstPeriod: () => of(first),
        },
      },
      { provide: PagosService, useValue: { getJugadores: () => of([]) } },
      { provide: AuthService, useValue: { isSuperAdmin: () => true } },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(BecadosDescuentos);
  fixture.detectChanges();
  const component = fixture.componentInstance;
  component.selectedPlayer = JUGADOR;
  component.openDialogForSelectedPlayer();
  fixture.detectChanges();
  return { fixture, component };
}

describe('BecadosDescuentos - formulario por cuota', () => {
  it('ofrece desde la próxima cuota que se emite y la deja elegida', async () => {
    const { component } = await crear({ period: '2026-11-01', issueDate: '2026-11-10', alreadyIssued: false });

    const opciones = component.startOptions();
    expect(opciones[0]).toEqual({ value: '2026-11-01', label: 'Noviembre 2026 · se emite el 10/11' });
    expect(opciones[1].value).toBe('2026-12-01');
    expect(component.benefitForm.get('startDate')!.value).toBe('2026-11-01');
  });

  it('si la cuota del mes en curso ya está emitida y sin pagos, es la primera que se ofrece', async () => {
    const { component } = await crear({ period: '2026-10-01', issueDate: '2026-10-10', alreadyIssued: true });

    expect(component.startOptions()[0]).toEqual({ value: '2026-10-01', label: 'Octubre 2026 · ya emitida' });
    expect(component.firstPeriodNote).toContain('ya está emitida y nadie pagó nada');
  });

  it('nunca ofrece una cuota anterior a la primera permitida', async () => {
    const { component } = await crear({ period: '2026-11-01', issueDate: '2026-11-10', alreadyIssued: false });

    const valores = component.startOptions().map((o) => o.value);
    expect(valores).not.toContain('2026-10-01');
    expect(valores).not.toContain('2026-09-01');
  });

  it('el fin por defecto es la cuota de diciembre y las finales posibles arrancan en la inicial', async () => {
    const { component } = await crear({ period: '2026-11-01', issueDate: '2026-11-10', alreadyIssued: false });

    expect(component.benefitForm.get('endDate')!.value).toBe('2026-12-31');
    expect(component.endOptions()[0]).toEqual({ value: '2026-11-30', label: 'Noviembre 2026' });
  });

  it('las fechas viajan como mes completo: primer día de la inicial y último día de la final', async () => {
    const { component } = await crear({ period: '2026-11-01', issueDate: '2026-11-10', alreadyIssued: false });

    const form = component.benefitForm.getRawValue();
    expect(form.startDate.endsWith('-01')).toBe(true);
    expect(form.endDate).toBe('2026-12-31');
  });

  it('si se mueve el inicio después de la final, la final pasa a ser la misma cuota', async () => {
    const { component } = await crear({ period: '2026-11-01', issueDate: '2026-11-10', alreadyIssued: false });

    component.benefitForm.get('startDate')!.setValue('2027-02-01');

    expect(component.benefitForm.get('endDate')!.value).toBe('2027-02-28');
    expect(component.dateRangeErrorMessage).toBe('');
  });
});

// HU-101: la tabla se separa en Vigentes (activas y programadas) e Historial (finalizadas, canceladas y anuladas).
function beneficio(id: number, status: DiscountModel['status'], playerName: string, type = 'Becado'): DiscountModel {
  return {
    id,
    playerId: id,
    playerName,
    category: 'AFA 2010',
    type,
    valueType: '%',
    percentage: 50,
    fixedAmount: null,
    startDate: '2026-01-01',
    endDate: '2026-12-31',
    status,
    isActive: status === 'Active',
  };
}

const BENEFICIOS: DiscountModel[] = [
  beneficio(1, 'Active', 'GOMEZ, ANA'),
  beneficio(2, 'Scheduled', 'PEREZ, LUIS', 'Descuento'),
  beneficio(3, 'Expired', 'GOMEZ, JUAN'),
  beneficio(4, 'Cancelled', 'DIAZ, SOL', 'Descuento'),
  beneficio(5, 'Voided', 'RUIZ, MARTA'),
];

async function crearConTabla(rows: DiscountModel[] = BENEFICIOS) {
  await TestBed.configureTestingModule({
    imports: [BecadosDescuentos],
    providers: [
      {
        provide: DiscountService,
        useValue: {
          getDiscountMap: () => of(new Map<number, DiscountModel>()),
          getAllDiscounts: () => of(rows),
          getDiscountTypes: () => of(['Becado', 'Descuento']),
        },
      },
      { provide: PagosService, useValue: { getJugadores: () => of([]) } },
      { provide: AuthService, useValue: { isSuperAdmin: () => true } },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(BecadosDescuentos);
  fixture.detectChanges();
  return { fixture, component: fixture.componentInstance };
}

const ids = (rows: DiscountModel[]) => rows.map((row) => row.id);

describe('BecadosDescuentos - pestañas Vigentes / Historial', () => {
  it('arranca en Vigentes y muestra solo las activas y programadas', async () => {
    const { component } = await crearConTabla();

    expect(component.pestana()).toBe('vigentes');
    expect(ids(component.filteredRows)).toEqual([1, 2]);
  });

  it('Historial muestra las finalizadas, canceladas y anuladas', async () => {
    const { component } = await crearConTabla();

    component.cambiarPestana('historial');

    expect(ids(component.filteredRows)).toEqual([3, 4, 5]);
  });

  it('cada pestaña muestra cuántos beneficios tiene', async () => {
    const { fixture } = await crearConTabla();

    const textos = Array.from(fixture.nativeElement.querySelectorAll('[role="tab"]') as NodeListOf<HTMLElement>).map((tab) =>
      tab.textContent!.replace(/\s+/g, ' ').trim(),
    );
    expect(textos).toEqual(['Vigentes 2', 'Historial 3']);
  });

  it('la búsqueda y el motivo se mantienen al cambiar de pestaña', async () => {
    const { component } = await crearConTabla();

    component.filterForm.patchValue({ busqueda: 'gomez', motivo: 'Becado' });
    expect(ids(component.filteredRows)).toEqual([1]);

    component.cambiarPestana('historial');
    expect(component.filterForm.value.busqueda).toBe('gomez');
    expect(ids(component.filteredRows)).toEqual([3]);
  });

  it('el filtro de estado ofrece solo los estados de la pestaña y se limpia si no existe en la nueva', async () => {
    const { component } = await crearConTabla();

    expect(component.estadoOptionsDePestana.map((o) => o.value)).toEqual(['Active', 'Scheduled']);
    component.filterForm.patchValue({ estado: 'Active' });

    component.cambiarPestana('historial');

    expect(component.estadoOptionsDePestana.map((o) => o.value)).toEqual(['Expired', 'Cancelled', 'Voided']);
    expect(component.filterForm.value.estado).toBeNull();
  });

  it('al cambiar de pestaña vuelve a la página 1', async () => {
    const muchos = Array.from({ length: 25 }, (_, i) => beneficio(i + 1, 'Active', `JUGADOR, ${i + 1}`));
    const { component } = await crearConTabla(muchos);
    component.irAPagina(3);

    component.cambiarPestana('historial');
    component.cambiarPestana('vigentes');

    expect(component.page).toBe(1);
  });

  it('los cinco estados se muestran con la etiqueta compartida, cada uno con su propio tono', async () => {
    const { component } = await crearConTabla();

    const tonos = BENEFICIOS.map((row) => component.statusClassOf(row));
    expect(new Set(tonos).size).toBe(5);
    expect(BENEFICIOS.map((row) => component.statusOf(row))).toEqual(['Activa', 'Programada', 'Finalizada', 'Cancelada', 'Anulada']);
  });
});
