import { Type } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { from } from 'rxjs';

import { JugadorPerfil } from '../../admin-portal/jugador-perfil/jugador-perfil';
import { DeudaJugador } from '../../admin-portal/deuda-jugador/deuda-jugador';
import { JugadoresService } from '../../services/jugadores';
import { PagosService } from '../../services/pagos';
import { NotificationService } from '../notifications/notification.service';
import { PlayerSummaryModel } from '../../models/PlayerSummaryModel';

// HU-096: el administrativo pasa de Perfil a Deuda del MISMO jugador y la cabecera tiene que verse igual
// (mismos datos, mismo formato, mismo estado). Lo único que cambia es lo propio de cada pantalla.

const JUGADOR: PlayerSummaryModel = {
  idJugador: 3,
  nombre: 'MATIAS',
  apellido: 'PRUEBA',
  dni: '99000003',
  genero: 'Masculino',
  categoria: 'AFA 20067',
  nombreCompleto: 'PRUEBA, MATIAS',
  estadoDeportivo: 'solo_entrenamientos',
};

const respuesta = <T>(valor: T) => from(Promise.resolve(valor));

async function montar<T>(pantalla: Type<T>) {
  TestBed.resetTestingModule();
  const jugadores = {
    getJugador: vi.fn(() => respuesta(JUGADOR)),
    getHistorialPagos: vi.fn(() => respuesta({ items: [], total: 0, page: 1, pageSize: 10 })),
  };
  const pagos = {
    getDeuda: vi.fn(() => respuesta([])),
    getEstadoDeCuenta: vi.fn(() =>
      respuesta({ playerId: 3, playerFullName: 'PRUEBA, MATIAS', dni: '99000003', totalDebtAmount: 0, fees: [] }),
    ),
  };
  await TestBed.configureTestingModule({
    imports: [pantalla],
    providers: [
      provideRouter([]),
      { provide: JugadoresService, useValue: jugadores },
      { provide: PagosService, useValue: pagos },
      { provide: NotificationService, useValue: { notify: vi.fn() } },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '3' }) } } },
    ],
  }).compileComponents();
  const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

  const fixture: ComponentFixture<T> = TestBed.createComponent(pantalla);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return { el: fixture.nativeElement as HTMLElement, jugadores, navigate };
}

const texto = (el: Element | null) => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

// Lo común de la cabecera, sin el contenido que proyecta cada pantalla.
function cabecera(el: HTMLElement) {
  const raiz = el.querySelector('app-cabecera-jugador')!;
  return {
    avatar: texto(raiz.querySelector('.cj-avatar')),
    nombre: texto(raiz.querySelector('h1')),
    dni: texto(raiz.querySelector('.cj-dni')),
    categoria: texto(raiz.querySelector('.cj-categoria')),
    estado: texto(raiz.querySelector('.cj-estado')),
    tono: Array.from(raiz.querySelector('.cj-estado')!.classList).sort(),
    volver: Array.from(raiz.querySelector('.cj-volver')!.classList).sort(),
    h1EnLaPantalla: el.querySelectorAll('h1').length,
  };
}

describe('Cabecera de jugador: Perfil ↔ Deuda (HU-096)', () => {
  it('muestra exactamente la misma cabecera para el mismo jugador', async () => {
    const perfil = await montar(JugadorPerfil);
    const enPerfil = cabecera(perfil.el);
    const deuda = await montar(DeudaJugador);
    const enDeuda = cabecera(deuda.el);

    expect(enPerfil).toEqual(enDeuda);
    expect(enPerfil).toEqual({
      avatar: 'PM',
      nombre: 'PRUEBA, MATIAS',
      dni: 'DNI 99000003',
      categoria: 'AFA 20067',
      estado: 'Estado deportivo: Solo entrenamientos',
      tono: ['cj-estado', 'etiqueta-estado', 'tono-aviso'],
      volver: ['boton', 'boton-secundario', 'cj-volver'],
      h1EnLaPantalla: 1,
    });
    expect(perfil.jugadores.getJugador).toHaveBeenCalledTimes(1);
    expect(deuda.jugadores.getJugador).toHaveBeenCalledTimes(1);
  });

  it('cada pantalla conserva su propio destino de Volver', async () => {
    const perfil = await montar(JugadorPerfil);
    perfil.el.querySelector<HTMLButtonElement>('.cj-volver')!.click();
    expect(perfil.navigate).toHaveBeenCalledWith(['/admin/portal/actividad-movimientos']);

    const deuda = await montar(DeudaJugador);
    deuda.el.querySelector<HTMLButtonElement>('.cj-volver')!.click();
    expect(deuda.navigate).toHaveBeenCalledWith(['/admin/portal/cuotas-pagos']);
  });

  it('solo Perfil agrega acciones a la cabecera (Dar de baja); Deuda no agrega ninguna', async () => {
    const perfil = await montar(JugadorPerfil);
    expect(texto(perfil.el.querySelector('.cj-acciones .profile-action'))).toBe('Dar de baja');

    const deuda = await montar(DeudaJugador);
    expect(deuda.el.querySelector('.cj-acciones')!.children.length).toBe(0);
  });
});
