import { ChangeDetectorRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject, delay, of } from 'rxjs';

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

  // HU-070: el período se elige con dos desplegables, Mes y Año, y arranca en el mes y año actuales.
  it('arranca en el mes y el año actuales', async () => {
    const hoy = new Date();
    const { component } = await crear([]);

    expect(llamadas[0]).toMatchObject({ anio: hoy.getFullYear(), mes: hoy.getMonth() + 1 });
    expect(component.anioSeleccionado).toBe(hoy.getFullYear());
  });

  it('elegir otro mes o año consulta al instante', async () => {
    const anterior = new Date().getFullYear() - 1;
    const { component } = await crear([]);

    component.onAnioChange(anterior);
    expect(llamadas.at(-1)).toMatchObject({ anio: anterior });
    component.onMesChange('Marzo');
    expect(llamadas.at(-1)).toMatchObject({ anio: anterior, mes: 3 });
    expect(component.periodoLabel).toBe(`Marzo ${anterior}`);
    await espera();
  });

  it('volver a elegir el mismo período no repite la consulta', async () => {
    const { component } = await crear([]);
    const antes = llamadas.length;

    component.onMesChange(component.mesSeleccionado);
    component.onAnioChange(component.anioSeleccionado);

    expect(llamadas.length).toBe(antes);
  });

  it('en el año en curso no se ofrecen meses que todavía no llegaron', async () => {
    const hoy = new Date();
    const { component } = await crear([]);

    expect(component.mesesOpciones).toHaveLength(hoy.getMonth() + 1);
    component.onAnioChange(hoy.getFullYear() - 1);
    expect(component.mesesOpciones).toHaveLength(12);
    expect(component.aniosOpciones[0].value).toBe(hoy.getFullYear());
    await espera();
  });

  it('al pasar al año en curso con un mes futuro, se queda en el mes actual', async () => {
    const hoy = new Date();
    const { component } = await crear([]);
    component.onAnioChange(hoy.getFullYear() - 1);
    component.onMesChange('Diciembre');

    component.onAnioChange(hoy.getFullYear());

    expect(llamadas.at(-1)).toMatchObject({ anio: hoy.getFullYear(), mes: hoy.getMonth() + 1 });
    await espera();
  });

  it('los desplegables de Mes y Año están en la barra de filtros, sin flechas', async () => {
    const { el } = await crear([]);

    expect(el.querySelector('.field-mes app-custom-select')).not.toBeNull();
    expect(el.querySelector('.field-anio app-custom-select')).not.toBeNull();
    expect(el.querySelector('.mes-flecha')).toBeNull();
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

// HU-070: las tarjetas de Efectivo y Transferencia muestran monto, cantidad de pagos y porcentaje (por cantidad).
describe('RecaudacionMensual - tarjetas por medio de pago', () => {
  it('cada tarjeta muestra su cantidad de pagos y su porcentaje', async () => {
    const { el } = await crear([
      pago(101, 'A', '1', 100000, 'Transferencia'),
      pago(102, 'B', '2', 100000, 'Transferencia'),
      pago(103, 'C', '3', 50000, 'Efectivo'),
    ]);

    expect(el.querySelector('.card-detail-efectivo')!.textContent!.trim()).toBe('1 pago · 33% de los pagos');
    expect(el.querySelector('.card-detail-transferencia')!.textContent!.trim()).toBe('2 pagos · 67% de los pagos');
  });

  it('los porcentajes siempre suman 100', async () => {
    const { component } = await crear([
      pago(101, 'A', '1', 1, 'Transferencia'),
      pago(102, 'B', '2', 1, 'Efectivo'),
      pago(103, 'C', '3', 1, 'Efectivo'),
    ]);

    expect(component.desglose.reduce((t, d) => t + d.porcentaje, 0)).toBe(100);
  });

  it('efectivo + transferencia = total', async () => {
    const { component } = await crear([
      pago(101, 'A', '1', 85000, 'Transferencia'),
      pago(102, 'B', '2', 92500, 'Efectivo'),
    ]);

    expect(component.datos!.efectivo + component.datos!.transferencia).toBe(component.datos!.total);
  });

  it('un mes sin cobros muestra 0 pagos, sin porcentaje', async () => {
    const { el } = await crear([]);

    expect(el.querySelector('.card-detail-efectivo')!.textContent!.trim()).toBe('0 pagos');
    expect(el.querySelector('.card-detail-transferencia')!.textContent!.trim()).toBe('0 pagos');
  });
});

// HU-097: barra de filtros y menú de exportación compartidos, y ninguna respuesta vieja pisa a la nueva.
describe('RecaudacionMensual - filtros y exportación compartidos (HU-097)', () => {
  it('the category shows as a chip; its × and "Limpiar filtros" remove it', async () => {
    const { component, el, fixture } = await crear([]);
    component.onCategoriaChange('5');
    fixture.componentRef.injector.get(ChangeDetectorRef).markForCheck();
    fixture.detectChanges();

    const chip = el.querySelector('.filter-bar-chip')!;
    expect(chip.textContent!.replace('×', '').trim()).toBe('Categoría: AFA 20067');
    expect(el.querySelector<HTMLButtonElement>('.filter-bar-clear')!.disabled).toBe(false);

    chip.querySelector<HTMLButtonElement>('button')!.click();
    expect(component.categoriaSeleccionada).toBe('');
    expect(llamadas.at(-1)!.idCategoria).toBeNull();
    // Se espera la respuesta de la consulta que disparó la ×, como en el resto de las pruebas de filtros.
    await new Promise((resolve) => setTimeout(resolve, 20));
  });

  it('the period is not a removable filter: without a category "Limpiar filtros" is disabled', async () => {
    const { el } = await crear([]);

    expect(el.querySelectorAll('.filter-bar-chip')).toHaveLength(0);
    expect(el.querySelector<HTMLButtonElement>('.filter-bar-clear')!.disabled).toBe(true);
  });

  it('uses the shared "Exportar Reporte" menu', async () => {
    const { el } = await crear([]);

    expect(el.querySelector('app-export-menu .export-menu-trigger')!.textContent!.trim()).toBe('Exportar Reporte');
    expect(el.querySelector('.export-wrap')).toBeNull();
  });

  it('a slow answer for an old month never replaces the month chosen after it', async () => {
    const respuestas: Subject<CollectionPeriodModel>[] = [];
    await TestBed.configureTestingModule({
      imports: [RecaudacionMensual],
      providers: [
        { provide: RecaudacionService, useValue: { getDelPeriodo: () => { const s = new Subject<CollectionPeriodModel>(); respuestas.push(s); return s; } } },
        { provide: PagosService, useValue: { getCategorias: () => respuesta([]) } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(RecaudacionMensual);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.onAnioChange(new Date().getFullYear() - 1);   // pide otro período mientras el primero sigue en camino
    expect(respuestas).toHaveLength(2);
    expect(respuestas[0].observed).toBe(false);              // el pedido viejo se canceló

    respuestas[1].next(periodo([pago(1, 'NUEVO', '1', 50000, 'Efectivo')]));
    respuestas[0].next(periodo([pago(2, 'VIEJO', '2', 99000, 'Transferencia')]));

    expect(component.filas.map((f) => f.jugador)).toEqual(['NUEVO']);
    expect(component.datos!.total).toBe(50000);
    await new Promise((resolve) => setTimeout(resolve, 20));
  });
});
