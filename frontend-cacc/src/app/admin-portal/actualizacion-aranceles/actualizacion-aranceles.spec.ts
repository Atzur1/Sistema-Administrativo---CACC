import { FormBuilder } from '@angular/forms';
import { of } from 'rxjs';

import { ActualizacionAranceles } from './actualizacion-aranceles';
import { EnrollmentFeeModel } from '../../models/EnrollmentFeeModel';

// Inscripción por género en "Actualización de aranceles". Se prueba la lógica de la pantalla (formulario y mapeo) sin
// dibujarla: el gráfico de cuotas necesita un canvas que el entorno de pruebas no tiene.
function crear(fees: EnrollmentFeeModel[] = []) {
  const createEnrollmentFee = vi.fn(() => of({} as EnrollmentFeeModel));
  const notify = vi.fn();
  const component = new ActualizacionAranceles(
    new FormBuilder(),
    {
      getResumen: () => of({}),
      getHistorial: () => of([]),
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
