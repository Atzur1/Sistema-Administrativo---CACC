import { ChangeDetectorRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { Auditoria } from './auditoria';
import { AuthService } from '../../services/auth';

// HU-068 (devolución de QA): los enlaces de contraseña (TOKEN_ACCESO_CUENTA) tienen que poder verse en Auditoría.
// Antes "Sección" era texto libre que tenía que coincidir con el nombre interno de la tabla.

async function create(items: unknown[] = []) {
  await TestBed.configureTestingModule({
    imports: [Auditoria],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: AuthService, useValue: { getToken: () => 'token' } },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(Auditoria);
  const http = TestBed.inject(HttpTestingController);
  // Las pruebas cambian el estado llamando métodos o campos (no con clics), y la pantalla solo se vuelve a dibujar si
  // está marcada (los componentes se revisan "OnPush" por defecto): cada detectChanges la marca primero, como un clic.
  const vista = fixture.componentRef.injector.get(ChangeDetectorRef);
  const detectar = fixture.detectChanges.bind(fixture);
  fixture.detectChanges = () => {
    vista.markForCheck();
    detectar();
  };
  // El constructor ya busca la primera página (vista por defecto, sin sección).
  http.expectOne((req) => req.url.endsWith('/auditoria/cambios')).flush({ page: 1, pageSize: 50, total: items.length, items });
  fixture.detectChanges();
  return { fixture, component: fixture.componentInstance, http, el: fixture.nativeElement as HTMLElement };
}

// La misma forma que el evento que devuelve la API.
interface Evento {
  id: number;
  userId: number | null;
  userEmail: string | null;
  userName: string | null;
  userSurname: string | null;
  userDni: string | null;
  entity: string;
  entityId: string;
  action: string;
  timestampUtc: string;
  beforeJson: string | null;
  afterJson: string | null;
}

const ENLACE: Evento = {
  id: 1, userId: 1, userEmail: null, userName: 'Alexander', userSurname: 'Monelli', userDni: null,
  entity: 'TOKEN_ACCESO_CUENTA', entityId: '5', action: 'INSERT', timestampUtc: '2026-10-10T18:00:00Z',
  beforeJson: null, afterJson: '{"idUsuario":7,"tipo":"RECUPERACION","venceUtc":"2026-10-10T18:30:00","consumidoUtc":null}',
};

// Un evento cualquiera a partir del de ejemplo. Tiene que ir DESPUÉS de ENLACE: se arma con él.
const evento = (extra: Partial<Evento> = {}): Evento => ({ ...ENLACE, ...extra });

const ANULACION = evento({
  id: 20, entity: 'JUGADORES_DESCUENTOS', entityId: '2012', action: 'UPDATE',
  beforeJson: '{"idJugador":1,"activo":0,"porcentaje":50,"anulado":0,"canceladaPorBaja":0}',
  afterJson: '{"idJugador":1,"activo":0,"porcentaje":50,"anulado":1,"canceladaPorBaja":0}',
});
const PAGO = evento({
  id: 21, entity: 'PAGOS', entityId: '900', action: 'INSERT',
  beforeJson: null, afterJson: '{"idJugador":1,"montoFinal":5000,"metodoPago":"Efectivo","estado":1}',
});

describe('Auditoria - enlaces de contraseña', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('ofrece "Acceso a cuenta" en Sección y la busca por su nombre interno', async () => {
    const { component, http } = await create();

    expect(component.secciones).toContainEqual({ value: 'TOKEN_ACCESO_CUENTA', label: 'Acceso a cuenta' });

    component.entity = 'TOKEN_ACCESO_CUENTA';
    component.search(1);

    const req = http.expectOne((r) => r.url.endsWith('/auditoria/cambios'));
    expect(req.request.params.get('entidad')).toBe('TOKEN_ACCESO_CUENTA');
    req.flush({ page: 1, pageSize: 50, total: 0, items: [] });
  });

  it('muestra el alta de un enlace como "Enlace enviado", con la cuenta afectada y el tipo legible', async () => {
    const { component } = await create();

    expect(component.accionLegible(ENLACE)).toBe('Enlace enviado');
    const campos = component.campos(ENLACE.afterJson);
    expect(campos).toContainEqual({ label: 'Cuenta afectada', value: '7' });
    expect(campos).toContainEqual({ label: 'Tipo de enlace', value: 'Reestablecer contraseña' });
  });
});

// Cada evento se lee como una frase, sin tener que armar la historia con cinco columnas.
describe('Auditoria - qué pasó, en una frase', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('cuenta un pago con su importe y su medio', async () => {
    const { component } = await create();

    // Un importe entero va sin decimales, como en el resto del sistema ("$ 5.000", no "$ 5.000,00").
    const cinco = new Intl.NumberFormat('es-AR', { style: 'currency', currency: 'ARS', minimumFractionDigits: 0, maximumFractionDigits: 2 }).format(5000);
    expect(cinco).not.toContain(',00');
    expect(component.resumen(PAGO)).toBe(`Registró un pago de ${cinco} en efectivo`);
  });

  it('distingue anular, cancelar y modificar una bonificación', async () => {
    const { component } = await create();

    expect(component.resumen(ANULACION)).toBe('Anuló una bonificación');
    const cancelacion = evento({
      entity: 'JUGADORES_DESCUENTOS', action: 'UPDATE',
      beforeJson: '{"activo":1,"anulado":0}', afterJson: '{"activo":0,"anulado":0}',
    });
    expect(component.resumen(cancelacion)).toBe('Canceló una bonificación');
    const alta = evento({ entity: 'JUGADORES_DESCUENTOS', action: 'INSERT', beforeJson: null, afterJson: '{"porcentaje":50}' });
    expect(component.resumen(alta)).toBe('Cargó una bonificación del 50 %');
  });

  it('cuenta el envío de un enlace según su tipo', async () => {
    const { component } = await create();

    expect(component.resumen(ENLACE)).toBe('Envió el enlace para restablecer una contraseña');
  });

  it('una cuota que se salda no se muestra como un pago eliminado', async () => {
    const { component } = await create();
    const saldada = evento({ entity: 'PAGOS', action: 'DELETE', beforeJson: '{"estado":false}', afterJson: null });

    expect(component.resumen(saldada)).toBe('Se saldó una cuota');
  });

  it('con una sección sin frase propia, la dice con su acción y su nombre', async () => {
    const { component } = await create();

    expect(component.resumen(evento({ entity: 'CATEGORIAS', action: 'UPDATE' }))).toBe('Actualizado · División');
  });
});

