import { FormBuilder } from '@angular/forms';
import { of } from 'rxjs';

import { ActualizacionAranceles } from './actualizacion-aranceles';
import { EnrollmentFeeModel } from '../../models/EnrollmentFeeModel';

// Inscripción por género en "Actualización de aranceles". Se prueba la lógica de la pantalla (formulario y mapeo) sin
// dibujarla: el gráfico de cuotas necesita un canvas que el entorno de pruebas no tiene.
function crear(fees: EnrollmentFeeModel[] = [], historial: unknown[] = []) {
  const createEnrollmentFee = vi.fn(() => of({} as EnrollmentFeeModel));
  const notify = vi.fn();
  const component = new ActualizacionAranceles(
    new FormBuilder(),
    {
      getResumen: () => of({}),
      getHistorial: () => of(historial),
      programar: vi.fn(() => of({ mensaje: '' })),
    } as never,
    { getAllEnrollmentFees: () => of(fees), createEnrollmentFee } as never,
    { getCategorias: () => of([]) } as never,
    { detectChanges: () => {}, markForCheck: () => {} } as never,
    { notify } as never,
  );
  return { component, createEnrollmentFee, notify };
}

const cargar = (component: ActualizacionAranceles, valores: { target: string; amount: string; validFrom?: string }) =>
  component.feeForm.setValue({ validFrom: '2099-01-01', ...valores });

describe('ActualizacionAranceles - inscripción por género', () => {
  it('ofrece la inscripción masculina y la femenina como destinos', () => {
    const { component } = crear();

    const destinos = component.targets.map((t) => [t.value, t.label]);
    expect(destinos).toContainEqual(['inscripcion:Masculino', 'Inscripción masculina']);
    expect(destinos).toContainEqual(['inscripcion:Femenino', 'Inscripción femenina']);
  });

  it('la inscripción admite $0 (ese género no paga), pero la cuota no', () => {
    const { component } = crear();

    cargar(component, { target: 'inscripcion:Femenino', amount: '0' });
    expect(component.feeForm.valid).toBe(true);

    cargar(component, { target: 'genero:Femenino', amount: '0' });
    expect(component.feeForm.get('amount')?.hasError('min')).toBe(true);
    expect(component.feeForm.valid).toBe(false);
  });

  it('al cambiar de destino se revalida el monto', () => {
    const { component } = crear();
    cargar(component, { target: 'inscripcion:Masculino', amount: '0' });
    expect(component.feeForm.valid).toBe(true);

    component.feeForm.patchValue({ target: 'categoria:3' });

    expect(component.feeForm.get('amount')?.hasError('min')).toBe(true);
  });

  it('programa la inscripción femenina mandando el género', () => {
    const { component, createEnrollmentFee, notify } = crear();
    cargar(component, { target: 'inscripcion:Femenino', amount: '30000' });

    component.onSubmit();

    expect(createEnrollmentFee).toHaveBeenCalledWith({ gender: 'Femenino', amount: 30000, startDate: '2099-01-01' });
    expect(notify).toHaveBeenCalledWith('Nuevo valor de la inscripción femenina programado correctamente.', 'success');
  });

  it('con $0 avisa que desde esa fecha no se cobra', () => {
    const { component, createEnrollmentFee, notify } = crear();
    cargar(component, { target: 'inscripcion:Femenino', amount: '0' });

    component.onSubmit();

    expect(createEnrollmentFee).toHaveBeenCalledWith({ gender: 'Femenino', amount: 0, startDate: '2099-01-01' });
    expect(notify).toHaveBeenCalledWith('Desde esa fecha no se cobra la inscripción femenina.', 'success');
  });

  it('la inscripción masculina sigue yendo como antes, con su género', () => {
    const { component, createEnrollmentFee } = crear();
    cargar(component, { target: 'inscripcion:Masculino', amount: '70000' });

    component.onSubmit();

    expect(createEnrollmentFee).toHaveBeenCalledWith({ gender: 'Masculino', amount: 70000, startDate: '2099-01-01' });
  });

  it('explica en el subtítulo que hoy las mujeres no pagan inscripción', () => {
    const { component } = crear();
    component.feeForm.patchValue({ target: 'inscripcion:Femenino' });

    expect(component.formSubtitle).toContain('Hoy las mujeres no pagan inscripción');
  });

  it('el historial muestra cada inscripción con su género y "No se cobra" cuando el monto es $0', () => {
    const { component } = crear([
      { id: 1, gender: 'Masculino', amount: 70000, startDate: '2026-09-28', endDate: null, status: 'Current' },
      { id: 2, gender: 'Femenino', amount: 0, startDate: '2026-01-01', endDate: null, status: 'Current' },
    ]);

    component.ngOnInit();

    const filas = component.feeRows.map((f) => [f.categoryLabel, f.amount]);
    expect(filas).toContainEqual(['Inscripción masculina', '$70.000']);
    expect(filas).toContainEqual(['Inscripción femenina', 'No se cobra']);
  });
});

