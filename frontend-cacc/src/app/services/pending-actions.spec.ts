import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Subject, of } from 'rxjs';

import { PENDING_REFRESH_MS, PendingActionsService } from './pending-actions';
import { CuotasFaltantesService } from './cuotas-faltantes';
import { UsuariosPortalService } from './usuarios-portal';
import { AuthService } from './auth';
import { MonthWithMissingFeesModel } from '../models/MissingFeeModel';
import { PortalUserCandidateModel } from '../models/PortalUserModel';

// HU-104: pendientes que esperan una decisión del SuperAdmin (meses con cuotas sin generar y personas candidatas a
// acceso al portal). Las consultas son las existentes; acá se prueba la coordinación: quién consulta, cuándo y cómo
// se mantiene el estado. Cada pedido HTTP es un Subject para responder cuándo y cómo quiere cada prueba.

const SEPT: MonthWithMissingFeesModel = { periodo: '2026-09-01', cantidad: 45, montoTotal: 4_140_000 };
const AGO: MonthWithMissingFeesModel = { periodo: '2026-08-01', cantidad: 25, montoTotal: 2_300_000 };
const cand = (idUsuario: number, nombre: string, apellido: string): PortalUserCandidateModel =>
  ({ idUsuario, nombre, apellido, dni: '30111222', email: `${nombre}@cacc.test`, fkIdRolSugerido: 2 });

function setup(rol: number | null = 1) {
  const sesion = { rol, token: rol === null ? null : 'token-1' };
  const meses: Subject<MonthWithMissingFeesModel[]>[] = [];
  const candidatos: Subject<PortalUserCandidateModel[]>[] = [];
  const getMeses = vi.fn(() => { const s = new Subject<MonthWithMissingFeesModel[]>(); meses.push(s); return s; });
  const getCandidatos = vi.fn(() => { const s = new Subject<PortalUserCandidateModel[]>(); candidatos.push(s); return s; });

  TestBed.configureTestingModule({
    providers: [
      { provide: CuotasFaltantesService, useValue: { getMeses } },
      { provide: UsuariosPortalService, useValue: { getCandidatos } },
      {
        provide: AuthService,
        useValue: {
          isSuperAdmin: () => sesion.rol === 1,
          getToken: () => sesion.token,
        },
      },
    ],
  });
  const service = TestBed.inject(PendingActionsService);
  const responder = <T>(lista: Subject<T>[], valor: T, i = lista.length - 1) => {
    lista[i].next(valor);
    lista[i].complete();
  };
  return { service, sesion, meses, candidatos, getMeses, getCandidatos, responder };
}

