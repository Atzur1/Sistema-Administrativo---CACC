import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { DtAdminChat } from './dt-admin-chat';

// Cubre los dos comportamientos con estado nuevos de esta vuelta: el
// comando /clear y la persistencia de la conversación entre aperturas
// (sessionStorage). El matching de intenciones ya está cubierto en
// bot-knowledge-base.spec.ts.
describe('DtAdminChat', () => {
  let fixture: ComponentFixture<DtAdminChat>;
  let component: DtAdminChat;

  beforeEach(async () => {
    sessionStorage.clear();
    await TestBed.configureTestingModule({
      imports: [DtAdminChat],
      providers: [provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(DtAdminChat);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  afterEach(() => {
    sessionStorage.clear();
  });

  it('al abrir por primera vez, muestra el saludo con los 5 accesos rápidos', () => {
    component.toggle();

    const messages = component.messages();
    expect(messages).toHaveLength(1);
    expect(messages[0].from).toBe('bot');
    expect(messages[0].chips).toHaveLength(5);
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
    expect(messages[0].chips).toHaveLength(5);
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
    expect(lastBotMessage.chips).toHaveLength(5);
    expect(lastBotMessage.suggestedKeywords).toEqual([
      'pagos',
      'deudores',
      'reportes',
      'socios',
      'aranceles',
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
    expect(lastBotMessage.chips).toHaveLength(5);
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
});