// "Ver cambios" muestra lo que cambió, no dos copias completas para comparar a ojo.
describe('Auditoria - detalle de un cambio', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('en una modificación destaca solo los datos que cambiaron y pliega el resto', async () => {
    const { component } = await create();

    const detalle = component.detalle(ANULACION);

    expect(detalle.modo).toBe('cambio');
    expect(detalle.cambiados).toEqual([{ label: 'Anulada', antes: 'No', despues: 'Sí' }]);
    expect(detalle.iguales.map((fila) => fila.label)).toEqual(['Jugador (n.º)', 'Activo', 'Porcentaje', 'Cancelada por la baja del jugador']);
  });

  it('un alta lista los datos cargados y una baja los eliminados', async () => {
    const { component } = await create();

    expect(component.detalle(PAGO).modo).toBe('alta');
    expect(component.detalle(PAGO).datos).toContainEqual({ label: 'Medio de pago', value: 'Efectivo' });
    const baja = evento({ action: 'DELETE', beforeJson: '{"activo":1}', afterJson: null });
    expect(component.detalle(baja).modo).toBe('baja');
  });

  it('sin copia de los datos lo dice, en vez de mostrar un panel vacío', async () => {
    const { component } = await create();

    expect(component.detalle(evento({ beforeJson: null, afterJson: null })).modo).toBe('sin-datos');
  });

  it('un cambio sin diferencias visibles no inventa nada', async () => {
    const { component } = await create();
    const igual = evento({ action: 'UPDATE', beforeJson: '{"activo":1}', afterJson: '{"activo":true}' });

    expect(component.detalle(igual).cambiados).toHaveLength(0);
  });

  it('los datos que se guardan como 0 o 1 se leen "Sí" o "No", y los códigos no quedan con su nombre interno', async () => {
    const { component } = await create();

    expect(component.campos('{"anulado":1,"canceladaPorBaja":0}')).toEqual([
      { label: 'Anulada', value: 'Sí' },
      { label: 'Cancelada por la baja del jugador', value: 'No' },
    ]);
  });
});