describe('PendingActionsService - quién consulta y cuánto cuenta', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('un SuperAdmin consulta una vez cada fuente al entrar al portal, sin pasar por sus pantallas', () => {
    const { service, getMeses, getCandidatos } = setup(1);

    service.start();

    expect(getMeses).toHaveBeenCalledTimes(1);
    expect(getCandidatos).toHaveBeenCalledTimes(1);
  });

  it('un Administrador (o alguien sin sesión) no consulta ninguna de las dos fuentes', () => {
    for (const rol of [2, null]) {
      TestBed.resetTestingModule();
      const { service, getMeses, getCandidatos } = setup(rol);
      service.start();
      vi.advanceTimersByTime(PENDING_REFRESH_MS * 3);
      expect(getMeses).not.toHaveBeenCalled();
      expect(getCandidatos).not.toHaveBeenCalled();
      expect(service.total()).toBe(0);
    }
  });

  it('cuenta meses más candidatos: dos meses (70 cuotas) y tres personas son 5 pendientes', () => {
    const { service, meses, candidatos, responder } = setup();
    service.start();

    responder(meses, [SEPT, AGO]);
    responder(candidatos, [cand(20, 'Ana', 'Pérez'), cand(21, 'Luis', 'Gómez'), cand(22, 'Eva', 'Ruiz')]);

    expect(service.total()).toBe(5);
    expect(service.items().map((p) => p.key)).toEqual([
      'cuotas:2026-09', 'cuotas:2026-08', 'candidato:20', 'candidato:21', 'candidato:22',
    ]);
  });

  it('cada pendiente tiene un texto corto y su enlace; el mes no cambia por zona horaria', () => {
    const { service, meses, candidatos, responder } = setup();
    service.start();
    responder(meses, [SEPT]);
    responder(candidatos, [cand(20, 'Ana', 'Pérez')]);

    const [mes, persona] = service.items();
    expect(mes).toMatchObject({ kind: 'missing-fees', link: '/admin/portal/resumen-general' });
    expect(mes.label).toBe('Cuotas de septiembre de 2026 sin generar (45)');
    expect(persona).toMatchObject({ kind: 'candidate', link: '/admin/portal/usuarios', queryParams: { pestana: 'candidatos' } });
    expect(persona.label).toBe('Revisar acceso de Ana Pérez');
    expect(persona.label).not.toContain('30111222');
  });

  it('mientras carga no afirma que no haya pendientes', () => {
    const { service } = setup();
    service.start();

    expect(service.status()).toBe('loading');
    expect(service.confirmedEmpty()).toBe(false);
  });

  it('con las dos fuentes vacías confirma que no hay pendientes', () => {
    const { service, meses, candidatos, responder } = setup();
    service.start();
    responder(meses, []);
    responder(candidatos, []);

    expect(service.total()).toBe(0);
    expect(service.confirmedEmpty()).toBe(true);
    expect(service.status()).toBe('complete');
  });
});

describe('PendingActionsService - una consulta por fuente por minuto', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('varios consumidores comparten la misma consulta (start es idempotente)', () => {
    const { service, getMeses, getCandidatos } = setup();

    service.start();
    service.start();
    service.start();

    expect(getMeses).toHaveBeenCalledTimes(1);
    expect(getCandidatos).toHaveBeenCalledTimes(1);
  });

  it('a los 59.999 ms no vuelve a consultar; a los 60.000 sí, una vez por fuente', () => {
    const { service, meses, candidatos, responder, getMeses, getCandidatos } = setup();
    service.start();
    responder(meses, [SEPT]);
    responder(candidatos, []);

    vi.advanceTimersByTime(PENDING_REFRESH_MS - 1);
    service.start();                                   // navegar o reabrir no reinicia la ventana
    expect(getMeses).toHaveBeenCalledTimes(1);
    expect(getCandidatos).toHaveBeenCalledTimes(1);

    vi.advanceTimersByTime(1);
    expect(getMeses).toHaveBeenCalledTimes(2);
    expect(getCandidatos).toHaveBeenCalledTimes(2);
    expect(PENDING_REFRESH_MS).toBe(60_000);
  });

  it('un error no se reintenta antes del minuto, y conserva lo último válido', () => {
    const { service, meses, candidatos, responder, getMeses } = setup();
    service.start();
    responder(meses, [SEPT]);
    responder(candidatos, []);

    vi.advanceTimersByTime(PENDING_REFRESH_MS);
    meses[1].error(new HttpErrorResponse({ status: 500 }));

    expect(service.months()).toEqual([SEPT]);           // un error no es "cero pendientes"
    expect(service.monthsStatus()).toBe('error');
    expect(service.total()).toBe(1);
    expect(service.status()).toBe('partial');

    vi.advanceTimersByTime(PENDING_REFRESH_MS - 1);
    expect(getMeses).toHaveBeenCalledTimes(2);
    vi.advanceTimersByTime(1);
    expect(getMeses).toHaveBeenCalledTimes(3);
  });

  it('una consulta lenta no se superpone con otra de la misma fuente', () => {
    const { service, meses, candidatos, responder, getMeses } = setup();
    service.start();
    responder(candidatos, []);

    vi.advanceTimersByTime(PENDING_REFRESH_MS * 2);    // la de meses sigue sin responder
    expect(getMeses).toHaveBeenCalledTimes(1);

    responder(meses, [SEPT], 0);                        // ya pasó más de un minuto desde que empezó
    vi.advanceTimersByTime(0);
    expect(getMeses).toHaveBeenCalledTimes(2);
  });

  it('si una fuente falla, la otra sigue disponible', () => {
    const { service, meses, candidatos } = setup();
    service.start();

    meses[0].error(new HttpErrorResponse({ status: 500 }));
    candidatos[0].next([cand(20, 'Ana', 'Pérez')]);
    candidatos[0].complete();

    expect(service.total()).toBe(1);
    expect(service.candidatesStatus()).toBe('ok');
    expect(service.monthsStatus()).toBe('error');
    expect(service.confirmedEmpty()).toBe(false);
  });

  it('una pestaña suspendida mucho tiempo no hace una ráfaga: un intento por fuente', () => {
    const { service, meses, candidatos, responder, getMeses, getCandidatos } = setup();
    service.start();
    responder(meses, []);
    responder(candidatos, []);

    vi.setSystemTime(Date.now() + PENDING_REFRESH_MS * 10); // el reloj avanzó 10 minutos sin correr timers
    vi.advanceTimersByTime(PENDING_REFRESH_MS);

    expect(getMeses).toHaveBeenCalledTimes(2);
    expect(getCandidatos).toHaveBeenCalledTimes(2);
  });
});

