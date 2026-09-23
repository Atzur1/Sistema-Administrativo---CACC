// Base de conocimiento de "El DT Administrativo": diccionario de intenciones
// por palabras clave. No hay NLP real acá — es un match de substring sobre
// texto normalizado (sin acentos, en minúsculas), a propósito simple porque
// el alcance de esta primera versión es guiar al administrador con
// respuestas cortas y accesos directos, no sostener una conversación abierta.
import { normalizeText } from '../normalize-text';

export interface QuickAction {
    label: string;
    route: string;
    // Selector de un input a enfocar en la pantalla de destino, una vez que
    // la navegación termina (ej. el buscador de socios en Cuotas y Pagos).
    focusSelector?: string;
}

export interface BotIntent {
    id: string;
    keywords: string[];
    // Ícono corto para los chips de acceso rápido (menú de saludo, fallback
    // y temas relacionados) — puramente decorativo, "más vida" a los botones.
    emoji: string;
    title: string;
    steps: string[];
    action: QuickAction;
    relatedIds: string[];
}

export interface TopicChip {
    intentId: string;
    label: string;
}

export const BOT_KNOWLEDGE_BASE: BotIntent[] = [
    {
        id: 'pagos',
        emoji: '⚽',
        keywords: [
            'cargar pago',
            'registrar pago',
            'registrar cuota',
            'como cobro',
            'como pago',
            'cobrar',
            'cobro',
            'pago',
            'pagos',
        ],
        title: 'Registrar un pago',
        steps: [
            'Entrá a "Cuotas y pagos" desde el menú lateral.',
            'Buscá al socio por nombre o DNI en el panel "Registrar pago".',
            'Completá el monto y el método de pago, y confirmá.',
        ],
        action: {
            label: 'Ir a Cuotas y pagos',
            route: '/admin/portal/cuotas-pagos',
            focusSelector: '#player',
        },
        relatedIds: ['deudores', 'reportes'],
    },
    {
        id: 'deudores',
        emoji: '📋',
        keywords: [
            'ver deudores',
            'quien debe',
            'quienes deben',
            'morosos',
            'morosidad',
            'cuotas impagas',
            'deudores',
            'deuda',
            'deudor',
        ],
        title: 'Ver deudores y morosidad',
        steps: [
            'Entrá a "Deudas y Morosidad" desde el menú lateral.',
            'Filtrá por categoría deportiva si buscás un grupo puntual.',
            'Tocá un socio de la lista para ver el detalle de su deuda.',
        ],
        action: {
            label: 'Ir a Deudas y Morosidad',
            route: '/admin/portal/deudas-morosidad',
        },
        relatedIds: ['pagos', 'reportes'],
    },
    {
        id: 'jugadores',
        emoji: '🔍',
        keywords: [
            'buscar chico',
            'buscar jugador',
            'buscar socio',
            'encontrar socio',
            'encontrar jugador',
            'jugador',
            'socio',
        ],
        title: 'Buscar un jugador o socio',
        steps: [
            'Entrá a "Cuotas y pagos": ahí está el buscador principal de socios.',
            'Escribí el nombre o el DNI en el campo "Buscar por nombre o DNI".',
            'Elegí el socio de la lista de sugerencias para ver su perfil.',
        ],
        action: {
            label: 'Abrir buscador de socios',
            route: '/admin/portal/cuotas-pagos',
            focusSelector: '#player',
        },
        relatedIds: ['pagos', 'deudores'],
    },
    {
        id: 'reportes',
        emoji: '📊',
        keywords: [
            'descargar pdf',
            'descargar reporte',
            'exportar csv',
            'exportar pdf',
            'reporte mensual',
            'reporte',
            'reportes',
            'exportar',
        ],
        title: 'Descargar reportes',
        steps: [
            'Entrá a "Reportes" desde el menú lateral.',
            'Elegí la caja correspondiente (deudores, pagos, becas o aranceles).',
            'Tocá "Exportar" y elegí PDF o CSV.',
        ],
        action: {
            label: 'Ir a Reportes',
            route: '/admin/portal/reportes',
        },
        relatedIds: ['deudores', 'pagos'],
    },
    {
        id: 'aranceles',
        emoji: '⚙️',
        keywords: [
            'aumentar cuota',
            'modificar arancel',
            'actualizar arancel',
            'inscripcion',
            'arancel',
            'aranceles',
            'tarifa',
            'aumento',
        ],
        title: 'Modificar aranceles',
        steps: [
            'Entrá a "Actualización de Aranceles" desde el menú lateral.',
            'Elegí la categoría a modificar.',
            'Actualizá el valor y confirmá — se notifica automáticamente.',
        ],
        action: {
            label: 'Ir a Actualización de Aranceles',
            route: '/admin/portal/actualizacion-aranceles',
        },
        relatedIds: ['becas', 'reportes'],
    },
    {
        id: 'metricas',
        emoji: '📈',
        keywords: [
            'ver panel',
            'estadisticas',
            'ingresos del mes',
            'metricas',
            'indicadores',
            'resumen general',
            'panel',
        ],
        title: 'Ver métricas y estadísticas',
        steps: [
            'Entrá a "Resumen General" desde el menú lateral.',
            'Ahí vas a ver los indicadores clave del mes (cobros, morosidad, becados).',
            'Los gráficos se actualizan solos con los datos reales.',
        ],
        action: {
            label: 'Ir a Resumen General',
            route: '/admin/portal/resumen-general',
        },
        relatedIds: ['reportes', 'deudores'],
    },
    {
        id: 'becas',
        emoji: '🎓',
        keywords: ['beca', 'becado', 'becados', 'descuento', 'descuentos', 'becas'],
        title: 'Gestionar becas y descuentos',
        steps: [
            'Entrá a "Becados y descuentos" desde el menú lateral.',
            'Buscá al socio y asigná un descuento fijo o porcentual.',
            'Guardá — el sistema te va a confirmar con una notificación.',
        ],
        action: {
            label: 'Ir a Becados y descuentos',
            route: '/admin/portal/becados-descuentos',
        },
        relatedIds: ['pagos', 'aranceles'],
    },
];