// Etapa 2: el servidor manda el nombre de lo que cada evento menciona solo por número, y la pantalla lo usa.
describe('Auditoria - nombres en vez de números', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  const NICOLAS = 'PRUEBA, NICOLAS (DNI 99000001)';
  const conNombres: Evento & { references: Record<string, string> } = {
    ...ANULACION,
    references: { 'jugador:1': NICOLAS },
  };

  // En la frase va solo el nombre: el DNI queda en la línea de abajo y en el detalle, no repetido en las dos.
  it('la frase nombra a quién le pasó, sin el DNI', async () => {
    const { component } = await create();

    expect(component.resumen(conNombres)).toBe('Anuló la bonificación de PRUEBA, NICOLAS');
    const pago = { ...PAGO, references: { 'jugador:1': NICOLAS } };
    expect(component.resumen(pago)).toContain('Registró el pago de PRUEBA, NICOLAS: ');
    expect(component.resumen(pago)).not.toContain('DNI');
    expect(component.resumen(pago)).toContain('en efectivo');
  });

  it('sin el nombre, la frase sigue siendo genérica (el jugador ya no existe en la base)', async () => {
    const { component } = await create();

    expect(component.resumen(ANULACION)).toBe('Anuló una bonificación');
  });

  it('la segunda línea de la fila dice qué registro es y el DNI, sin repetir el nombre de la frase', async () => {
    const { component } = await create();

    expect(component.contexto(conNombres)).toBe('Beneficio de jugador n.º 2012 · DNI 99000001');
    expect(component.contexto(conNombres)).not.toContain('NICOLAS');
    // Sin nombre resuelto no hay DNI que mostrar: queda solo el registro.
    expect(component.contexto(ANULACION)).toBe('Beneficio de jugador n.º 2012');
  });

  it('en el detalle el jugador aparece con su nombre y sin la marca "n.º"', async () => {
    const { component } = await create();

    const jugador = component.detalle(conNombres).iguales.find((fila) => fila.label.startsWith('Jugador'))!;

    expect(jugador).toEqual({ label: 'Jugador', antes: NICOLAS, despues: NICOLAS });
  });

  it('si el nombre no llegó, el detalle muestra el número y lo aclara', async () => {
    const { component } = await create();

    const jugador = component.detalle(ANULACION).iguales.find((fila) => fila.label.startsWith('Jugador'))!;

    expect(jugador).toEqual({ label: 'Jugador (n.º)', antes: '1', despues: '1' });
  });

  it('nombra la división, el beneficio de un pago y el rol de una cuenta', async () => {
    const { component } = await create();
    const pago = {
      ...PAGO,
      afterJson: '{"idJugador":1,"idDescuento":12,"idCategoria":5}',
      references: { 'jugador:1': NICOLAS, 'beneficio:12': 'Becado · 50 %', 'categoria:5': 'AFA 20067' },
    };

    expect(component.campos(pago.afterJson, pago.references)).toEqual([
      { label: 'Jugador', value: NICOLAS },
      { label: 'Beneficio', value: 'Becado · 50 %' },
      { label: 'División', value: 'AFA 20067' },
    ]);
    expect(component.campos('{"rolPortal":1,"accesoPortal":true}')).toEqual([
      { label: 'Rol en el portal', value: 'SuperAdmin' },
      { label: 'Acceso al portal', value: 'Sí' },
    ]);
  });

  it('un arancel se identifica por su división o, si no tiene, por su rama', async () => {
    const { component } = await create();
    const arancel = evento({ entity: 'ARANCELES', entityId: '4', action: 'INSERT', beforeJson: null, afterJson: '{"genero":"Masculino","idCategoria":null}' });

    expect(component.sujeto(arancel)).toBe('rama masculino');
    const deDivision = { ...arancel, afterJson: '{"idCategoria":5}', references: { 'categoria:5': 'AFA 20067' } };
    expect(component.sujeto(deDivision)).toBe('AFA 20067');
  });

  it('la ficha, la cuenta y el enlace de contraseña se nombran por la persona a la que pertenecen', async () => {
    const { component } = await create();

    expect(component.sujeto({ ...evento({ entity: 'JUGADORES', entityId: '3' }), references: { 'jugador:3': NICOLAS } })).toBe(NICOLAS);
    expect(component.sujeto({ ...evento({ entity: 'USUARIO', entityId: '4' }), references: { 'usuario:4': 'Monelli, Alexander' } })).toBe('Monelli, Alexander');
    expect(component.resumen({ ...ENLACE, references: { 'usuario:7': 'Monelli, Alexander' } }))
      .toBe('Envió el enlace para restablecer la contraseña de Monelli, Alexander');
  });

  it('la fecha de cada fila va como dd/MM/yyyy, igual que el resto de las pantallas', async () => {
    const { el } = await create([PAGO]);

    expect(el.querySelector('.when-cell strong')!.textContent).toMatch(/^\d{2}\/\d{2}\/\d{4}$/);
  });
});

