import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AuthService } from '../../services/auth';
import { DtAdminChat } from './dt-admin-chat';

// Cubre los dos comportamientos con estado nuevos de esta vuelta: el
// comando /clear y la persistencia de la conversación entre aperturas
// (sessionStorage). El matching de intenciones ya está cubierto en
// bot-knowledge-base.spec.ts.
describe('DtAdminChat', () => {
  let fixture: ComponentFixture<DtAdminChat>;
  let component: DtAdminChat;
  // Quién está logueado: el bot filtra lo que ofrece según el rol.
  let auth: { rol: number; email: string };

  beforeEach(async () => {
    sessionStorage.clear();
    auth = { rol: 1, email: 'super@cacc.test' };
    await TestBed.configureTestingModule({
      imports: [DtAdminChat],
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            isSuperAdmin: () => auth.rol === 1,
            getUsuario: () => ({ email: auth.email, rol: auth.rol }),
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(DtAdminChat);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  // Crea el componente como si recién entrara un usuario con ese rol.
  function mountAs(rol: number, email: string): DtAdminChat {
    auth.rol = rol;
    auth.email = email;
    const f = TestBed.createComponent(DtAdminChat);
    f.detectChanges();
    return f.componentInstance;
  }

  afterEach(() => {
    sessionStorage.clear();
  });

  it('al abrir por primera vez, muestra el saludo con los 6 accesos rápidos', () => {
    component.toggle();

    const messages = component.messages();
    expect(messages).toHaveLength(1);
    expect(messages[0].from).toBe('bot');
    expect(messages[0].chips).toHaveLength(6);
  });

  it('/clear reinicia el historial y deja solo el saludo predeterminado', () => {
    component.toggle();
    component.updateDraft('¿Cómo registro un pago?');
    component.send();
    expect(component.messages().length).toBeGreaterThan(1);

    component.updateDraft('/clear');
    component.send();

    const messages = component.messages();
    expect(messages).toHaveLength(1);
    expect(messages[0].from).toBe('bot');
    expect(messages[0].chips).toHaveLength(6);
  });

  it('/limpiar (con espacios y mayúsculas) también reinicia el chat', () => {
    component.toggle();
    component.updateDraft('buscar jugador');
    component.send();
    expect(component.messages().length).toBeGreaterThan(1);

    component.updateDraft('  /LIMPIAR  ');
    component.send();

    expect(component.messages()).toHaveLength(1);
  });

  it('reconoce "limpiar chat" en lenguaje natural, sin necesidad del comando /clear', () => {
    component.toggle();
    component.updateDraft('buscar jugador');
    component.send();
    expect(component.messages().length).toBeGreaterThan(1);

    component.updateDraft('quiero limpiar el chat');
    component.send();

    expect(component.messages()).toHaveLength(1);
  });

  it('una consulta sin match muestra el fallback con propósito del bot y palabras clave sugeridas', () => {
    component.toggle();
    component.updateDraft('qué clima hace hoy');
    component.send();

    const messages = component.messages();
    const lastBotMessage = messages[messages.length - 1];
    expect(lastBotMessage.text).toContain('DT Administrativo');
    expect(lastBotMessage.chips).toHaveLength(6);
    expect(lastBotMessage.suggestedKeywords).toEqual([
      'pagos',
      'deudores',
      'reportes',
      'socios',
      'aranceles',
      'auditoría',
    ]);
  });

  it('la pista de /clear solo se muestra mientras el input tiene foco', () => {
    expect(component.showClearHint()).toBe(false);
    component.onInputFocus();
    expect(component.showClearHint()).toBe(true);
    component.onInputBlur();
    expect(component.showClearHint()).toBe(false);
  });

  it('un saludo ("hola") siempre responde con el menú principal, no con matching de intención', () => {
    component.toggle();
    component.updateDraft('hola');
    component.send();

    const messages = component.messages();
    const lastBotMessage = messages[messages.length - 1];
    expect(lastBotMessage.from).toBe('bot');
    expect(lastBotMessage.chips).toHaveLength(6);
    // No es una respuesta de intención puntual (no tiene título/pasos).
    expect(lastBotMessage.title).toBeUndefined();
  });

  it('persiste la conversación en sessionStorage y la restaura en una nueva instancia', async () => {
    component.toggle();
    component.updateDraft('¿Cómo registro un pago?');
    component.send();
    // El effect() que guarda en sessionStorage corre en un microtask aparte
    // de la mutación del signal — hay que dejarlo asentar antes de leerlo.
    fixture.detectChanges();
    await fixture.whenStable();

    const messagesBefore = component.messages();
    expect(messagesBefore.length).toBeGreaterThan(1);

    // Simula cerrar y volver a abrir el drawer: un componente nuevo, como
    // pasaría si Angular lo recrea (o si se recarga la pestaña).
    const fixture2 = TestBed.createComponent(DtAdminChat);
    const component2 = fixture2.componentInstance;
    fixture2.detectChanges();

    expect(component2.messages()).toEqual(messagesBefore);
    expect(component2.isOpen()).toBe(true);
  });

  it('sin estado previo en sessionStorage, arranca cerrado y sin mensajes', () => {
    expect(component.isOpen()).toBe(false);
    expect(component.messages()).toHaveLength(0);
  });

  describe('rol Administrador (2)', () => {
    it('el saludo solo ofrece sus 5 accesos: nada de reportes ni aranceles', () => {
      const admin = mountAs(2, 'admin@cacc.test');
      admin.toggle();

      const labels = admin.messages()[0].chips!.map((c) => c.label).join(' | ');
      expect(admin.messages()[0].chips).toHaveLength(5);
      expect(labels).toContain('Cobrar una inscripción');
      expect(labels).toContain('becas');
      expect(labels).not.toContain('reportes');
      expect(labels).not.toContain('arancel');
    });

    it('una consulta de una sección exclusiva de SuperAdmin cae en el fallback, sin ofrecerla', () => {
      const admin = mountAs(2, 'admin@cacc.test');
      admin.toggle();
      admin.updateDraft('quiero programar un arancel');
      admin.send();

      const last = admin.messages()[admin.messages().length - 1];
      expect(last.title).toBeUndefined();
      expect(last.suggestedKeywords).toEqual(['pagos', 'inscripción', 'deudores', 'socios', 'becas']);
      expect(last.chips!.map((c) => c.intentId)).not.toContain('aranceles');
    });

    it('"exportar pdf" lo lleva a la exportación de Deudas y Morosidad', () => {
      const admin = mountAs(2, 'admin@cacc.test');
      admin.toggle();
      admin.updateDraft('exportar pdf');
      admin.send();

      const last = admin.messages()[admin.messages().length - 1];
      expect(last.action?.route).toBe('/admin/portal/deudas-morosidad');
    });

    it('no puede disparar una intención restringida tocando un chip viejo', () => {
      const admin = mountAs(2, 'admin@cacc.test');
      admin.toggle();
      const before = admin.messages().length;
      admin.askIntent('auditoria', 'Consultar la auditoría');
      expect(admin.messages()).toHaveLength(before);
    });

    it('no hereda la conversación de SuperAdmin guardada en la misma pestaña', async () => {
      component.toggle();
      component.updateDraft('auditoría');
      component.send();
      fixture.detectChanges();
      await fixture.whenStable();

      const admin = mountAs(2, 'admin@cacc.test');
      expect(admin.messages()).toHaveLength(0);
    });
  });

  it('SuperAdmin conserva todo: "auditoría" lo lleva a Auditoría', () => {
    component.toggle();
    component.updateDraft('auditoría');
    component.send();

    const last = component.messages()[component.messages().length - 1];
    expect(last.action?.route).toBe('/admin/portal/auditoria');
  });
});