// Los 5 trámites más comunes: se muestran como accesos rápidos cuando el
// bot no reconoce lo que escribió el administrador (o cuando saluda, o al
// abrir el chat por primera vez), para reorientarlo sin fricción en vez de
// dejarlo frente a un "no entendí" sin salida.
export const FALLBACK_INTENT_IDS = ['pagos', 'deudores', 'jugadores', 'reportes', 'aranceles'];

export const GREETING_TEXT =
    '¡Hola! Soy El DT Administrativo 📋⚽ Contame qué jugada necesitás hacer, o elegí uno de estos accesos rápidos:';

// Deja claro el límite del bot en vez de un genérico "no entendí": qué hace
// y qué no, para que el administrador no siga probando temas fuera de tema.
export const FALLBACK_TEXT =
    'Disculpá, no reconozco esa indicación. Como DT Administrativo, mi tarea es marcarte la cancha y guiarte en los procesos del club (pagos, deudores, reportes o tarifas). Solo puedo responder a esas tareas puntuales.';

// Se listan además de los botones de acceso rápido, como pista de qué
// palabras sueltas reconoce el bot si el administrador prefiere escribir en
// vez de tocar un botón.
export const SUGGESTED_KEYWORDS = ['pagos', 'deudores', 'reportes', 'socios', 'aranceles'];

export function findIntentById(id: string): BotIntent | undefined {
    return BOT_KNOWLEDGE_BASE.find((intent) => intent.id === id);
}

// Chips con emoji para el menú principal (saludo, fallback y primera
// apertura) — siempre los mismos 5 trámites, en el mismo orden.
export function fallbackTopics(): TopicChip[] {
    return FALLBACK_INTENT_IDS.map((id) => findIntentById(id))
        .filter((intent): intent is BotIntent => !!intent)
        .map((intent) => ({ intentId: intent.id, label: `${intent.emoji} ${intent.title}` }));
}

// Chips de "temas relacionados" que acompañan la respuesta a una intención
// puntual — mismo formato con emoji, para que se vean igual que el menú.
export function relatedTopics(intent: BotIntent): TopicChip[] {
    return intent.relatedIds
        .map((id) => findIntentById(id))
        .filter((related): related is BotIntent => !!related)
        .map((related) => ({ intentId: related.id, label: `${related.emoji} ${related.title}` }));
}

// "hola", "buenas", "buen día", "hey", etc. — sin distinguir mayúsculas ni
// tildes. \b para no disparar con palabras que solo contienen el saludo
// como substring (ej. que "buenas" no matchee dentro de otra palabra).
const GREETING_PATTERN = /\b(hola+s?|buenas|buen dia|buenos dias|buenas tardes|buenas noches|hey|que tal|holis)\b/;

export function isGreeting(text: string): boolean {
    return GREETING_PATTERN.test(normalizeText(text));
}

const CLEAR_COMMANDS = ['/clear', '/limpiar'];

// Comando explícito: coincidencia exacta (sin el "/" a mitad de frase no
// dispara, es una sintaxis de comando deliberada).
export function isClearCommand(text: string): boolean {
    return CLEAR_COMMANDS.includes(text.trim().toLowerCase());
}

// Lenguaje natural equivalente a /clear ("limpiar chat", "borrar
// conversación", "reiniciar", "empezar de nuevo"...) — acá sí es substring,
// igual que el resto del matching de intenciones, porque es una frase
// dicha con naturalidad y no una sintaxis de comando.
// Bare words ("limpiar", "borrar", "reiniciar") en vez de frases exactas:
// cubren variantes como "quiero borrar la conversación" o "borrar
// historial" sin tener que enumerar cada combinación con artículos de por
// medio — ninguna otra intención del bot usa estas palabras, así que no hay
// choque con otro tema.
const CLEAR_INTENT_KEYWORDS = ['limpiar', 'borrar', 'reiniciar', 'empezar de nuevo'];

export function isClearIntent(text: string): boolean {
    const normalized = normalizeText(text);
    if (!normalized.trim()) return false;
    return CLEAR_INTENT_KEYWORDS.some((keyword) => normalized.includes(keyword));
}

// Puntúa cada intención por cantidad de keywords que aparecen como substring
// del texto normalizado, y devuelve la de mayor puntaje. Sin coincidencias,
// null — el llamador decide mostrar el fallback.
export function matchIntent(query: string): BotIntent | null {
    const normalizedQuery = normalizeText(query);
    if (!normalizedQuery.trim()) return null;

    let best: BotIntent | null = null;
    let bestScore = 0;

    for (const intent of BOT_KNOWLEDGE_BASE) {
        const score = intent.keywords.filter((keyword) =>
            normalizedQuery.includes(normalizeText(keyword))
        ).length;

        if (score > bestScore) {
            bestScore = score;
            best = intent;
        }
    }

    return best;
}
