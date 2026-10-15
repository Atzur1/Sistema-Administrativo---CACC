import { TestBed } from '@angular/core/testing';
import { delay, of } from 'rxjs';

import { RecaudacionMensual } from './recaudacion-mensual';
import { RecaudacionService } from '../../services/recaudacion';
import { PagosService } from '../../services/pagos';
import { CollectionPeriodModel, PeriodPaymentModel } from '../../models/CollectionPeriodModel';

// HU-018: la pantalla de Recaudación Mensual. Los servicios HTTP se reemplazan por dobles.

const pago = (idPago: number, jugador: string, dni: string, monto: number, medioPago: string): PeriodPaymentModel => ({
  idPago, jugador, dni, categoria: 'AFA 20067', monto, medioPago,
});

function periodo(detalle: PeriodPaymentModel[]): CollectionPeriodModel {
  const efectivo = detalle.filter((p) => p.medioPago === 'Efectivo');
  const transferencia = detalle.filter((p) => p.medioPago === 'Transferencia');
  const suma = (lista: PeriodPaymentModel[]) => lista.reduce((t, p) => t + p.monto, 0);
  return {
    total: suma(detalle), efectivo: suma(efectivo), transferencia: suma(transferencia),
    pagos: detalle.length, pagosEfectivo: efectivo.length, pagosTransferencia: transferencia.length, detalle,
  };
}

// Como el servidor real, los dobles responden de forma asíncrona. El componente repinta dentro de la respuesta
// (cdr.detectChanges), y con una respuesta instantánea eso ocurriría mientras se está creando la vista.
const respuesta = <T>(valor: T) => of(valor).pipe(delay(0));

async function crear(detalle: PeriodPaymentModel[]) {
  await TestBed.configureTestingModule({
    imports: [RecaudacionMensual],
    providers: [
      { provide: RecaudacionService, useValue: { getDelPeriodo: () => respuesta(periodo(detalle)) } },
      { provide: PagosService, useValue: { getCategorias: () => respuesta([{ idCategoria: 5, nombre: 'AFA 20067' }, { idCategoria: 9, nombre: 'Pt preAFA 2015' }]) } },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(RecaudacionMensual);
  fixture.detectChanges();
  // Se espera a que respondan los dobles (el temporizador de delay(0)) y se vuelve a dibujar.
  await new Promise((resolve) => setTimeout(resolve, 20));
  await fixture.whenStable();
  fixture.detectChanges();
  return { fixture, component: fixture.componentInstance, el: fixture.nativeElement as HTMLElement };
}

describe('RecaudacionMensual - lista de pagos', () => {
  // Un jugador puede pagar dos veces en el mismo mes (en tu captura, "PRUEBA, MATIAS" aparece dos veces). La lista
  // identificaba cada fila por el DNI, así que dos pagos del mismo jugador compartían identificador.
  it('muestra una fila por pago, aunque el mismo jugador tenga más de uno', async () => {
    const { el } = await crear([
      pago(101, 'PRUEBA, MATIAS', '99000003', 100000, 'Transferencia'),
      pago(102, 'PRUEBA, SANTIAGO', '99000005', 100000, 'Transferencia'),
      pago(103, 'PRUEBA, MATIAS', '99000003', 100000, 'Efectivo'),
    ]);

    const filas = Array.from(el.querySelectorAll('.tabla tbody tr'));
    expect(filas).toHaveLength(3);
    expect(filas.filter((f) => f.textContent!.includes('PRUEBA, MATIAS'))).toHaveLength(2);
  });

  it('cada fila conserva su propio medio de pago', async () => {
    const { el } = await crear([
      pago(101, 'PRUEBA, MATIAS', '99000003', 100000, 'Transferencia'),
      pago(103, 'PRUEBA, MATIAS', '99000003', 100000, 'Efectivo'),
    ]);

    const medios = Array.from(el.querySelectorAll('.tabla tbody .chip-medio')).map((m) => m.textContent!.trim());
    expect(medios).toEqual(['Transferencia', 'Efectivo']);
  });

  it('los totales y el conteo salen del período, no de las filas repetidas', async () => {
    const { component } = await crear([
      pago(101, 'PRUEBA, MATIAS', '99000003', 100000, 'Transferencia'),
      pago(103, 'PRUEBA, MATIAS', '99000003', 85000, 'Efectivo'),
    ]);

    expect(component.datos!.total).toBe(185000);
    expect(component.datos!.pagos).toBe(2);
    expect(component.hayCobros).toBe(true);
  });

  it('el selector de categoría ocupa el espacio de su fila (el menú copia su ancho)', async () => {
    const { el } = await crear([pago(101, 'PRUEBA, MATIAS', '99000003', 100000, 'Efectivo')]);

    // El selector es hijo directo de la fila de campos, y por eso necesita la regla que lo estira.
    expect(el.querySelector('.filtro-campos > app-custom-select')).not.toBeNull();
  });
});
