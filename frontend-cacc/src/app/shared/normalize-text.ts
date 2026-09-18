// Lowercases and strips accents, so searching "sanchez" finds "Sánchez".
export function normalizeText(text: string): string {
    return text
        .toLowerCase()
        .normalize('NFD')
        .replace(/[\u0300-\u036f]/g, '');
}
