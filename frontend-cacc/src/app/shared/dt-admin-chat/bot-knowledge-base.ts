// Base de conocimiento de "El DT Administrativo": diccionario de intenciones
// por palabras clave. No hay NLP real acá — es un match de substring sobre
// texto normalizado (sin acentos, en minúsculas), a propósito simple porque
// el alcance de esta primera versión es guiar al administrador con
// respuestas cortas y accesos directos, no sostener una conversación abierta.
//
// El bot respeta los permisos del portal: el Administrador (rol 2) solo
// accede a Cuotas y Pagos, Deudas y Morosidad y Becados y Descuentos, así que
// las intenciones marcadas superAdminOnly no se le ofrecen ni se le responden.
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
    // Sección exclusiva de SuperAdmin: el Administrador (rol 2) ni la ve en
    // el menú ni la recibe como respuesta.
    superAdminOnly?: boolean;
    // Al revés: pantalla/atajo que solo tiene sentido para el Administrador.
    adminOnly?: boolean;
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
            'registrar cobro',
            'como cobro',
            'como pago',
            'cobrar',
            'cobro',
            'cuota',
            'cuotas',
            'pago',
            'pagos',
        ],
        title: 'Registrar un pago de cuota',
        steps: [
            'Entrá a "Cuotas y pagos" y dejá la pestaña "Pago de cuota" en el panel "Registrar cobro".',
            'Buscá al jugador por nombre o DNI y completá período, año, monto y método.',
            'Confirmá el cobro. También podés tocar un jugador de "Pendientes de cobro" para pagar desde su deuda.',
        ],
        action: {
            label: 'Ir a Cuotas y pagos',
            route: '/admin/portal/cuotas-pagos',
            focusSelector: '#player',
        },
        relatedIds: ['inscripcion', 'deudores'],
    },
    {
        id: 'inscripcion',
        emoji: '📝',
        keywords: [
            'cobrar inscripcion',
            'pagar inscripcion',
            'cobro de inscripcion',
            'inscripcion',
            'inscribir',
            'matricula',
        ],
        title: 'Cobrar una inscripción',
        steps: [
            'Entrá a "Cuotas y pagos" y elegí la pestaña "Inscripción".',
            'Buscá al jugador por nombre o DNI: vas a ver lo que ya abonó de la inscripción.',
            'Ingresá el monto a pagar ahora, elegí el método y confirmá.',
        ],
        action: {
            label: 'Ir a Cuotas y pagos',
            route: '/admin/portal/cuotas-pagos',
        },
        relatedIds: ['pagos', 'deudores'],
    },
    // Solo Administrador: es la única exportación de su alcance. SuperAdmin tiene
    // la sección Reportes, que cubre este caso y más.
    {
        id: 'exportar-deudores',
        emoji: '📥',
        adminOnly: true,
        keywords: [
            'exportar deudores',
            'descargar deudores',
            'lista de deudores',
            'descargar pdf',
            'descargar reporte',
            'exportar csv',
            'exportar pdf',
            'exportar',
            'descargar',
            'reporte',
        ],
        title: 'Descargar la lista de deudores',
        steps: [
            'Entrá a "Deudas y Morosidad" y aplicá los filtros que necesites (categoría, estado o búsqueda).',
            'Tocá "Exportar" en el panel "Lista de deudores".',
            'Elegí PDF o CSV.',
        ],
        action: {
            label: 'Ir a Deudas y Morosidad',
            route: '/admin/portal/deudas-morosidad',
        },
        relatedIds: ['deudores', 'pagos'],
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
            'inhabilitado',
            'inhabilitados',
            'deudores',
            'deuda',
            'deudor',
        ],
        title: 'Ver deudores y morosidad',
        steps: [
            'Entrá a "Deudas y Morosidad": ahí están la lista de deudores, los jugadores inhabilitados y la deuda por categoría.',
            'Filtrá por nombre o DNI, categoría o estado (también podés tocar una categoría del panel de deuda).',
            'Tocá un jugador para ver su deuda pendiente y registrar el pago de cada cuota.',
        ],
        action: {
            label: 'Ir a Deudas y Morosidad',
            route: '/admin/portal/deudas-morosidad',
        },
        relatedIds: ['pagos', 'exportar-deudores', 'becas'],
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
            'Entrá a "Cuotas y pagos": ahí está el buscador de jugadores.',
            'Escribí el nombre o el DNI en el campo "Buscar por nombre o DNI".',
            'Elegí al jugador de las sugerencias para cobrarle, o tocá su fila en "Pendientes de cobro" para ver su deuda.',
        ],
        action: {
            label: 'Abrir buscador de jugadores',
            route: '/admin/portal/cuotas-pagos',
            focusSelector: '#player',
        },
        relatedIds: ['pagos', 'deudores'],
    },
    {
        id: 'becas',
        emoji: '🎓',
        keywords: [
            'asignar beca',
            'quitar beca',
            'anular beca',
            'cancelar beca',
            'beca',
            'becado',
            'becados',
            'bonificacion',
            'bonificaciones',
            'descuento',
            'descuentos',
            'becas',
        ],
        title: 'Gestionar becas y descuentos',
        steps: [
            'Entrá a "Becados y descuentos" y, en "Asignar bonificación", buscá al jugador.',
            'Elegí el motivo, el tipo de valor (porcentaje o monto) y la vigencia, y guardá.',
            'En "Beneficios asignados" abrí un jugador para ver su historial, editar, cancelar o anular la bonificación.',
        ],
        action: {
            label: 'Ir a Becados y descuentos',
            route: '/admin/portal/becados-descuentos',
        },
        relatedIds: ['pagos', 'deudores'],
    },

    // ---- Solo SuperAdmin: secciones que el Administrador (rol 2) no tiene ----
    {
        id: 'reportes',
        emoji: '📊',
        superAdminOnly: true,
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
            'Elegí la tarjeta que necesitás: Deudas y Morosidad, Cuotas y Pagos, Becados y Descuentos o Aranceles.',
            'Tocá "Exportar" y elegí PDF o CSV.',
        ],
        action: {
            label: 'Ir a Reportes',
            route: '/admin/portal/reportes',
        },
        relatedIds: ['deudores', 'metricas'],
    },
    {
        id: 'aranceles',
        emoji: '⚙️',
        superAdminOnly: true,
        keywords: [
            'aumentar cuota',
            'modificar arancel',
            'actualizar arancel',
            'programar arancel',
            'arancel',
            'aranceles',
            'tarifa',
            'aumento',
        ],
        title: 'Programar un nuevo arancel',
        steps: [
            'Entrá a "Actualización de aranceles" y, en "Programar nuevo arancel", elegí a qué aplica: un género o una categoría (la categoría tiene prioridad sobre el género).',
            'Ingresá el nuevo monto y la fecha desde la que entra en vigencia.',
            'Confirmá: queda en "Historial y aranceles programados". Hay un solo arancel por mes para cada género o categoría: para cambiar uno, cancelalo antes con el botón "Cancelar" del historial.',
        ],
        action: {
            label: 'Ir a Actualización de aranceles',
            route: '/admin/portal/actualizacion-aranceles',
        },
        relatedIds: ['becas', 'reportes'],
    },
    {
        id: 'metricas',
        emoji: '📈',
        superAdminOnly: true,
        keywords: [
            'ver panel',
            'estadisticas',
            'ingresos del mes',
            'recaudacion',
            'metricas',
            'indicadores',
            'resumen general',
            'panel',
        ],
        title: 'Ver métricas y estadísticas',
        steps: [
            'Entrá a "Resumen General" desde el menú lateral.',
            'Ahí ves la recaudación mensual, el estado de habilitación y la evolución de cobertura de pagos.',
            'También encontrás los jugadores con mayor deuda pendiente.',
        ],
        action: {
            label: 'Ir a Resumen General',
            route: '/admin/portal/resumen-general',
        },
        relatedIds: ['reportes', 'deudores'],
    },
    {
        id: 'movimientos',
        emoji: '💸',
        superAdminOnly: true,
        keywords: [
            'pagos registrados',
            'actividad reciente',
            'ultimos pagos',
            'movimientos',
            'movimiento',
            'actividad',
        ],
        title: 'Revisar pagos y actividad reciente',
        steps: [
            'Entrá a "Actividad y Movimientos" desde el menú lateral.',
            'En "Pagos registrados" filtrá por jugador o estado.',
            'Mirá "Actividad reciente" para ver los últimos movimientos del club.',
        ],
        action: {
            label: 'Ir a Actividad y Movimientos',
            route: '/admin/portal/actividad-movimientos',
        },
        relatedIds: ['auditoria', 'metricas'],
    },
    {
        id: 'auditoria',
        emoji: '🕵️',
        superAdminOnly: true,
        keywords: [
            'auditoria',
            'auditar',
            'quien modifico',
            'quien cambio',
            'historial de cambios',
            'registro de cambios',
        ],
        title: 'Consultar la auditoría',
        steps: [
            'Entrá a "Auditoría" desde el menú lateral.',
            'Filtrá por persona, sección, acción o fechas y tocá "Buscar".',
            'Abrí un registro para ver el detalle y comparar el "Antes" y el "Después".',
        ],
        action: {
            label: 'Ir a Auditoría',
            route: '/admin/portal/auditoria',
        },
        relatedIds: ['movimientos', 'usuarios'],
    },
    {
        id: 'usuarios',
        emoji: '🔐',
        superAdminOnly: true,
        keywords: [
            'usuarios y permisos',
            'dar acceso',
            'quitar acceso',
            'habilitar acceso',
            'deshabilitar',
            'reactivar',
            'permisos',
            'permiso',
            'usuarios',
            'usuario',
            'acceso',
            'roles',
        ],
        title: 'Habilitar o quitar accesos al portal',
        steps: [
            'Entrá a "Usuarios y permisos" desde el menú lateral.',
            'Para dar acceso: en "Candidatos para habilitar" tocá "Habilitar" y elegí el rol (SuperAdmin o Administrador).',
            'Para quitarlo, tocá "Deshabilitar" en "Cuentas habilitadas"; desde "Cuentas deshabilitadas" podés reactivarlo.',
        ],
        action: {
            label: 'Ir a Usuarios y permisos',
            route: '/admin/portal/usuarios',
        },
        relatedIds: ['auditoria', 'movimientos'],
    },
];