describe('PendingActionsService - resoluciones confirmadas', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  function conDatos() {
    const ctx = setup();
    ctx.service.start();
    ctx.responder(ctx.meses, [SEPT, AGO]);
    ctx.responder(ctx.candidatos, [cand(20, 'Ana', 'Pérez'), cand(21, 'Luis', 'Gómez')]);
    return ctx;
  }

  it('habilitar una persona la quita al instante, sin otra consulta de candidatos', () => {
    const { service, getCandidatos } = conDatos();

    service.resolveCandidate(20);

    expect(service.candidates()!.map((c) => c.idUsuario)).toEqual([21]);
    expect(service.total()).toBe(3);
    expect(getCandidatos).toHaveBeenCalledTimes(1);
  });

  it('generar un mes lo quita por su período, sin otra consulta de meses', () => {
    const { service, getMeses } = conDatos();

    service.resolveMonth('2026-09-01');

    expect(service.months()).toEqual([AGO]);
    expect(service.total()).toBe(3);
    expect(getMeses).toHaveBeenCalledTimes(1);
  });

  it('procesar dos veces el mismo éxito no descuenta dos veces ni toca a otro', () => {
    const { service } = conDatos();

    service.resolveCandidate(20);
    service.resolveCandidate(20);
    service.resolveMonth('2026-09-01');
    service.resolveMonth('2026-09-01');

    expect(service.total()).toBe(2);
    expect(service.items().map((p) => p.key)).toEqual(['cuotas:2026-08', 'candidato:21']);
  });

  it('una consulta que empezó antes de resolver no vuelve a traer lo resuelto', () => {
    const { service, meses, candidatos } = conDatos();
    vi.advanceTimersByTime(PENDING_REFRESH_MS);          // empiezan las consultas del minuto siguiente

    service.resolveCandidate(20);
    service.resolveMonth('2026-09-01');
    candidatos[1].next([cand(20, 'Ana', 'Pérez'), cand(21, 'Luis', 'Gómez')]); // respuesta vieja
    candidatos[1].complete();
    meses[1].next([SEPT, AGO]);
    meses[1].complete();

    expect(service.items().map((p) => p.key)).toEqual(['cuotas:2026-08', 'candidato:21']);
  });

  it('lo que diga una consulta empezada después de resolver vuelve a mandar (no hay exclusiones permanentes)', () => {
    const { service, candidatos, responder } = conDatos();
    service.resolveCandidate(20);

    vi.advanceTimersByTime(PENDING_REFRESH_MS);
    responder(candidatos, [cand(20, 'Ana', 'Pérez')]);   // por ejemplo, otro operador la volvió a dejar candidata

    expect(service.candidates()!.map((c) => c.idUsuario)).toEqual([20]);
  });

  it('un cambio de otro operador se ve en el siguiente refresco', () => {
    const { service, candidatos, responder } = conDatos();

    vi.advanceTimersByTime(PENDING_REFRESH_MS);
    responder(candidatos, [cand(21, 'Luis', 'Gómez')]);

    expect(service.candidates()!.map((c) => c.idUsuario)).toEqual([21]);
  });

  it('corregir los datos de un candidato actualiza su texto sin consultar', () => {
    const { service, getCandidatos } = conDatos();

    service.updateCandidate(21, { nombre: 'Luisa', apellido: 'Gómez', dni: '30111223' });

    expect(service.items().find((p) => p.key === 'candidato:21')!.label).toBe('Revisar acceso de Luisa Gómez');
    expect(getCandidatos).toHaveBeenCalledTimes(1);
  });
});