describe('ActualizacionAranceles - cuota mensual e inscripción en pestañas separadas', () => {
  const cuotaMasculina = {
    idArancel: 10, genero: 'Masculino', idCategoria: null, nombreCategoria: null, monto: 85000,
    vigenteDesde: '2026-01-01', vigenteHasta: null, estado: 'Vigente', puedeCancelar: false,
  };
  const inscripciones: EnrollmentFeeModel[] = [
    { id: 1, gender: 'Masculino', amount: 70000, startDate: '2026-09-28', endDate: null, status: 'Current' },
    { id: 2, gender: 'Femenino', amount: 0, startDate: '2026-01-01', endDate: null, status: 'Current' },
    { id: 3, gender: 'Femenino', amount: 30000, startDate: '2099-01-01', endDate: null, status: 'Scheduled' },
  ];

  it('arranca en la cuota mensual', () => {
    const { component } = crear();

    expect(component.vista).toBe('cuota');
  });

  it('la cuota ofrece género y categoría, y la inscripción solo el género', () => {
    const { component } = crear();
    component.targets = [...component.targets, { value: 'categoria:3', label: 'Categoría AFA' }];

    expect(component.targetsVista.map((t) => t.value)).toEqual(['genero:Masculino', 'genero:Femenino', 'categoria:3']);

    component.setVista('inscripcion');

    expect(component.targetsVista.map((t) => t.value)).toEqual(['inscripcion:Masculino', 'inscripcion:Femenino']);
  });

  it('cada pestaña muestra solo su historial: la cuota no se mezcla con la inscripción', () => {
    const { component } = crear(inscripciones, [cuotaMasculina]);
    component.ngOnInit();

    expect(component.filasVista.map((f) => f.category)).toEqual(['male']);

    component.setVista('inscripcion');

    expect(component.filasVista.length).toBe(3);
    expect(component.filasVista.every((f) => f.category === 'enrollment')).toBe(true);
  });

  it('al cambiar de pestaña se limpia lo que había en el formulario', () => {
    const { component } = crear();
    cargar(component, { target: 'genero:Masculino', amount: '85000' });
    component.montoDisplay = '85.000';

    component.setVista('inscripcion');

    expect(component.feeForm.value).toEqual({ target: '', amount: '', validFrom: '' });
    expect(component.montoDisplay).toBe('');
  });

  it('la inscripción tiene sus propias métricas: lo vigente por género y el próximo cambio', () => {
    const { component } = crear(inscripciones);
    component.ngOnInit();
    component.setVista('inscripcion');

    const metricas = Object.fromEntries(component.metricasVista.map((m) => [m.label, m.value]));
    expect(metricas['Inscripción masculina']).toBe('$70.000');
    expect(metricas['Inscripción femenina']).toBe('No se cobra');
    expect(metricas['Próximo cambio']).toBe('01/01/2099');
  });

  it('la pestaña de inscripción explica qué es antes de elegir un género', () => {
    const { component } = crear();
    component.setVista('inscripcion');

    expect(component.formSubtitle).toContain('una vez por año');
  });

  it('desde la pestaña de inscripción se programa la inscripción, no una cuota', () => {
    const { component, createEnrollmentFee } = crear();
    component.setVista('inscripcion');
    cargar(component, { target: 'inscripcion:Femenino', amount: '30000' });

    component.onSubmit();

    expect(createEnrollmentFee).toHaveBeenCalledWith({ gender: 'Femenino', amount: 30000, startDate: '2099-01-01' });
  });
});

