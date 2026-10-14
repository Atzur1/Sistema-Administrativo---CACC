import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';

import { AvisoCuotasFaltantes } from './aviso-cuotas-faltantes';
import { CuotasFaltantesService } from '../../../services/cuotas-faltantes';
import { NotificationService } from '../../../shared/notifications/notification.service';
import { MonthWithMissingFeesModel } from '../../../models/MissingFeeModel';

const SEPTIEMBRE: MonthWithMissingFeesModel = { periodo: '2026-09-01', cantidad: 45, montoTotal: 4_140_000 };

async function create(meses: MonthWithMissingFeesModel[] = [SEPTIEMBRE]) {
  const generar = vi.fn(() => of({ mensaje: 'Se generaron 2 cuotas de septiembre 2026.', creadas: 2 }));
  const getMeses = vi.fn(() => of(meses));
  const notify = vi.fn();
  await TestBed.configureTestingModule({
    imports: [AvisoCuotasFaltantes],
    providers: [
      {
        provide: CuotasFaltantesService,
        useValue: {
          getMeses,
          getDetalle: () =>
            of([
              { idJugador: 1, nombreCompleto: 'Pérez, Juan', dni: '40111222', categoria: 'Sub 17', monto: 92000 },
              { idJugador: 2, nombreCompleto: 'Gómez, Ana', dni: '41222333', categoria: 'Sub 15', monto: 80000 },
            ]),
          generar,
        },
      },
      { provide: NotificationService, useValue: { notify } },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(AvisoCuotasFaltantes);
  fixture.detectChanges();
  return { fixture, component: fixture.componentInstance, element: fixture.nativeElement as HTMLElement, generar, getMeses, notify };
}

describe('AvisoCuotasFaltantes', () => {
  it('no muestra nada si no falta ninguna cuota', async () => {
    const { element } = await create([]);

    expect(element.querySelector('.aviso')).toBeNull();
  });

  it('avisa cuántas cuotas faltan de cada mes', async () => {
    const { element } = await create();

    expect(element.querySelector('.aviso-item')?.textContent?.replace(/\s+/g, ' ')).toContain('Faltan 45 cuotas de septiembre 2026');
  });

  it('"Generar" solo abre la explicación: no genera nada hasta confirmar', async () => {
    const { component, fixture, element, generar } = await create();

    component.abrir(SEPTIEMBRE);
    fixture.detectChanges();

    const dialogo = element.querySelector('.dialog')?.textContent?.replace(/\s+/g, ' ') ?? '';
    expect(dialogo).toContain('una cuota de septiembre 2026');
    expect(dialogo).toContain('arancel de septiembre 2026');
    expect(dialogo).toContain('10/10/2026'); // vencen el 10 del mes siguiente
    expect(dialogo).toContain('Auditoría');
    expect(dialogo).toContain('Pérez, Juan');
    expect(generar).not.toHaveBeenCalled();
  });

  it('al confirmar genera ese mes, avisa y vuelve a consultar los meses', async () => {
    const { component, generar, notify, getMeses } = await create();

    component.abrir(SEPTIEMBRE);
    component.confirmar();

    expect(generar).toHaveBeenCalledWith(2026, 9);
    expect(notify).toHaveBeenCalledWith('Se generaron 2 cuotas de septiembre 2026.', 'success');
    expect(component.mesElegido).toBeNull();
    expect(getMeses).toHaveBeenCalledTimes(2);
  });

  it('volver cierra sin generar', async () => {
    const { component, generar } = await create();

    component.abrir(SEPTIEMBRE);
    component.cerrar();

    expect(component.mesElegido).toBeNull();
    expect(generar).not.toHaveBeenCalled();
  });
});
