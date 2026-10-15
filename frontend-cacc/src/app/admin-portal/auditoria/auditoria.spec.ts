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
  // El constructor ya busca la primera página (vista por defecto, sin sección).
  http.expectOne((req) => req.url.endsWith('/auditoria/cambios')).flush({ page: 1, pageSize: 50, total: items.length, items });
  fixture.detectChanges();
  return { fixture, component: fixture.componentInstance, http, el: fixture.nativeElement as HTMLElement };
}

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

    expect(component.resumen(PAGO)).toBe(`Registró un pago de ${new Intl.NumberFormat('es-AR', { style: 'currency', currency: 'ARS', maximumFractionDigits: 2 }).format(5000)} en efectivo`);
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

describe('Auditoria - panel lateral', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('al tocar una fila abre el panel con la frase y la tabla de cambios, y la × lo cierra', async () => {
    const { fixture, el } = await create([ANULACION]);

    el.querySelector<HTMLElement>('tbody tr')!.click();
    fixture.detectChanges();

    const panel = el.querySelector('.drawer');
    expect(panel).not.toBeNull();
    expect(panel!.textContent).toContain('Anuló una bonificación');
    expect(panel!.querySelector('.diff-before')!.textContent).toContain('No');
    expect(panel!.querySelector('.diff-after')!.textContent).toContain('Sí');

    el.querySelector<HTMLElement>('.drawer-close')!.click();
    fixture.detectChanges();
    expect(el.querySelector('.drawer')).toBeNull();
  });

  it('se cierra con la tecla Esc', async () => {
    const { fixture, el, component } = await create([ANULACION]);

    component.abrirDetalle(ANULACION);
    fixture.detectChanges();
    expect(el.querySelector('.drawer')).not.toBeNull();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    expect(el.querySelector('.drawer')).toBeNull();
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

  it('al escribir espera un momento antes de buscar, y busca una sola vez', async () => {
    vi.useFakeTimers();
    try {
      const { component, http } = await create();

      component.email = 'a';
      component.buscarAlEscribir();
      component.email = 'an';
      component.buscarAlEscribir();
      http.expectNone((r) => r.url.endsWith('/auditoria/cambios'));

      vi.advanceTimersByTime(450);
      http.expectOne((r) => r.url.endsWith('/auditoria/cambios') && r.params.get('emailUsuario') === 'an')
        .flush({ page: 1, pageSize: 50, total: 0, items: [] });
    } finally {
      vi.useRealTimers();
    }
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
