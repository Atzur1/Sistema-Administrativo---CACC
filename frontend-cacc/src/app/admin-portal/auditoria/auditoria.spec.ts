import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { Auditoria } from './auditoria';
import { AuthService } from '../../services/auth';

// HU-068 (devolución de QA): los enlaces de contraseña (TOKEN_ACCESO_CUENTA) tienen que poder verse en Auditoría.
// Antes "Sección" era texto libre que tenía que coincidir con el nombre interno de la tabla.

async function create() {
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
  http.expectOne((req) => req.url.endsWith('/auditoria/cambios')).flush({ page: 1, pageSize: 50, total: 0, items: [] });
  return { component: fixture.componentInstance, http };
}

const ENLACE = {
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
