import { ChangeDetectorRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject, delay, of } from 'rxjs';

import { RecaudacionMensual, formatFechaPago } from './recaudacion-mensual';
import { RecaudacionService } from '../../services/recaudacion';
import { ReportesService } from '../../services/reportes';
import { PagosService } from '../../services/pagos';
import {
  CollectionPeriodModel, PeriodPaymentModel, PeriodPaymentsPageModel, PeriodPaymentsQuery,
} from '../../models/CollectionPeriodModel';

// HU-018: la pantalla de Recaudación Mensual. Los servicios HTTP se reemplazan por dobles.
// HU-107: el doble de getPagos hace lo que hace el servidor (ordenar y paginar todo el período); el componente
// solo muestra la página que recibe.

const pago = (
  idPago: number, jugador: string, dni: string, monto: number, medioPago: string,
  fechaHoraRegistro: string | null = `2026-10-${String(1 + (idPago % 28)).padStart(2, '0')}T10:00:00-03:00`,
): PeriodPaymentModel => ({
  idPago, jugador, dni, categoria: 'AFA 20067', monto, medioPago,
  fechaPago: (fechaHoraRegistro ?? '2026-10-01').slice(0, 10), fechaHoraRegistro,
});

function periodo(detalle: PeriodPaymentModel[]): CollectionPeriodModel {
  const efectivo = detalle.filter((p) => p.medioPago === 'Efectivo');
  const transferencia = detalle.filter((p) => p.medioPago === 'Transferencia');
  const suma = (lista: PeriodPaymentModel[]) => lista.reduce((t, p) => t + p.monto, 0);
  return {
    total: suma(detalle), efectivo: suma(efectivo), transferencia: suma(transferencia),
    pagos: detalle.length, pagosEfectivo: efectivo.length, pagosTransferencia: transferencia.length,
  };
}

// El "servidor" de las pruebas: ordena con desempate por id y corta la página pedida.
function paginaDelServidor(detalle: PeriodPaymentModel[], q: PeriodPaymentsQuery): PeriodPaymentsPageModel {
  const signo = q.sortDirection === 'asc' ? 1 : -1;
  const clave = (p: PeriodPaymentModel) => (q.sortBy === 'monto' ? p.monto : (p.fechaHoraRegistro ?? p.fechaPago));
  const ordenados = [...detalle].sort((a, b) => {
    const [x, y] = [clave(a), clave(b)];
    return (x < y ? -1 : x > y ? 1 : a.idPago - b.idPago) * signo;
  });
  return {
    items: ordenados.slice((q.page - 1) * q.pageSize, q.page * q.pageSize),
    total: detalle.length,
    pagina: q.page,
    tamanioPagina: q.pageSize,
    totalPaginas: Math.max(1, Math.ceil(detalle.length / q.pageSize)),
  };
}

// Como el servidor real, los dobles responden de forma asíncrona. El componente repinta dentro de la respuesta
// (cdr.detectChanges), y con una respuesta instantánea eso ocurriría mientras se está creando la vista.
const respuesta = <T>(valor: T) => of(valor).pipe(delay(0));
const espera = () => new Promise((resolve) => setTimeout(resolve, 20));

const llamadas: { anio: number; mes: number; idCategoria: number | null }[] = [];
const consultasPagos: PeriodPaymentsQuery[] = [];
const exportaciones: unknown[][] = [];

