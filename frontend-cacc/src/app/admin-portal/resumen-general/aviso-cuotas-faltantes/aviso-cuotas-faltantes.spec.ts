import { ChangeDetectorRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';

import { AvisoCuotasFaltantes } from './aviso-cuotas-faltantes';
import { CuotasFaltantesService } from '../../../services/cuotas-faltantes';
import { NotificationService } from '../../../shared/notifications/notification.service';
import { MonthWithMissingFeesModel } from '../../../models/MissingFeeModel';
import { UsuariosPortalService } from '../../../services/usuarios-portal';
import { AuthService } from '../../../services/auth';
import { PendingActionsService } from '../../../services/pending-actions';

const SEPTIEMBRE: MonthWithMissingFeesModel = { periodo: '2026-09-01', cantidad: 45, montoTotal: 4_140_000 };

const JULIO: MonthWithMissingFeesModel = { periodo: '2026-07-01', cantidad: 5, montoTotal: 400_000 };

async function create(
  meses: MonthWithMissingFeesModel[] = [SEPTIEMBRE],
  generarRespuesta: () => Observable<{ mensaje: string; creadas: number }> = () => of({ mensaje: 'Se generaron 2 cuotas de septiembre 2026.', creadas: 2 }),
) {
  const generar = vi.fn(generarRespuesta);
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
      // HU-104: los meses llegan del coordinador de pendientes (la misma consulta que usa la campanita).
      { provide: UsuariosPortalService, useValue: { getCandidatos: () => of([]) } },
      { provide: AuthService, useValue: { isSuperAdmin: () => true, getToken: () => 't' } },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(AvisoCuotasFaltantes);
  fixture.detectChanges();
  const refrescar = () => {
    fixture.componentRef.injector.get(ChangeDetectorRef).markForCheck();
    fixture.detectChanges();
  };
  return {
    fixture, component: fixture.componentInstance, element: fixture.nativeElement as HTMLElement, generar, getMeses, notify,
    refrescar, pendientes: TestBed.inject(PendingActionsService),
  };
}

describe('AvisoCuotasFaltantes', () => {
  afterEach(() => TestBed.inject(PendingActionsService).stop());

  it('no muestra nada si no falta ninguna cuota', async () => {
    const { element } = await create([]);

    expect(element.querySelector('.aviso')).toBeNull();
  });

  it('resume en una línea cuántos meses y cuántas cuotas faltan', async () => {
    const { element } = await create();

    const resumen = element.querySelector('.aviso-resumen')?.textContent?.replace(/\s+/g, ' ') ?? '';
    expect(resumen).toContain('1 mes con cuotas sin generar');
    expect(resumen).toContain('45 cuotas');
  });

  it('con un solo mes, "Revisar" abre directo el detalle para confirmar', async () => {
    const { component, fixture, element } = await create();

    (element.querySelector('.aviso-revisar') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(component.mesElegido).toEqual(SEPTIEMBRE);
    expect(element.querySelector('.aviso-despliegue')).toBeNull();
  });

  it('con varios meses, el listado arranca plegado y "Revisar" lo despliega con cada mes y su total', async () => {
    const { component, fixture, element } = await create([JULIO, SEPTIEMBRE]);
    const resumen = element.querySelector('.aviso-resumen')?.textContent?.replace(/\s+/g, ' ') ?? '';

    expect(resumen).toContain('2 meses con cuotas sin generar');
    expect(resumen).toContain('50 cuotas');
    expect(element.querySelector('.aviso-lista')).toBeNull();

    component.revisar();
    fixture.detectChanges();

    const filas = Array.from(element.querySelectorAll('.aviso-item')).map((f) => f.textContent?.replace(/\s+/g, ' '));
    expect(filas).toHaveLength(2);
    expect(filas[0]).toContain('Julio 2026');
    expect(filas[1]).toContain('Septiembre 2026');
    expect(element.querySelector('.aviso-revisar')?.getAttribute('aria-expanded')).toBe('true');

    component.revisar();
    fixture.detectChanges();
    expect(element.querySelector('.aviso-lista')).toBeNull();
  });

  it('"Generar" solo abre la explicación: no genera nada hasta confirmar', async () => {
    const { component, fixture, element, generar } = await create();

    component.abrir(SEPTIEMBRE);
    fixture.detectChanges();

    const dialogo = element.querySelector('[role="dialog"]')?.textContent?.replace(/\s+/g, ' ') ?? '';
    expect(dialogo).toContain('una cuota de septiembre 2026');
    expect(dialogo).toContain('arancel de septiembre 2026');
    expect(dialogo).toContain('10/10/2026'); // vencen el 10 del mes siguiente
    expect(dialogo).toContain('Auditoría');
    expect(dialogo).toContain('Pérez, Juan');
    expect(generar).not.toHaveBeenCalled();
  });

  // HU-104: el mes generado desaparece al instante del aviso y de la campanita, sin volver a consultar los meses
  // (se respeta el minuto entre consultas; la generación del mes es completa y transaccional).
  it('al confirmar genera ese mes, avisa y lo quita sin volver a consultar los meses', async () => {
    const { component, generar, notify, getMeses, element, refrescar, pendientes } = await create([JULIO, SEPTIEMBRE]);

    component.abrir(SEPTIEMBRE);
    component.confirmar();
    refrescar();

    expect(generar).toHaveBeenCalledWith(2026, 9);
    expect(notify).toHaveBeenCalledWith('Se generaron 2 cuotas de septiembre 2026.', 'success');
    expect(component.mesElegido).toBeNull();
    expect(getMeses).toHaveBeenCalledTimes(1);
    expect(component.meses).toEqual([JULIO]);
    expect(pendientes.items().map((p) => p.key)).toEqual(['cuotas:2026-07']);
    expect(element.querySelector('.aviso-resumen')!.textContent!.replace(/\s+/g, ' ')).toContain('1 mes con cuotas sin generar');
  });

  it('"creadas = 0" (ya estaban todas, por ejemplo por otra operación) también resuelve el mes', async () => {
    const { component, pendientes, refrescar, element } = await create([SEPTIEMBRE], () =>
      of({ mensaje: 'No había cuotas para generar en ese mes: ya estaban todas.', creadas: 0 }));

    component.abrir(SEPTIEMBRE);
    component.confirmar();
    refrescar();

    expect(pendientes.total()).toBe(0);
    expect(element.querySelector('.aviso')).toBeNull();
  });

  it('si la generación falla, el mes sigue pendiente', async () => {
    const { component, pendientes } = await create([SEPTIEMBRE], () =>
      throwError(() => ({ error: { mensaje: 'No se pudieron generar las cuotas.' } })));

    component.abrir(SEPTIEMBRE);
    component.confirmar();

    expect(pendientes.total()).toBe(1);
    expect(component.errorDialogo).toBe('No se pudieron generar las cuotas.');
  });

  it('volver cierra sin generar', async () => {
    const { component, generar } = await create();

    component.abrir(SEPTIEMBRE);
    component.cerrar();

    expect(component.mesElegido).toBeNull();
    expect(generar).not.toHaveBeenCalled();
  });
});
