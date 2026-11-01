// HU-098 (corrección de QA): pruebas de accesibilidad del Portal Administrativo sobre los estilos tal como están en
// los archivos: foco visible, tamaño mínimo de texto y contraste de los colores de texto. Leen los .css del portal
// (admin-portal y shared) y styles.css, así cualquier regla nueva que rompa un criterio hace fallar la prueba.

interface ArchivoCss {
  ruta: string;
  texto: string;
}

// Lectura de los archivos con node:fs (las pruebas corren en Node). La importación es dinámica para no depender
// de los tipos de Node en el proyecto.
async function leerEstilos(): Promise<{ global: string; portal: ArchivoCss[] }> {
  const modulo = 'node:fs';
  const fs = await import(/* @vite-ignore */ modulo);
  const raiz = 'src/app';
  const recorrer = (dir: string): string[] =>
    (fs.readdirSync(dir, { withFileTypes: true }) as { name: string; isDirectory(): boolean }[]).flatMap((entrada) =>
      entrada.isDirectory() ? recorrer(`${dir}/${entrada.name}`) : entrada.name.endsWith('.css') ? [`${dir}/${entrada.name}`] : [],
    );
  const portal = [...recorrer(`${raiz}/admin-portal`), ...recorrer(`${raiz}/shared`)].map((ruta) => ({
    ruta,
    texto: fs.readFileSync(ruta, 'utf8') as string,
  }));
  return { global: fs.readFileSync('src/styles.css', 'utf8') as string, portal };
}

const sinComentarios = (css: string) => css.replace(/\/\*[\s\S]*?\*\//g, '');

// Valor de una variable de :root en styles.css.
function token(global: string, nombre: string): string {
  const m = sinComentarios(global).match(new RegExp(`${nombre}\\s*:\\s*([^;]+);`));
  if (!m) throw new Error(`No está la variable ${nombre}`);
  return m[1].trim();
}

// Contraste WCAG entre dos colores #rrggbb.
function contraste(a: string, b: string): number {
  const luminancia = (hex: string) => {
    const [r, g, bl] = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255).map((c) =>
      c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4),
    );
    return 0.2126 * r + 0.7152 * g + 0.0722 * bl;
  };
  const [claro, oscuro] = [luminancia(a), luminancia(b)].sort((x, y) => y - x);
  return (claro + 0.05) / (oscuro + 0.05);
}

