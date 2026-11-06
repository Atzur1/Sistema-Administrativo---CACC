import { ChangeDetectorRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormBuilder } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { Subject, of, throwError } from 'rxjs';

import { ActualizacionAranceles } from './actualizacion-aranceles';
import { EnrollmentFeeModel } from '../../models/EnrollmentFeeModel';
import { TariffImpactModel } from '../../models/TariffModel';
import { ArancelesService } from '../../services/aranceles';
import { EnrollmentFeeService } from '../../services/enrollment-fees';
import { PagosService } from '../../services/pagos';
import { NotificationService } from '../../shared/notifications/notification.service';

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

function impactoDePrueba(cambios: Partial<TariffImpactModel> = {}): TariffImpactModel {
  return {
    tipo: 'porcentaje', valor: 10, genero: 'Masculino', idCategoria: null, nombreCategoria: null,
    vigenteDesde: '2099-01-01', montoBase: 85000, montoNuevo: 93500, mesDeAplicacion: '2099-01-01', reemplazadoDesde: null,
    lineas: [
      { idCategoria: 2, nombreCategoria: 'Sub 15', genero: 'Masculino', montoActual: 85000, montoNuevo: 93500, jugadores: 20, diferenciaPorJugador: 8500, diferenciaTotal: 170000 },
      { idCategoria: 1, nombreCategoria: 'Sub 17', genero: 'Masculino', montoActual: 85000, montoNuevo: 93500, jugadores: 22, diferenciaPorJugador: 8500, diferenciaTotal: 187000 },
    ],
    excluidos: [], jugadores: 42, categorias: 2, diferenciaMensual: 357000, mesEnCurso: false,
    cuotasDelMesPendientesSinPagos: 0, cuotasDelMesConPagos: 0, seEmiteElDia: '2099-01-10',
    requiereConfirmacionReforzada: true, huella: 'HUELLA-1',
    ...cambios,
  };
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

// HU-069: increase by percentage or by a fixed amount over the current tariff. HU-106: the portal does no money math —
// it sends what was typed (type + value) to the backend simulation, which works out the final amount and its impact.
// So every case checks what reaches arancelesService.simular, and that nothing is scheduled before confirming.
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
    const simular = vi.fn(() => of(impactoDePrueba()));
    const component = new ActualizacionAranceles(
      new FormBuilder(),
      { getResumen: () => of({}), getHistorial: () => of(history), programar, simular } as never,
      { getAllEnrollmentFees: () => of([]), createEnrollmentFee: vi.fn() } as never,
      { getCategorias: () => of([{ idCategoria: 3, nombre: 'AFA' }, { idCategoria: 4, nombre: 'Escuelita' }]) } as never,
      { detectChanges: () => {}, markForCheck: () => {} } as never,
      { notify: vi.fn() } as never,
    );
    component.ngOnInit();
    return { component, programar, simular };
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
    it('sends the typed amount to the simulation and schedules nothing yet', () => {
      const { component, programar, simular } = setup();
      fill(component, 'genero:Masculino', '92000');

      component.onSubmit();
      expect(simular).toHaveBeenCalledWith({ genero: 'Masculino', tipo: 'absoluto', valor: 92000, vigenteDesde: '2099-01-01' });
      expect(programar).not.toHaveBeenCalled();
    });

    it('works on a destination without a current tariff (its first fee)', () => {
      const { component, simular } = setup();
      fill(component, 'categoria:4', '50000');

      expect(component.feeForm.valid).toBe(true);
      expect(component.amountError).toBeNull();
      component.onSubmit();
      expect(simular).toHaveBeenCalledWith({ idCategoria: 4, tipo: 'absoluto', valor: 50000, vigenteDesde: '2099-01-01' });
    });
  });

  describe('percentage mode', () => {
    it('sends the percentage as typed: the backend works out $85.000 + 12,5%', () => {
      const { component, simular } = setup();
      component.setIncreaseMode('percentage');
      fill(component, 'genero:Masculino', '12.5');

      component.onSubmit();
      expect(simular).toHaveBeenCalledWith({ genero: 'Masculino', tipo: 'porcentaje', valor: 12.5, vigenteDesde: '2099-01-01' });
    });

    it('accepts "12,5" typed with a comma', () => {
      const { component, simular } = setup();
      component.setIncreaseMode('percentage');
      component.feeForm.patchValue({ target: 'genero:Masculino', validFrom: '2099-01-01' });
      const input = { value: '12,5' } as HTMLInputElement;

      component.onAmountInput(input);
      component.onSubmit();

      expect(input.value).toBe('12,5');
      expect(component.feeForm.get('amount')?.value).toBe('12.5');
      expect(simular).toHaveBeenCalledWith(expect.objectContaining({ tipo: 'porcentaje', valor: 12.5 }));
    });
  });

  describe('fixed amount mode', () => {
    it('sends the amount to add as typed', () => {
      const { component, simular } = setup();
      component.setIncreaseMode('fixed');
      fill(component, 'genero:Masculino', '10000');

      component.onSubmit();
      expect(simular).toHaveBeenCalledWith({ genero: 'Masculino', tipo: 'montoFijo', valor: 10000, vigenteDesde: '2099-01-01' });
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
      const { component, simular } = setup();
      component.setIncreaseMode('percentage');
      fill(component, 'genero:Masculino', '0');
      component.feeForm.get('amount')?.markAsTouched();

      expect(component.amountError).toBe('El porcentaje tiene que ser mayor a 0.');
      expect(component.feeForm.valid).toBe(false);
      component.onSubmit();
      expect(simular).not.toHaveBeenCalled();
    });

    it('fixed amount of $0', () => {
      const { component } = setup();
      component.setIncreaseMode('fixed');
      fill(component, 'genero:Masculino', '0');
      component.feeForm.get('amount')?.markAsTouched();

      expect(component.amountError).toBe('El monto a sumar tiene que ser mayor a $0.');
    });

    it('destination without a current tariff, as soon as it is chosen', () => {
      const { component, simular } = setup();
      component.setIncreaseMode('percentage');
      component.feeForm.patchValue({ target: 'categoria:4' });

      expect(component.amountError).toBe('Categoría Escuelita no tiene un arancel vigente sobre el que calcular el aumento. Usá "Monto absoluto" para definir su primer valor.');

      fill(component, 'categoria:4', '10');
      expect(component.feeForm.get('amount')?.hasError('sinArancelVigente')).toBe(true);
      component.onSubmit();
      expect(simular).not.toHaveBeenCalled();
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

// HU-094, corrección de QA (30/10): "¿Cancelar este arancel?" usa la ventana común del portal, como el resto.
describe('ActualizacionAranceles - ventana para cancelar un arancel', () => {
  const FILA = {
    id: 41, target: 'genero:Masculino', category: 'male' as const, categoryLabel: 'Masculino',
    amount: '$ 25.000', validFrom: '2099-03-01', validTo: null, status: 'Programado', statusClass: 'status-scheduled',
  };

  async function crearPantalla(cancelar: ReturnType<typeof vi.fn> = vi.fn(() => of(undefined))) {
    const notify = vi.fn();
    await TestBed.configureTestingModule({
      imports: [ActualizacionAranceles],
      providers: [
        { provide: ArancelesService, useValue: { getResumen: () => of({}), getHistorial: () => of([]), programar: vi.fn(), cancelar } },
        { provide: EnrollmentFeeService, useValue: { getAllEnrollmentFees: () => of([]) } },
        { provide: PagosService, useValue: { getCategorias: () => of([]) } },
        { provide: NotificationService, useValue: { notify } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(ActualizacionAranceles);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    const el = fixture.nativeElement as HTMLElement;
    // La pantalla usa campos comunes (no señales): se marca para redibujar, como hace ella con detectChanges().
    const refrescar = () => {
      fixture.componentRef.injector.get(ChangeDetectorRef).detectChanges();
    };
    const abrir = () => {
      component.askCancel(FILA as never);
      refrescar();
    };
    return { fixture, component, el, abrir, refrescar, cancelar, notify };
  }

  afterEach(() => TestBed.resetTestingModule());

  it('es la ventana común: diálogo modal con título, cruz y la acción en tono de peligro', async () => {
    const { el, abrir } = await crearPantalla();
    abrir();

    const dialogo = el.querySelector('app-ventana [role="dialog"]')!;
    expect(dialogo).not.toBeNull();
    expect(dialogo.getAttribute('aria-modal')).toBe('true');
    expect(dialogo.hasAttribute('cdkTrapFocus') || dialogo.hasAttribute('cdktrapfocus')).toBe(true);
    expect(el.querySelector('.ventana-titulo')!.textContent).toContain('¿Cancelar este arancel?');
    expect(el.querySelector('.ventana-subtitulo')!.textContent).toContain('Masculino');
    expect(el.querySelector('.ventana-cerrar')).not.toBeNull();
    expect(el.querySelector('.ventana')!.classList).toContain('ventana-peligro');
    expect(el.querySelector('.ventana-confirmar')!.classList).toContain('es-peligro');
    expect(el.querySelector('.ventana-confirmar')!.textContent).toContain('Cancelar arancel');
    expect(el.querySelector('.confirm-overlay')).toBeNull(); // la ventana propia de antes ya no existe
  });

  it('se cierra con Esc, con la cruz y con Volver, sin cancelar nada', async () => {
    const { el, abrir, refrescar, cancelar } = await crearPantalla();

    abrir();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    refrescar();
    expect(el.querySelector('[role="dialog"]')).toBeNull();

    abrir();
    el.querySelector<HTMLButtonElement>('.ventana-cerrar')!.click();
    refrescar();
    expect(el.querySelector('[role="dialog"]')).toBeNull();

    abrir();
    el.querySelector<HTMLButtonElement>('.ventana-volver')!.click();
    refrescar();
    expect(el.querySelector('[role="dialog"]')).toBeNull();

    expect(cancelar).not.toHaveBeenCalled();
  });

  it('Cancelar arancel lo cancela una sola vez, avisa y cierra la ventana', async () => {
    const { el, abrir, refrescar, cancelar, notify } = await crearPantalla();
    abrir();

    el.querySelector<HTMLButtonElement>('.ventana-confirmar')!.click();
    refrescar();

    expect(cancelar).toHaveBeenCalledTimes(1);
    expect(cancelar).toHaveBeenCalledWith(41);
    expect(notify).toHaveBeenCalledWith(expect.stringContaining('Arancel cancelado'), 'success');
    expect(el.querySelector('[role="dialog"]')).toBeNull();
  });

  it('mientras se cancela no se puede cerrar ni volver a confirmar', async () => {
    const pendiente = new Subject<void>();
    const { el, abrir, refrescar, cancelar } = await crearPantalla(vi.fn(() => pendiente.asObservable()));
    abrir();

    el.querySelector<HTMLButtonElement>('.ventana-confirmar')!.click();
    refrescar();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    el.querySelector<HTMLButtonElement>('.ventana-confirmar')!.click();
    refrescar();

    expect(el.querySelector('[role="dialog"]')).not.toBeNull();
    expect(el.querySelector('.ventana-confirmar')!.textContent).toContain('Cancelando…');
    expect(cancelar).toHaveBeenCalledTimes(1);
  });
});

// HU-106: resumen de impacto antes de confirmar. Los números los da el backend (simular); la pantalla los muestra, pide la
// confirmación que corresponde y recién confirma con la huella de lo que se revisó.
describe('ActualizacionAranceles - HU-106 resumen de impacto', () => {
  function setup(impacto: TariffImpactModel = impactoDePrueba()) {
    const programar = vi.fn((..._args: unknown[]) => of({ exito: true, mensaje: 'Arancel programado correctamente. Las cuotas con este monto se generan el 10/11/2026.', impacto }));
    const simular = vi.fn(() => of(impacto));
    const notify = vi.fn();
    // El arancel masculino vigente: sin él, el porcentaje no se puede pedir (validación de HU-069).
    const cargarHistorial = vi.fn(() => of([{
      idArancel: 10, genero: 'Masculino', idCategoria: null, nombreCategoria: null, monto: 85000,
      vigenteDesde: '2026-01-01', vigenteHasta: null, estado: 'Vigente', puedeCancelar: false,
    }]));
    const component = new ActualizacionAranceles(
      new FormBuilder(),
      { getResumen: () => of({}), getHistorial: cargarHistorial, programar, simular } as never,
      { getAllEnrollmentFees: () => of([]), createEnrollmentFee: vi.fn() } as never,
      { getCategorias: () => of([]) } as never,
      { detectChanges: () => {}, markForCheck: () => {} } as never,
      { notify } as never,
    );
    component.ngOnInit();
    component.setIncreaseMode('percentage');
    component.feeForm.setValue({ target: 'genero:Masculino', amount: '10', validFrom: '2099-01-01' });
    cargarHistorial.mockClear();
    return { component, programar, simular, notify, cargarHistorial };
  }

  it('Revisar impacto abre el resumen con los números del backend y no programa nada', () => {
    const { component, programar } = setup();

    component.onSubmit();

    expect(component.impacto?.jugadores).toBe(42);
    expect(component.cambioImpactoTexto(component.impacto!)).toBe('Aumento del 10 % sobre el arancel vigente');
    expect(component.categoriasImpactoTexto).toBe('Sub 15, Sub 17');
    expect(component.diferenciaTexto(357000)).toMatch(/^\+\$\s?357\.000 \(aumento\)$/);
    expect(component.diferenciaTexto(-2000)).toMatch(/^−\$\s?2\.000 \(baja\)$/);
    expect(programar).not.toHaveBeenCalled();
  });

  it('con más de una categoría, Confirmar se habilita solo con la cantidad exacta de jugadores', () => {
    const { component, programar } = setup();
    component.onSubmit();

    for (const escrito of ['', '41', '42 ', ' 42', 'cuarenta y dos', '042']) {
      component.confirmacionEscrita = escrito;
      expect(component.confirmacionValida).toBe(false);
      component.confirmarImpacto();
    }
    expect(programar).not.toHaveBeenCalled();

    component.onConfirmacionEscrita({ value: '42' } as HTMLInputElement);
    expect(component.confirmacionValida).toBe(true);
    component.confirmarImpacto();
    expect(programar).toHaveBeenCalledWith(
      { genero: 'Masculino', tipo: 'porcentaje', valor: 10, vigenteDesde: '2099-01-01' }, 'HUELLA-1', 42);
  });

  it('con una sola categoría alcanza con el botón (sin escribir la cantidad)', () => {
    const { component, programar } = setup(impactoDePrueba({ categorias: 1, requiereConfirmacionReforzada: false }));
    component.onSubmit();

    expect(component.confirmacionValida).toBe(true);
    component.confirmarImpacto();
    expect(programar).toHaveBeenCalledWith(expect.anything(), 'HUELLA-1', null);
  });

  it('el aviso de éxito aparece recién con la respuesta del backend, y se recargan los datos', () => {
    const { component, programar, notify, cargarHistorial } = setup();
    const respuesta = new Subject<unknown>();
    programar.mockReturnValueOnce(respuesta as never);
    component.onSubmit();
    component.confirmacionEscrita = '42';

    component.confirmarImpacto();
    expect(component.confirmandoImpacto).toBe(true);
    expect(notify).not.toHaveBeenCalled();

    respuesta.next({ exito: true, mensaje: 'Arancel programado correctamente. Las cuotas con este monto se generan el 10/11/2026.', impacto: impactoDePrueba() });
    expect(component.impacto).toBeNull();
    expect(notify).toHaveBeenCalledWith(expect.stringMatching(/^Aranceles actualizados correctamente: género Masculino pasa a .*93\.500.*42 jugadores.*Las cuotas con este monto se generan el 10\/11\/2026\.$/), 'success');
    expect(cargarHistorial).toHaveBeenCalled();
  });

  it('si el impacto cambió (409), no se graba: muestra el nuevo resumen y pide escribir la nueva cantidad', () => {
    const { component, programar, notify } = setup();
    const nuevo = impactoDePrueba({ jugadores: 44, huella: 'HUELLA-2' });
    programar.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 409, error: { mensaje: 'El impacto cambió.', impacto: nuevo } })) as never);
    component.onSubmit();
    component.confirmacionEscrita = '42';

    component.confirmarImpacto();

    expect(component.impacto?.huella).toBe('HUELLA-2');
    expect(component.avisoImpactoActualizado).toBe('El impacto cambió.');
    expect(component.confirmacionEscrita).toBe('');
    expect(component.confirmacionValida).toBe(false);
    expect(notify).not.toHaveBeenCalled();
  });

  it('si falla al grabar, no muestra éxito y deja el resumen abierto para reintentar', () => {
    const { component, programar, notify } = setup();
    programar.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500, error: { mensaje: 'Error interno al programar el arancel.' } })) as never);
    component.onSubmit();
    component.confirmacionEscrita = '42';

    component.confirmarImpacto();

    expect(component.errorImpacto).toBe('Error interno al programar el arancel.');
    expect(component.impacto).not.toBeNull();
    expect(notify).not.toHaveBeenCalled();
  });

  it('cambiar cualquier dato del form descarta el resumen: hay que volver a revisarlo', () => {
    const { component } = setup();
    component.onSubmit();
    expect(component.impacto).not.toBeNull();

    component.feeForm.patchValue({ amount: '11' });

    expect(component.impacto).toBeNull();
  });

  it('Volver descarta el resumen sin programar nada', () => {
    const { component, programar } = setup();
    component.onSubmit();

    component.cerrarImpacto();

    expect(component.impacto).toBeNull();
    expect(programar).not.toHaveBeenCalled();
  });

  it('si la simulación es rechazada (400), avisa el motivo y no abre ningún resumen', () => {
    const { component, simular, notify } = setup();
    simular.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 400, error: { mensaje: 'El género Masculino ya tiene un arancel que rige desde el 01/11/2099.' } })) as never);

    component.onSubmit();

    expect(component.impacto).toBeNull();
    expect(notify).toHaveBeenCalledWith('El género Masculino ya tiene un arancel que rige desde el 01/11/2099.', 'error');
  });

  // HU-090: sin un mensaje utilizable (vacío, un objeto, un cuerpo crudo) se avisa con el texto propio de la pantalla,
  // nunca con un toast vacío ni "[object Object]".
  it('si la API no manda un mensaje utilizable, avisa con el texto propio', () => {
    for (const cuerpo of [{ mensaje: '' }, { mensaje: { detalle: 'x' } }, '<html>502 Bad Gateway</html>', null]) {
      const { component, simular, notify } = setup();
      simular.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 400, error: cuerpo })) as never);

      component.onSubmit();

      expect(notify).toHaveBeenLastCalledWith('No se pudo calcular el impacto del cambio. Intentá de nuevo.', 'error');
    }
  });
});
