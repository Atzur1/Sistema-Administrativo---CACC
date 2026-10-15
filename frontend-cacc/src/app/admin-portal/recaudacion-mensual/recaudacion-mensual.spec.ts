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

const llamadas: { anio: number; mes: number; idCategoria: number | null }[] = [];

async function crear(detalle: PeriodPaymentModel[]) {
  llamadas.length = 0;
  await TestBed.configureTestingModule({
    imports: [RecaudacionMensual],
    providers: [
      { provide: RecaudacionService, useValue: { getDelPeriodo: (anio: number, mes: number, idCategoria: number | null) => { llamadas.push({ anio, mes, idCategoria }); return respuesta(periodo(detalle)); } } },
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

describe('RecaudacionMensual - filtros', () => {
  const espera = () => new Promise((resolve) => setTimeout(resolve, 20));

  it('elegir una categoría consulta al instante, sin botón "Aplicar"', async () => {
    const { component, el } = await crear([pago(101, 'PRUEBA, MATIAS', '99000003', 100000, 'Efectivo')]);
    expect(el.querySelector('.btn-aplicar')).toBeNull();

    component.onCategoriaChange('5');
    await espera();

    expect(llamadas.at(-1)!.idCategoria).toBe(5);
  });

  it('las flechas recorren los meses, cruzando el año hacia atrás', async () => {
    const { component } = await crear([]);
    component.mesSeleccionado = 'Enero';
    component.anioSeleccionado = 2026;

    component.mesAnterior();
    expect(component.periodoLabel).toBe('Diciembre 2025');
    expect(llamadas.at(-1)).toMatchObject({ anio: 2025, mes: 12 });
    await espera();
  });

  it('no se puede avanzar más allá del mes actual', async () => {
    const { component } = await crear([]);
    expect(component.puedeAvanzar).toBe(false);

    component.mesAnterior();
    expect(component.puedeAvanzar).toBe(true);
    component.mesSiguiente();
    expect(component.puedeAvanzar).toBe(false);
    await espera();
  });

  it('el monto va a la derecha y cada medio de pago tiene su color', async () => {
    const { el } = await crear([
      pago(101, 'PRUEBA, MATIAS', '99000003', 100000, 'Transferencia'),
      pago(102, 'PRUEBA, SANTIAGO', '99000005', 85000, 'Efectivo'),
    ]);

    expect(el.querySelector('.tabla tbody .celda-monto')!.classList).toContain('col-monto');
    expect(el.querySelector('.chip-transferencia')).not.toBeNull();
    expect(el.querySelector('.chip-efectivo')).not.toBeNull();
  });
});