// HU-069: increase by percentage or by a fixed amount over the current tariff. The API keeps receiving the final
// absolute amount, so every case checks what reaches arancelesService.programar.
describe('ActualizacionAranceles - HU-069 increase modes', () => {
  const tariff = (id: number, target: { genero?: string; idCategoria?: number }, monto: number, estado = 'Vigente') => ({
    idArancel: id, genero: target.genero ?? null, idCategoria: target.idCategoria ?? null,
    nombreCategoria: target.idCategoria ? 'AFA' : null, monto,
    vigenteDesde: '2026-01-01', vigenteHasta: null, estado, puedeCancelar: false,
  });
  const history = [
    tariff(10, { genero: 'Masculino' }, 85000),
    tariff(11, { genero: 'Masculino' }, 70000, 'Anterior'),
    tariff(12, { genero: 'Femenino' }, 61999),
    tariff(13, { idCategoria: 3 }, 90000),
  ];

  function setup() {
    const programar = vi.fn(() => of({ mensaje: '' }));
    const component = new ActualizacionAranceles(
      new FormBuilder(),
      { getResumen: () => of({}), getHistorial: () => of(history), programar } as never,
      { getAllEnrollmentFees: () => of([]), createEnrollmentFee: vi.fn() } as never,
      { getCategorias: () => of([{ idCategoria: 3, nombre: 'AFA' }, { idCategoria: 4, nombre: 'Escuelita' }]) } as never,
      { detectChanges: () => {}, markForCheck: () => {} } as never,
      { notify: vi.fn() } as never,
    );
    component.ngOnInit();
    return { component, programar };
  }

  const fill = (component: ActualizacionAranceles, target: string, amount: string) =>
    component.feeForm.setValue({ target, amount, validFrom: '2099-01-01' });

  it('starts in absolute mode and offers the three modes', () => {
    const { component } = setup();
    expect(component.increaseMode).toBe('absolute');
    expect(component.increaseModes.map((m) => m.label)).toEqual(['Monto absoluto', 'Porcentaje (+%)', 'Monto fijo a sumar (+$)']);
  });

  it('looks up the current tariff of the chosen destination (only the "Vigente" one)', () => {
    const { component } = setup();
    expect(component.arancelVigente('genero:Masculino')).toBe(85000);
    expect(component.arancelVigente('categoria:3')).toBe(90000);
    expect(component.arancelVigente('categoria:4')).toBeNull();
  });

  describe('absolute mode', () => {
    it('sends the typed amount', () => {
      const { component, programar } = setup();
      fill(component, 'genero:Masculino', '92000');

      expect(component.resultingAmount).toBe(92000);
      component.onSubmit();
      expect(programar).toHaveBeenCalledWith({ genero: 'Masculino' }, 92000, '2099-01-01');
    });

    it('works on a destination without a current tariff (its first fee)', () => {
      const { component, programar } = setup();
      fill(component, 'categoria:4', '50000');

      expect(component.feeForm.valid).toBe(true);
      expect(component.amountError).toBeNull();
      component.onSubmit();
      expect(programar).toHaveBeenCalledWith({ idCategoria: 4 }, 50000, '2099-01-01');
    });
  });

  describe('percentage mode', () => {
    it('$85.000 + 12,5% sends $95.625', () => {
      const { component, programar } = setup();
      component.setIncreaseMode('percentage');
      fill(component, 'genero:Masculino', '12.5');

      expect(component.resultingAmount).toBe(95625);
      component.onSubmit();
      expect(programar).toHaveBeenCalledWith({ genero: 'Masculino' }, 95625, '2099-01-01');
    });

    it('rounds to the nearest whole peso before sending', () => {
      const { component, programar } = setup();
      component.setIncreaseMode('percentage');
      // 61.999 * 1,075 = 66.648,925 -> 66.649
      fill(component, 'genero:Femenino', '7.5');

      component.onSubmit();
      expect(programar).toHaveBeenCalledWith({ genero: 'Femenino' }, 66649, '2099-01-01');
    });

    it('recalculates when the destination changes', () => {
      const { component } = setup();
      component.setIncreaseMode('percentage');
      fill(component, 'genero:Masculino', '10');
      expect(component.resultingAmount).toBe(93500);

      component.feeForm.patchValue({ target: 'categoria:3' });
      expect(component.resultingAmount).toBe(99000);
    });

    it('accepts "12,5" typed with a comma', () => {
      const { component } = setup();
      component.setIncreaseMode('percentage');
      component.feeForm.patchValue({ target: 'genero:Masculino', validFrom: '2099-01-01' });
      const input = { value: '12,5' } as HTMLInputElement;

      component.onAmountInput(input);

      expect(input.value).toBe('12,5');
      expect(component.feeForm.get('amount')?.value).toBe('12.5');
      expect(component.resultingAmount).toBe(95625);
    });
  });

  describe('fixed amount mode', () => {
    it('$85.000 + $10.000 sends $95.000', () => {
      const { component, programar } = setup();
      component.setIncreaseMode('fixed');
      fill(component, 'genero:Masculino', '10000');

      expect(component.resultingAmount).toBe(95000);
      component.onSubmit();
      expect(programar).toHaveBeenCalledWith({ genero: 'Masculino' }, 95000, '2099-01-01');
    });
  });

  describe('preview', () => {
    it('shows the current tariff and the one that will be scheduled', () => {
      const { component } = setup();
      component.setIncreaseMode('percentage');
      fill(component, 'genero:Masculino', '12.5');

      expect(component.tariffPreview).toEqual({ current: '$85.000', next: '$95.625', difference: '+$10.625' });
    });

    it('is hidden until there is a destination and a valid result', () => {
      const { component } = setup();
      component.setIncreaseMode('percentage');
      expect(component.tariffPreview).toBeNull();

      fill(component, 'categoria:4', '10');
      expect(component.tariffPreview).toBeNull();
    });

    it('says "Sin definir" for a first fee in absolute mode', () => {
      const { component } = setup();
      fill(component, 'categoria:4', '50000');

      expect(component.tariffPreview).toEqual({ current: 'Sin definir', next: '$50.000', difference: null });
    });
  });

  describe('field validation messages', () => {
    it('empty value, once the field was touched', () => {
      const { component } = setup();
      component.setIncreaseMode('fixed');
      fill(component, 'genero:Masculino', '');
      expect(component.amountError).toBeNull();

      component.feeForm.get('amount')?.markAsTouched();
      expect(component.amountError).toBe('Ingresá el monto a sumar.');
      expect(component.feeForm.valid).toBe(false);
    });

    it('absolute amount of $0', () => {
      const { component } = setup();
      fill(component, 'genero:Masculino', '0');
      component.feeForm.get('amount')?.markAsTouched();

      expect(component.amountError).toBe('El monto tiene que ser mayor a $0.');
      expect(component.feeForm.valid).toBe(false);
    });

    it('percentage of 0', () => {
      const { component, programar } = setup();
      component.setIncreaseMode('percentage');
      fill(component, 'genero:Masculino', '0');
      component.feeForm.get('amount')?.markAsTouched();

      expect(component.amountError).toBe('El porcentaje tiene que ser mayor a 0.');
      expect(component.feeForm.valid).toBe(false);
      component.onSubmit();
      expect(programar).not.toHaveBeenCalled();
    });

    it('fixed amount of $0', () => {
      const { component } = setup();
      component.setIncreaseMode('fixed');
      fill(component, 'genero:Masculino', '0');
      component.feeForm.get('amount')?.markAsTouched();

      expect(component.amountError).toBe('El monto a sumar tiene que ser mayor a $0.');
    });

    it('destination without a current tariff, as soon as it is chosen', () => {
      const { component, programar } = setup();
      component.setIncreaseMode('percentage');
      component.feeForm.patchValue({ target: 'categoria:4' });

      expect(component.amountError).toBe('Categoría Escuelita no tiene un arancel vigente sobre el que calcular el aumento. Usá "Monto absoluto" para definir su primer valor.');

      fill(component, 'categoria:4', '10');
      expect(component.feeForm.get('amount')?.hasError('sinArancelVigente')).toBe(true);
      component.onSubmit();
      expect(programar).not.toHaveBeenCalled();
    });

    it('the same destination is fine back in absolute mode', () => {
      const { component } = setup();
      component.setIncreaseMode('fixed');
      component.feeForm.patchValue({ target: 'categoria:4' });
      expect(component.amountError).not.toBeNull();

      component.setIncreaseMode('absolute');
      expect(component.amountError).toBeNull();
    });
  });

  it('changing the mode clears the typed value but keeps the destination', () => {
    const { component } = setup();
    fill(component, 'genero:Masculino', '92000');
    component.montoDisplay = '92.000';

    component.setIncreaseMode('percentage');

    expect(component.feeForm.get('amount')?.value).toBe('');
    expect(component.montoDisplay).toBe('');
    expect(component.feeForm.get('target')?.value).toBe('genero:Masculino');
  });

  it('switching to the enrollment tab goes back to absolute mode', () => {
    const { component } = setup();
    component.setIncreaseMode('percentage');

    component.setVista('inscripcion');

    expect(component.increaseMode).toBe('absolute');
  });
});