describe('PendingActionsService - respuestas en el acto', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('una fuente que responde en el acto no queda trabada "en curso": vuelve a consultarse al minuto', () => {
    const getMeses = vi.fn(() => new Subject<MonthWithMissingFeesModel[]>());
    const getCandidatos = vi.fn(() => of<PortalUserCandidateModel[]>([]));
    TestBed.configureTestingModule({
      providers: [
        { provide: CuotasFaltantesService, useValue: { getMeses } },
        { provide: UsuariosPortalService, useValue: { getCandidatos } },
        { provide: AuthService, useValue: { isSuperAdmin: () => true, getToken: () => 't' } },
      ],
    });
    const service = TestBed.inject(PendingActionsService);

    service.start();
    vi.advanceTimersByTime(PENDING_REFRESH_MS);

    expect(getCandidatos).toHaveBeenCalledTimes(2);
  });
});

describe('PendingActionsService - sesión', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('stop() detiene el refresco, cancela lo que está en curso y borra los datos', () => {
    const { service, meses, candidatos, responder, getMeses } = setup();
    service.start();
    responder(candidatos, [cand(20, 'Ana', 'Pérez')]);

    service.stop();

    expect(meses[0].observed).toBe(false);
    expect(service.total()).toBe(0);
    expect(service.candidates()).toBeNull();
    vi.advanceTimersByTime(PENDING_REFRESH_MS * 3);
    expect(getMeses).toHaveBeenCalledTimes(1);
  });

  it('una respuesta de otra sesión no repuebla el estado', () => {
    const { service, sesion, candidatos } = setup();
    service.start();

    sesion.token = 'token-2';                            // cambió la sesión mientras el pedido viajaba
    candidatos[0].next([cand(20, 'Ana', 'Pérez')]);
    candidatos[0].complete();

    expect(service.candidates()).toBeNull();
  });

  it('si deja de ser SuperAdmin (o pierde la autorización) se detiene y no sigue consultando', () => {
    const { service, sesion, meses, getMeses, getCandidatos } = setup();
    service.start();

    meses[0].error(new HttpErrorResponse({ status: 403 }));
    vi.advanceTimersByTime(PENDING_REFRESH_MS * 2);
    expect(getMeses).toHaveBeenCalledTimes(1);
    expect(getCandidatos).toHaveBeenCalledTimes(1);

    TestBed.resetTestingModule();
    const otro = setup();
    otro.service.start();
    otro.sesion.rol = 2;
    vi.advanceTimersByTime(PENDING_REFRESH_MS);
    expect(otro.getMeses).toHaveBeenCalledTimes(1);
    expect(otro.service.total()).toBe(0);
    void sesion;
  });

  it('una sesión nueva empieza sin los datos de la anterior', () => {
    const { service, sesion, candidatos, responder, getCandidatos } = setup();
    service.start();
    responder(candidatos, [cand(20, 'Ana', 'Pérez')]);

    service.stop();
    sesion.token = 'token-2';
    service.start();

    expect(service.candidates()).toBeNull();
    expect(getCandidatos).toHaveBeenCalledTimes(2);
  });
});