// Trámites que se muestran como accesos rápidos cuando el bot no reconoce lo
// que escribió el administrador (o cuando saluda, o al abrir el chat por
// primera vez), para reorientarlo sin fricción en vez de dejarlo frente a un
// "no entendí" sin salida. Uno por rol: cada uno ve solo lo que puede hacer.
export const FALLBACK_INTENT_IDS = ['pagos', 'deudores', 'jugadores', 'reportes', 'aranceles', 'auditoria'];
export const FALLBACK_INTENT_IDS_ADMIN = ['pagos', 'inscripcion', 'deudores', 'jugadores', 'becas'];

export const GREETING_TEXT =
    '¡Hola! Soy El DT Administrativo 📋⚽ Contame qué jugada necesitás hacer, o elegí uno de estos accesos rápidos:';
export const GREETING_TEXT_ADMIN =
    '¡Hola! Soy El DT Administrativo 📋⚽ Te guío en lo que podés hacer desde tu rol: cobros, deudas y becas. Contame qué necesitás o elegí un acceso rápido:';

// Deja claro el límite del bot en vez de un genérico "no entendí": qué hace
// y qué no, para que el administrador no siga probando temas fuera de tema.
export const FALLBACK_TEXT =
    'Disculpá, no reconozco esa indicación. Como DT Administrativo, mi tarea es marcarte la cancha y guiarte en los procesos del club (pagos, deudores, reportes o tarifas). Solo puedo responder a esas tareas puntuales.';