async function crear(detalle: PeriodPaymentModel[]) {
  llamadas.length = 0;
  consultasPagos.length = 0;
  exportaciones.length = 0;
  await TestBed.configureTestingModule({
    imports: [RecaudacionMensual],
    providers: [
      { provide: ReportesService, useValue: { contar: vi.fn(() => of({tipo: 'recaudacion', cantidadFilas: 1})) } },
      {
        provide: RecaudacionService,
        useValue: {
          getDelPeriodo: (anio: number, mes: number, idCategoria: number | null) => {
            llamadas.push({ anio, mes, idCategoria });
            return respuesta(periodo(detalle));
          },
          getPagos: (q: PeriodPaymentsQuery) => {
            consultasPagos.push(q);
            return respuesta(paginaDelServidor(detalle, q));
          },
          exportarPdf: (...args: unknown[]) => { exportaciones.push(['pdf', ...args]); return new Subject(); },
          exportarCsv: (...args: unknown[]) => { exportaciones.push(['csv', ...args]); return new Subject(); },
        },
      },
      { provide: PagosService, useValue: { getCategorias: () => respuesta([{ idCategoria: 5, nombre: 'AFA 20067' }, { idCategoria: 9, nombre: 'Pt preAFA 2015' }]) } },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(RecaudacionMensual);
  fixture.detectChanges();
  // Se espera a que respondan los dobles (el temporizador de delay(0)) y se vuelve a dibujar.
  await espera();
  await fixture.whenStable();
  fixture.detectChanges();
  return { fixture, component: fixture.componentInstance, el: fixture.nativeElement as HTMLElement };
}

async function redibujar(fixture: { detectChanges(): void; whenStable(): Promise<unknown> }) {
  await espera();
  await fixture.whenStable();
  fixture.detectChanges();
}

const muchos = (n: number) => Array.from({ length: n }, (_, i) =>
  pago(i + 1, `JUGADOR ${i + 1}`, String(90000000 + i), 1000 * (1 + (i % 9)), i % 3 === 0 ? 'Transferencia' : 'Efectivo'));

describe('RecaudacionMensual - estados HU-107', () => {
  it('no presenta filas y conteo del filtro anterior mientras carga otro', async () => {
    const { fixture, component, el } = await crear(muchos(40));
    const pendiente = new Subject<PeriodPaymentsPageModel>();
    vi.spyOn(TestBed.inject(RecaudacionService), 'getPagos').mockReturnValue(pendiente);
    component.onCategoriaChange('9');
    fixture.componentRef.injector.get(ChangeDetectorRef).markForCheck();
    fixture.detectChanges();
    expect(component.filas).toEqual([]);
    expect(el.querySelector('.tabla')).toBeNull();
    expect(el.querySelector('.tabla-conteo')!.textContent).toContain('Cargando');
    expect(el.querySelector('.tabla-conteo')!.textContent).not.toContain('40 pagos');
    pendiente.next({ items: [], total: 0, pagina: 1, tamanioPagina: 10, totalPaginas: 1 });
    await redibujar(fixture);
    expect(el.querySelector('.tabla-conteo')!.textContent).toContain('0 pagos');
  });

  it('un error no anuncia cero pagos y permite reintentar solo la tabla', async () => {
    const { fixture, component, el } = await crear(muchos(40));
    const pendiente = new Subject<PeriodPaymentsPageModel>();
    const consulta = vi.spyOn(TestBed.inject(RecaudacionService), 'getPagos').mockReturnValue(pendiente);
    const globales = llamadas.length;
    component.irAPagina(2);
    pendiente.error(new Error('Sin conexión'));
    await redibujar(fixture);
    expect(el.querySelector('.tabla-conteo')!.textContent).not.toContain('0 pagos');
    expect(el.querySelector('.tabla-conteo')!.textContent).toContain('Conteo no disponible');
    const reintentar = Array.from(el.querySelectorAll<HTMLButtonElement>('button')).find((b) => b.textContent!.includes('Reintentar'));
    expect(reintentar).toBeDefined();
    consulta.mockReturnValue(respuesta(paginaDelServidor(muchos(40), consultasPagos[0])));
    reintentar!.click();
    await redibujar(fixture);
    expect(component.errorPagos).toBe(false);
    expect(llamadas.length).toBe(globales);
  });
});

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
      pago(101, 'PRUEBA, MATIAS', '99000003', 100000, 'Transferencia', '2026-10-05T10:00:00-03:00'),
      pago(103, 'PRUEBA, MATIAS', '99000003', 100000, 'Efectivo', '2026-10-04T10:00:00-03:00'),
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
  it('elegir una categoría consulta al instante, sin botón "Aplicar"', async () => {
    const { component, el } = await crear([pago(101, 'PRUEBA, MATIAS', '99000003', 100000, 'Efectivo')]);
    expect(el.querySelector('.btn-aplicar')).toBeNull();

    component.onCategoriaChange('5');
    await espera();

    expect(llamadas.at(-1)!.idCategoria).toBe(5);
    expect(consultasPagos.at(-1)!.idCategoria).toBe(5);
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
    expect(consultasPagos.at(-1)).toMatchObject({ anio: anterior, mes: 3, page: 1 });
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
    await espera();
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
    const paginas: Subject<PeriodPaymentsPageModel>[] = [];
    await TestBed.configureTestingModule({
      imports: [RecaudacionMensual],
      providers: [
      { provide: ReportesService, useValue: { contar: vi.fn(() => of({tipo: 'recaudacion', cantidadFilas: 1})) } },
        {
          provide: RecaudacionService,
          useValue: {
            getDelPeriodo: () => { const s = new Subject<CollectionPeriodModel>(); respuestas.push(s); return s; },
            getPagos: () => { const s = new Subject<PeriodPaymentsPageModel>(); paginas.push(s); return s; },
          },
        },
        { provide: PagosService, useValue: { getCategorias: () => respuesta([]) } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(RecaudacionMensual);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.onAnioChange(new Date().getFullYear() - 1);   // pide otro período mientras el primero sigue en camino
    expect(respuestas).toHaveLength(2);
    expect(respuestas[0].observed).toBe(false);              // el pedido viejo se canceló
    expect(paginas[0].observed).toBe(false);

    const nuevo = [pago(1, 'NUEVO', '1', 50000, 'Efectivo')];
    const viejo = [pago(2, 'VIEJO', '2', 99000, 'Transferencia')];
    const q: PeriodPaymentsQuery = { anio: 2025, mes: 10, idCategoria: null, page: 1, pageSize: 10, sortBy: 'fecha', sortDirection: 'desc' };
    respuestas[1].next(periodo(nuevo));
    paginas[1].next(paginaDelServidor(nuevo, q));
    respuestas[0].next(periodo(viejo));
    paginas[0].next(paginaDelServidor(viejo, q));

    expect(component.filas.map((f) => f.jugador)).toEqual(['NUEVO']);
    expect(component.datos!.total).toBe(50000);
    await espera();
  });
});

// HU-107: fecha de cada pago, paginación y orden en el servidor, métricas del período completo.
describe('RecaudacionMensual - fecha y paginación (HU-107)', () => {
  const textoFilas = (el: HTMLElement, columna: number) =>
    Array.from(el.querySelectorAll(`.tabla tbody tr td:nth-child(${columna})`)).map((td) => td.textContent!.trim());
  const encabezado = (el: HTMLElement, texto: string) =>
    Array.from(el.querySelectorAll<HTMLButtonElement>('.tabla thead .orden-boton')).find((b) => b.textContent!.includes(texto))!;
  const boton = (el: HTMLElement, texto: string) =>
    Array.from(el.querySelectorAll<HTMLButtonElement>('.pagination-button')).find((b) => b.textContent!.includes(texto))!;

  it('la primera columna es la fecha de cada pago, en dd/MM/yyyy con la hora del club', async () => {
    const { el } = await crear([
      pago(1, 'CON HORA', '1', 1000, 'Efectivo', '2026-10-31T14:35:12-03:00'),
      pago(2, 'SIN HORA', '2', 1000, 'Efectivo', null),
    ]);

    expect(el.querySelector('.tabla thead th')!.textContent).toContain('Fecha');
    expect(textoFilas(el, 1)).toEqual(['31/10/2026 14:35', '01/10/2026']);
  });

  // Los dígitos que manda el servidor son los que se ven: no pasan por la zona horaria del navegador.
  it('la fecha no depende de la zona del navegador: un cobro a las 23:59 sigue siendo de ese día', () => {
    expect(formatFechaPago({ fechaPago: '2026-10-31', fechaHoraRegistro: '2026-10-31T23:59:59.1234567-03:00' })).toBe('31/10/2026 23:59');
    expect(formatFechaPago({ fechaPago: '2026-11-01', fechaHoraRegistro: '2026-11-01T00:00:00-03:00' })).toBe('01/11/2026 00:00');
    expect(formatFechaPago({ fechaPago: '2026-09-07', fechaHoraRegistro: null })).toBe('07/09/2026');
  });

  it('muestra el total real de pagos del período y en qué página está, no las filas visibles', async () => {
    const { el } = await crear(muchos(126));

    expect(el.querySelectorAll('.tabla tbody tr')).toHaveLength(10);
    expect(el.querySelector('.tabla-conteo')!.textContent).toContain('126 pagos en');
    expect(el.querySelector('.tabla-conteo')!.textContent).toContain('Página 1 de 13');
    expect(el.querySelector('.pagination-status')!.textContent!.trim()).toBe('Página 1 de 13');
  });

  it('pide al servidor la página 1 de 10, por fecha descendente', async () => {
    await crear(muchos(3));

    expect(consultasPagos[0]).toMatchObject({ page: 1, pageSize: 10, sortBy: 'fecha', sortDirection: 'desc' });
  });

  it('cambiar de página pide solo la tabla: tarjetas y donut no se vuelven a consultar ni cambian', async () => {
    const { el, fixture, component } = await crear(muchos(126));
    const tarjetas = el.querySelector('.cards')!.textContent;
    const desglose = component.desglose;
    const arco = component.arcoDonut;
    const trazo = el.querySelector('.donut-arc')!.getAttribute('stroke-dasharray');
    const primeraPagina = textoFilas(el, 2);
    const totales = llamadas.length;

    boton(el, 'Siguiente').click();
    await redibujar(fixture);

    expect(consultasPagos.at(-1)).toMatchObject({ page: 2, sortBy: 'fecha', sortDirection: 'desc' });
    expect(llamadas.length).toBe(totales);
    expect(component.desglose).toBe(desglose);
    expect(el.querySelector('.cards')!.textContent).toBe(tarjetas);
    expect(component.arcoDonut).toBe(arco);
    expect(el.querySelector('.donut-arc')!.getAttribute('stroke-dasharray')).toBe(trazo);
    expect(textoFilas(el, 2)).not.toEqual(primeraPagina);
    expect(el.querySelector('.pagination-status')!.textContent!.trim()).toBe('Página 2 de 13');
  });

  it('en la primera página "Anterior" está deshabilitado y en la última "Siguiente"', async () => {
    const { el, fixture } = await crear(muchos(25));
    expect(boton(el, 'Anterior').disabled).toBe(true);

    boton(el, 'Siguiente').click();
    await redibujar(fixture);
    boton(el, 'Siguiente').click();
    await redibujar(fixture);

    expect(el.querySelector('.pagination-status')!.textContent!.trim()).toBe('Página 3 de 3');
    expect(el.querySelectorAll('.tabla tbody tr')).toHaveLength(5);
    expect(boton(el, 'Siguiente').disabled).toBe(true);
  });

  it('ordenar por fecha invierte la dirección en el servidor y vuelve a la página 1', async () => {
    const { el, fixture, component } = await crear(muchos(40));
    boton(el, 'Siguiente').click();
    await redibujar(fixture);
    expect(component.pagina).toBe(2);
    const totales = llamadas.length;

    encabezado(el, 'Fecha').click();
    await redibujar(fixture);

    expect(consultasPagos.at(-1)).toMatchObject({ page: 1, sortBy: 'fecha', sortDirection: 'asc' });
    expect(llamadas.length).toBe(totales);
    expect(el.querySelector('.tabla thead th')!.getAttribute('aria-sort')).toBe('ascending');
  });

  it('ordenar por monto lo pide al servidor, de mayor a menor y después al revés', async () => {
    const { el, fixture } = await crear([
      pago(1, 'A', '1', 100000, 'Efectivo'),
      pago(2, 'B', '2', 20000, 'Efectivo'),
      pago(3, 'C', '3', 9000, 'Efectivo'),
    ]);

    encabezado(el, 'Monto').click();
    await redibujar(fixture);
    expect(consultasPagos.at(-1)).toMatchObject({ page: 1, sortBy: 'monto', sortDirection: 'desc' });
    expect(textoFilas(el, 2)).toEqual(['A', 'B', 'C']);

    encabezado(el, 'Monto').click();
    await redibujar(fixture);
    expect(consultasPagos.at(-1)).toMatchObject({ sortBy: 'monto', sortDirection: 'asc' });
    expect(textoFilas(el, 2)).toEqual(['C', 'B', 'A']);

    const th = el.querySelectorAll('.tabla thead th');
    expect(th[th.length - 1].getAttribute('aria-sort')).toBe('ascending');
    expect(th[0].getAttribute('aria-sort')).toBe('none');
  });

  // Sin ordenar en el navegador: la tabla respeta el orden en que llegan las filas.
  it('muestra las filas en el orden en que las manda el servidor', async () => {
    const { el } = await crear([
      pago(1, 'PRIMERO', '1', 5000, 'Efectivo', '2026-10-30T10:00:00-03:00'),
      pago(2, 'SEGUNDO', '2', 5000, 'Efectivo', '2026-10-10T10:00:00-03:00'),
    ]);

    expect(textoFilas(el, 2)).toEqual(['PRIMERO', 'SEGUNDO']);
  });

  it('cambiar de período vuelve a la página 1, conserva el orden y recalcula tarjetas y total', async () => {
    const { el, fixture, component } = await crear(muchos(40));
    encabezado(el, 'Monto').click();
    await redibujar(fixture);
    boton(el, 'Siguiente').click();
    await redibujar(fixture);
    const totales = llamadas.length;

    component.onAnioChange(new Date().getFullYear() - 1);
    await redibujar(fixture);

    expect(llamadas.length).toBe(totales + 1);
    expect(consultasPagos.at(-1)).toMatchObject({ page: 1, sortBy: 'monto', sortDirection: 'desc', anio: new Date().getFullYear() - 1 });
    expect(component.pagina).toBe(1);
  });

  it('un mes sin cobros muestra 0 pagos, sin paginador ni errores', async () => {
    const { el, component } = await crear([]);

    expect(el.querySelector('.tabla-conteo')!.textContent).toContain('0 pagos en');
    expect(el.querySelector('.pagination')).toBeNull();
    expect(el.querySelector('.tabla')).toBeNull();
    expect(component.desglose).toEqual([]);
  });

  it('un mes con pocos pagos es una sola página, sin paginador', async () => {
    const { el } = await crear(muchos(7));

    expect(el.querySelectorAll('.tabla tbody tr')).toHaveLength(7);
    expect(el.querySelector('.tabla-conteo')!.textContent).toContain('7 pagos en');
    expect(el.querySelector('.pagination')).toBeNull();
  });

  it('exportar pide el período completo al servidor, sin página ni tamaño', async () => {
    const { el, fixture, component } = await crear(muchos(40));
    boton(el, 'Siguiente').click();
    await redibujar(fixture);

    component.exportarRecaudacion('pdf');
    component.exportando = false;
    component.exportarRecaudacion('csv');

    const hoy = new Date();
    expect(exportaciones).toEqual([
      ['pdf', hoy.getFullYear(), hoy.getMonth() + 1, null],
      ['csv', hoy.getFullYear(), hoy.getMonth() + 1, null],
    ]);
  });

  it('los encabezados ordenables son botones con nombre accesible', async () => {
    const { el } = await crear(muchos(3));

    expect(encabezado(el, 'Fecha').getAttribute('aria-label')).toBe('Ordenado por fecha, más reciente primero. Cambiar el orden');
    expect(encabezado(el, 'Monto').getAttribute('aria-label')).toBe('Ordenar por monto');
    expect(el.querySelector('nav.pagination')).toBeNull();
  });
});
