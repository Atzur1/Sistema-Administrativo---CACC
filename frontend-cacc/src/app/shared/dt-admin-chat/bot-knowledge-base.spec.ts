import {
  BOT_KNOWLEDGE_BASE,
  FALLBACK_INTENT_IDS,
  FALLBACK_TEXT,
  SUGGESTED_KEYWORDS,
  fallbackTopics,
  findIntentById,
  isClearCommand,
  isClearIntent,
  isGreeting,
  matchIntent,
  relatedTopics,
} from './bot-knowledge-base';

// Cubre el matching de intenciones de El DT Administrativo: los dos ejemplos que
// dispararon esta HU ("¿Cómo registro un pago?" y "Necesito ver los
// deudores y bajar el reporte"), más los casos de borde (vacío, sin match).
describe('matchIntent', () => {
  it('reconoce "¿Cómo registro un pago?" como la intención de pagos', () => {
    const intent = matchIntent('¿Cómo registro un pago?');
    expect(intent?.id).toBe('pagos');
    expect(intent?.action.route).toBe('/admin/portal/cuotas-pagos');
  });

  it('con varios temas en la misma frase, elige el que más palabras clave matchea', () => {
    // "deudores" matchea las keywords "deudor" y "deudores" (2 keywords);
    // "reporte" matchea solo "reporte" (1 keyword) — deudores debe ganar.
    const intent = matchIntent('Necesito ver los deudores y bajar el reporte');
    expect(intent?.id).toBe('deudores');
  });

  it('ignora acentos y mayúsculas', () => {
    expect(matchIntent('MOROSOS')?.id).toBe('deudores');
    expect(matchIntent('cómo cobro')?.id).toBe('pagos');
  });

  it('devuelve null para texto vacío o sin ninguna palabra clave reconocida', () => {
    expect(matchIntent('')).toBeNull();
    expect(matchIntent('   ')).toBeNull();
    expect(matchIntent('qué clima hace hoy')).toBeNull();
  });
});

describe('findIntentById', () => {
  it('encuentra cada intención declarada por su id', () => {
    for (const intent of BOT_KNOWLEDGE_BASE) {
      expect(findIntentById(intent.id)?.id).toBe(intent.id);
    }
  });

  it('devuelve undefined para un id inexistente', () => {
    expect(findIntentById('no-existe')).toBeUndefined();
  });
});

describe('FALLBACK_INTENT_IDS', () => {
  it('son 5 trámites frecuentes, todos con intención real asociada', () => {
    expect(FALLBACK_INTENT_IDS).toHaveLength(5);
    for (const id of FALLBACK_INTENT_IDS) {
      expect(findIntentById(id)).toBeDefined();
    }
  });
});

describe('isGreeting', () => {
  it('reconoce saludos comunes sin importar mayúsculas ni tildes', () => {
    expect(isGreeting('Hola')).toBe(true);
    expect(isGreeting('HOLA!')).toBe(true);
    expect(isGreeting('buenas')).toBe(true);
    expect(isGreeting('Buen día')).toBe(true);
    expect(isGreeting('hey, como estas')).toBe(true);
    expect(isGreeting('holis')).toBe(true);
  });

  it('no confunde una palabra que solo contiene el saludo como substring', () => {
    expect(isGreeting('Holanda')).toBe(false);
  });

  it('no reconoce texto sin saludo', () => {
    expect(isGreeting('quiero ver los deudores')).toBe(false);
  });
});

describe('isClearCommand', () => {
  it('reconoce /clear y /limpiar, sin importar mayúsculas o espacios', () => {
    expect(isClearCommand('/clear')).toBe(true);
    expect(isClearCommand('/CLEAR')).toBe(true);
    expect(isClearCommand('  /limpiar  ')).toBe(true);
  });

  it('no dispara con texto que solo contiene el comando como parte de la frase', () => {
    expect(isClearCommand('quiero hacer /clear del historial')).toBe(false);
    expect(isClearCommand('limpiar')).toBe(false);
  });
});

describe('isClearIntent', () => {
  it('reconoce frases en lenguaje natural para limpiar el chat', () => {
    expect(isClearIntent('limpiar chat')).toBe(true);
    expect(isClearIntent('quiero borrar la conversación')).toBe(true);
    expect(isClearIntent('reiniciar')).toBe(true);
    expect(isClearIntent('empezar de nuevo')).toBe(true);
    expect(isClearIntent('Limpiar')).toBe(true);
  });

  it('no reconoce texto sin relación a limpiar el chat', () => {
    expect(isClearIntent('quiero ver los deudores')).toBe(false);
    expect(isClearIntent('')).toBe(false);
  });
});

describe('fallback: texto y palabras clave sugeridas', () => {
  it('el mensaje de fallback explica el propósito y los límites del bot', () => {
    expect(FALLBACK_TEXT).toContain('DT Administrativo');
    expect(FALLBACK_TEXT).toContain('pagos');
    expect(FALLBACK_TEXT).toContain('deudores');
  });

  it('las palabras clave sugeridas son las 5 del requerimiento', () => {
    expect(SUGGESTED_KEYWORDS).toEqual(['pagos', 'deudores', 'reportes', 'socios', 'aranceles']);
  });
});

describe('fallbackTopics / relatedTopics', () => {
  it('cada chip del menú principal lleva el emoji de su intención', () => {
    const chips = fallbackTopics();
    expect(chips).toHaveLength(5);
    for (const chip of chips) {
      const intent = findIntentById(chip.intentId)!;
      expect(chip.label).toBe(`${intent.emoji} ${intent.title}`);
    }
  });

  it('los temas relacionados de una intención también llevan emoji', () => {
    const pagos = findIntentById('pagos')!;
    const chips = relatedTopics(pagos);
    expect(chips.length).toBeGreaterThan(0);
    for (const chip of chips) {
      const intent = findIntentById(chip.intentId)!;
      expect(chip.label).toBe(`${intent.emoji} ${intent.title}`);
    }
  });
});

describe('coherencia de la base de conocimiento', () => {
  it('cada relatedId apunta a una intención que existe', () => {
    for (const intent of BOT_KNOWLEDGE_BASE) {
      for (const relatedId of intent.relatedIds) {
        expect(findIntentById(relatedId), `relatedId "${relatedId}" en "${intent.id}"`).toBeDefined();
      }
    }
  });

  it('cada intención tiene como máximo 3 pasos, según el requerimiento', () => {
    for (const intent of BOT_KNOWLEDGE_BASE) {
      expect(intent.steps.length).toBeLessThanOrEqual(3);
    }
  });

  it('cada intención tiene un emoji definido', () => {
    for (const intent of BOT_KNOWLEDGE_BASE) {
      expect(intent.emoji.length).toBeGreaterThan(0);
    }
  });
});
