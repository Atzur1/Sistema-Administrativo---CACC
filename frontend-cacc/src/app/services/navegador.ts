// Envoltura mínima de la navegación del navegador: los portales son sitios distintos, así que pasar de uno a otro es una
// navegación completa (no del router de Angular). Existe para poder probarla sin recargar la página de prueba.
export const navegador = {
  irA: (url: string): void => window.location.assign(url),
};
