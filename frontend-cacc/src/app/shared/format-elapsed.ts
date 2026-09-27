// Fraseo relativo por día ("Hoy", "Ayer", "Hace N días") a partir de una fecha ISO
// (yyyy-MM-dd, sin hora) — para datos que solo registran el día, no el momento exacto.
export function formatElapsedDay(fechaIso: string): string {
  const fecha = new Date(fechaIso);
  const hoy = new Date();
  const unDia = 24 * 60 * 60 * 1000;
  const diffDias = Math.round(
    (new Date(hoy.getFullYear(), hoy.getMonth(), hoy.getDate()).getTime() -
      new Date(fecha.getFullYear(), fecha.getMonth(), fecha.getDate()).getTime()) /
      unDia,
  );

  if (diffDias === 0) return 'Hoy';
  if (diffDias === 1) return 'Ayer';
  if (diffDias > 1 && diffDias < 30) return `Hace ${diffDias} días`;
  return fecha.toLocaleDateString('es-AR', { day: '2-digit', month: '2-digit', year: 'numeric' });
}

// Fraseo relativo por minuto/hora ("Hace 5 minutos", "Hace 2 horas") a partir de un
// timestamp real. Sin ese timestamp (pagos registrados antes de que existiera la
// columna que lo guarda) cae al fraseo por día de arriba, que es la precisión real
// que tienen esos datos.
export function formatElapsedPrecise(fechaHoraIso: string | null, fechaDiaIso: string): string {
  if (!fechaHoraIso) {
    return formatElapsedDay(fechaDiaIso);
  }

  const fecha = new Date(fechaHoraIso);
  const diffMin = Math.floor((Date.now() - fecha.getTime()) / 60000);

  if (diffMin < 1) return 'Recién';
  if (diffMin < 60) return `Hace ${diffMin} minuto${diffMin === 1 ? '' : 's'}`;

  const diffHoras = Math.floor(diffMin / 60);
  if (diffHoras < 24) return `Hace ${diffHoras} hora${diffHoras === 1 ? '' : 's'}`;

  return formatElapsedDay(fechaDiaIso);
}