// Las horas y fechas se muestran en hora del club (Argentina, UTC-3), no en la del navegador ni en UTC.
describe('Auditoria - fechas y horas', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('la hora de la fila es la del club: 18:30 UTC son las 15:30 en Argentina', async () => {
    const { el } = await create([evento({ id: 30, timestampUtc: '2026-10-11T18:30:56.3671640Z' })]);

    expect(el.querySelector('.when-cell span')!.textContent).toContain('15:30');
    expect(el.querySelector('.when-cell strong')!.textContent).toBe('11/10/2026');
  });

  // La API mandaba la hora de la base sin la "Z" final y el navegador la tomaba por hora local (3 horas de más).
  it('si la hora llega sin la "Z" final, igual se toma como UTC', async () => {
    const { el } = await create([evento({ id: 31, timestampUtc: '2026-10-11T18:30:56.3671640' })]);

    expect(el.querySelector('.when-cell span')!.textContent).toContain('15:30');
  });

  it('cerca de la medianoche el día también es el del club', async () => {
    const { el } = await create([evento({ id: 32, timestampUtc: '2026-10-15T00:33:00Z' })]);

    expect(el.querySelector('.when-cell strong')!.textContent).toBe('14/10/2026');
    expect(el.querySelector('.when-cell span')!.textContent).toContain('21:33');
  });

  it('una fecha sin hora no se corre un día, y el período de una cuota se lee como el mes', async () => {
    const { component } = await create();

    expect(component.campos('{"vencimiento":"2026-10-01","fechaPago":"2026-10-14","vigente_desde":"2026-01-01"}')).toEqual([
      { label: 'Cuota del período', value: 'Octubre 2026' },
      { label: 'Fecha de pago', value: '14/10/2026' },
      { label: 'Vigente desde', value: '01/01/2026' },
    ]);
  });

  it('una fecha con hora se muestra en hora del club', async () => {
    const { component } = await create();

    const [vence] = component.campos('{"venceUtc":"2026-10-10T18:30:00"}');

    expect(vence.value).toBe('10/10/2026 15:30 hs');
  });
});

describe('Auditoria - panel lateral', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('al tocar una fila abre el panel con la frase y la tabla de cambios, y la × lo cierra', async () => {
    const { fixture, el } = await create([ANULACION]);

    el.querySelector<HTMLElement>('tbody tr')!.click();
    fixture.detectChanges();

    const panel = el.querySelector('[role="dialog"]');
    expect(panel).not.toBeNull();
    expect(panel!.textContent).toContain('Anuló una bonificación');
    expect(panel!.querySelector('.diff-before')!.textContent).toContain('No');
    expect(panel!.querySelector('.diff-after')!.textContent).toContain('Sí');

    el.querySelector<HTMLElement>('.ventana-cerrar')!.click();
    fixture.detectChanges();
    expect(el.querySelector('[role="dialog"]')).toBeNull();
  });

  it('se cierra con la tecla Esc', async () => {
    const { fixture, el, component } = await create([ANULACION]);

    component.abrirDetalle(ANULACION);
    fixture.detectChanges();
    expect(el.querySelector('[role="dialog"]')).not.toBeNull();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    expect(el.querySelector('[role="dialog"]')).toBeNull();
  });

  it('los datos sin cambios están plegados hasta que se piden', async () => {
    const { fixture, el, component } = await create([ANULACION]);

    component.abrirDetalle(ANULACION);
    fixture.detectChanges();
    expect(el.querySelector('.data-list')).toBeNull();

    el.querySelector<HTMLElement>('.toggle-same')!.click();
    fixture.detectChanges();
    expect(el.querySelector('.data-list')!.textContent).toContain('Porcentaje');
  });
});