export const FALLBACK_TEXT_ADMIN =
    'Disculpá, no reconozco esa indicación. Como DT Administrativo, puedo guiarte en lo que tenés disponible: cobrar cuotas e inscripciones, ver deudores y gestionar becas y descuentos. Solo puedo responder a esas tareas puntuales.';

// Se listan además de los botones de acceso rápido, como pista de qué
// palabras sueltas reconoce el bot si el administrador prefiere escribir en
// vez de tocar un botón.
export const SUGGESTED_KEYWORDS = ['pagos', 'deudores', 'reportes', 'socios', 'aranceles', 'auditoría'];
export const SUGGESTED_KEYWORDS_ADMIN = ['pagos', 'inscripción', 'deudores', 'socios', 'becas'];

export function greetingText(isSuperAdmin: boolean): string {
    return isSuperAdmin ? GREETING_TEXT : GREETING_TEXT_ADMIN;
}

export function fallbackText(isSuperAdmin: boolean): string {
    return isSuperAdmin ? FALLBACK_TEXT : FALLBACK_TEXT_ADMIN;
}

export function suggestedKeywords(isSuperAdmin: boolean): string[] {
    return isSuperAdmin ? SUGGESTED_KEYWORDS : SUGGESTED_KEYWORDS_ADMIN;
}