describe('Accesibilidad del portal (HU-098)', () => {
  let estilos: Awaited<ReturnType<typeof leerEstilos>>;
  beforeAll(async () => {
    estilos = await leerEstilos();
  });

  it('lee los estilos del portal', () => {
    expect(estilos.portal.length).toBeGreaterThan(20);
  });

  describe('foco visible', () => {
    it('hay una regla global que dibuja el anillo de foco en el portal y en sus menús y calendarios', () => {
      const global = sinComentarios(estilos.global);
      expect(global).toMatch(/app-admin-portal :is\(a, button, input, select, textarea, summary, \[tabindex\]\):focus-visible/);
      expect(global).toMatch(/\.cs-menu :focus-visible/);
      expect(global).toMatch(/\.dp-panel :focus-visible/);
      expect(global).toMatch(/outline:\s*2px solid var\(--action-accent\)/);
    });

    it('ninguna pantalla ni componente del portal esconde el foco con outline: none', () => {
      const conOutlineNone = estilos.portal
        .filter((archivo) => /outline\s*:\s*(none|0)\s*[;}]/.test(sinComentarios(archivo.texto)))
        .map((archivo) => archivo.ruta);
      expect(conOutlineNone).toEqual([]);
    });
  });

  describe('tamaño mínimo de texto', () => {
    it('dentro del portal los tamaños chicos de la escala valen 12 px', () => {
      const global = sinComentarios(estilos.global);
      const bloque = global.match(/app-admin-portal,\s*\.cs-menu,\s*\.dp-panel,\s*\.help-tooltip\s*\{([^}]*)\}/);
      expect(bloque).not.toBeNull();
      expect(bloque![1]).toMatch(/--font-xs:\s*var\(--font-base\)/);
      expect(bloque![1]).toMatch(/--font-sm:\s*var\(--font-base\)/);
      expect(token(estilos.global, '--font-base')).toBe('12px');
    });

    it('ningún texto del portal tiene un tamaño escrito a mano por debajo de 12 px', () => {
      const chicos = estilos.portal.flatMap((archivo) =>
        [...sinComentarios(archivo.texto).matchAll(/font-size\s*:\s*([\d.]+)(px|rem|em)/g)]
          .filter(([, n, unidad]) => (unidad === 'px' ? Number(n) : Number(n) * 16) < 12)
          .map(([declaracion]) => `${archivo.ruta}: ${declaracion}`),
      );
      expect(chicos).toEqual([]);
    });
  });

  describe('contraste de texto (4.5:1)', () => {
    it('ningún texto del portal usa los colores claros que no llegan a 4.5:1', () => {
      const claros = /^\s*color\s*:\s*(var\(--color-(brand|brand-light|brand-strong|info|accent)\)|#00a651|#00b355|#3b82f6|#8b5cf6|#6b7280)\s*;/gm;
      const encontrados = estilos.portal.flatMap((archivo) =>
        [...sinComentarios(archivo.texto).matchAll(claros)].map(([declaracion]) => `${archivo.ruta}: ${declaracion.trim()}`),
      );
      expect(encontrados).toEqual([]);
    });

    it('los colores de texto del portal llegan a 4.5:1 sobre blanco y sobre sus fondos', () => {
      const t = (nombre: string) => token(estilos.global, nombre);
      const blanco = '#ffffff';
      const pares: [string, string, string][] = [
        ['verde oscuro / blanco', t('--color-brand-deep'), blanco],
        ['verde oscuro / fondo verde (pill Vigente)', t('--color-brand-deep'), t('--color-brand-tint')],
        ['verde oscuro / fondo de página', t('--color-brand-deep'), t('--color-bg-page')],
        ['azul de avisos / blanco', t('--aviso-acento'), blanco],
        ['azul de avisos / fondo azul (pill Masculino)', t('--aviso-acento'), t('--color-info-bg')],
        ['violeta de texto / blanco', t('--color-accent-text'), blanco],
        ['violeta de texto / fondo violeta (pill Femenino)', t('--color-accent-text'), t('--color-accent-bg')],
        ['gris del portal / blanco', '#5f6672', blanco],
        ['gris del portal / fondo de página', '#5f6672', t('--color-bg-page')],
        ['gris secundario / fondo de página', t('--color-text-muted'), t('--color-bg-page')],
      ];
      const fallan = pares
        .map(([nombre, texto, fondo]) => ({ nombre, valor: contraste(texto, fondo) }))
        .filter((p) => p.valor < 4.5)
        .map((p) => `${p.nombre}: ${p.valor.toFixed(2)}`);
      expect(fallan).toEqual([]);
    });

    it('el gris de los textos secundarios dentro del portal es el que cumple', () => {
      const global = sinComentarios(estilos.global);
      expect(global).toMatch(/app-admin-portal,\s*\.cs-menu,\s*\.dp-panel,\s*\.help-tooltip\s*\{[^}]*--color-text-subtle:\s*#5f6672/);
    });

    it('la pill de estado "info" usa el azul de texto', () => {
      expect(sinComentarios(estilos.global)).toMatch(/\.tono-info\s*\{[^}]*color:\s*var\(--aviso-acento\)/);
    });
  });

  describe('movimiento reducido', () => {
    it('hay una regla global que apaga animaciones y transiciones', () => {
      expect(sinComentarios(estilos.global)).toMatch(/@media \(prefers-reduced-motion: reduce\)/);
    });
  });
});