describe('Auditoria - filtros', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  const hoyLocal = () => {
    const d = new Date();
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  };

  it('un período rápido pone desde y hasta, busca solo, y elegirlo de nuevo lo quita', async () => {
    const { component, http } = await create();

    component.aplicarRango(0);
    expect(component.from).toBe(hoyLocal());
    expect(component.to).toBe(hoyLocal());
    http.expectOne((r) => r.url.endsWith('/auditoria/cambios') && r.params.has('desdeUtc'))
      .flush({ page: 1, pageSize: 50, total: 0, items: [] });

    component.aplicarRango(0);
    expect(component.from).toBe('');
    http.expectOne((r) => r.url.endsWith('/auditoria/cambios') && !r.params.has('desdeUtc'))
      .flush({ page: 1, pageSize: 50, total: 0, items: [] });
  });

  it('muestra cada filtro puesto como etiqueta y permite quitarlo', async () => {
    const { component, http } = await create();
    component.email = 'ana';
    component.entity = 'PAGOS';

    expect(component.filtrosActivos.map((f) => f.etiqueta)).toEqual(['Persona: ana', 'Sección: Pago']);

    component.quitarFiltro('email');
    http.expectOne((r) => r.url.endsWith('/auditoria/cambios')).flush({ page: 1, pageSize: 50, total: 0, items: [] });
    expect(component.email).toBe('');
    expect(component.filtrosActivos.map((f) => f.clave)).toEqual(['entity']);
  });

  // HU-097: la espera la hace la barra de filtros compartida (300 ms), escribiendo en su buscador.
  it('al escribir espera un momento antes de buscar, y busca una sola vez', async () => {
    const { component, http, el } = await create();
    vi.useFakeTimers();
    try {
      const input = el.querySelector<HTMLInputElement>('app-filter-bar input[type="search"]')!;
      const escribir = (texto: string) => {
        input.value = texto;
        input.dispatchEvent(new Event('input'));
      };

      escribir('a');
      escribir('an');
      vi.advanceTimersByTime(250);
      http.expectNone((r) => r.url.endsWith('/auditoria/cambios'));

      vi.advanceTimersByTime(100);
      http.expectOne((r) => r.url.endsWith('/auditoria/cambios') && r.params.get('emailUsuario') === 'an')
        .flush({ page: 1, pageSize: 50, total: 0, items: [] });
      expect(component.email).toBe('an');
    } finally {
      vi.useRealTimers();
    }
  });

  // HU-097: una respuesta vieja nunca pisa la de la búsqueda más reciente.
  it('una búsqueda nueva cancela la anterior, y su respuesta no se muestra', async () => {
    const { component, http } = await create();

    component.buscarPersona('ana');
    const vieja = http.expectOne((r) => r.params.get('emailUsuario') === 'ana');
    component.buscarPersona('juan');
    const nueva = http.expectOne((r) => r.params.get('emailUsuario') === 'juan');

    expect(vieja.cancelled).toBe(true);
    nueva.flush({ page: 1, pageSize: 50, total: 1, items: [] });
    expect(component.total).toBe(1);
  });

  it('las etiquetas y "Limpiar filtros" son las de la barra compartida', async () => {
    const { component, el, fixture, http } = await create();
    component.entity = 'PAGOS';
    fixture.detectChanges();

    const etiquetas = Array.from(el.querySelectorAll('.filter-bar-chip')).map((c) => c.textContent!.replace('×', '').trim());
    expect(etiquetas).toEqual(['Sección: Pago']);
    const limpiar = el.querySelector<HTMLButtonElement>('.filter-bar-clear')!;
    expect(limpiar.textContent!.trim()).toBe('Limpiar filtros');

    limpiar.click();
    http.expectOne((r) => r.url.endsWith('/auditoria/cambios') && !r.params.has('entidad'))
      .flush({ page: 1, pageSize: 50, total: 0, items: [] });
    expect(component.entity).toBe('');
  });

  it('avisa que sin elegir una sección se muestran solo pagos y aranceles', async () => {
    const { component, el, fixture } = await create();

    expect(component.alcanceReducido).toBe(true);
    expect(el.querySelector('.scope-note')!.textContent).toContain('pagos y aranceles');

    component.entity = 'USUARIO';
    fixture.detectChanges();
    expect(el.querySelector('.scope-note')).toBeNull();
  });
});