// Intenciones que el rol puede usar: el Administrador (rol 2) no ve ni puede
// disparar nada de lo marcado superAdminOnly, y SuperAdmin no recibe lo
// marcado adminOnly.
export function intentsFor(isSuperAdmin: boolean): BotIntent[] {
    return BOT_KNOWLEDGE_BASE.filter((intent) =>
        isSuperAdmin ? !intent.adminOnly : !intent.superAdminOnly
    );
}

// Sin rol busca en todo el catálogo; con rol, solo entre lo que ese rol puede usar.
export function findIntentById(id: string, isSuperAdmin?: boolean): BotIntent | undefined {
    const pool = isSuperAdmin === undefined ? BOT_KNOWLEDGE_BASE : intentsFor(isSuperAdmin);
    return pool.find((intent) => intent.id === id);
}

// Chips con emoji para el menú principal (saludo, fallback y primera
// apertura) — siempre los mismos trámites del rol, en el mismo orden.
export function fallbackTopics(isSuperAdmin = true): TopicChip[] {
    const ids = isSuperAdmin ? FALLBACK_INTENT_IDS : FALLBACK_INTENT_IDS_ADMIN;
    return ids
        .map((id) => findIntentById(id, isSuperAdmin))
        .filter((intent): intent is BotIntent => !!intent)
        .map((intent) => ({ intentId: intent.id, label: `${intent.emoji} ${intent.title}` }));
}

// Chips de "temas relacionados" que acompañan la respuesta a una intención
// puntual — mismo formato con emoji, para que se vean igual que el menú.
export function relatedTopics(intent: BotIntent, isSuperAdmin = true): TopicChip[] {
    return intent.relatedIds
        .map((id) => findIntentById(id, isSuperAdmin))
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

// Puntúa cada intención del rol por cantidad de keywords que aparecen como
// substring del texto normalizado, y devuelve la de mayor puntaje. Sin
// coincidencias, null — el llamador decide mostrar el fallback.
export function matchIntent(query: string, isSuperAdmin = true): BotIntent | null {
    const normalizedQuery = normalizeText(query);
    if (!normalizedQuery.trim()) return null;

    let best: BotIntent | null = null;
    let bestScore = 0;

    for (const intent of intentsFor(isSuperAdmin)) {
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
