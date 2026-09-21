import { HttpResponse } from '@angular/common/http';

// Dispara la descarga de un blob que ya llegó completo del backend (HttpClient
// con responseType: 'blob'), sin abrir una pestaña nueva ni recargar la
// página — HU-021 pide que la descarga arranque sola, sin bloqueos.
export function triggerBlobDownload(response: HttpResponse<Blob>, fallbackName: string): void {
    const blob = response.body;
    if (!blob) {
        return;
    }

    // El nombre real lo pone el backend en Content-Disposition (requiere que el
    // CORS del backend lo exponga con WithExposedHeaders, si no el navegador se
    // lo esconde al JS aunque venga en la respuesta cruda). El valor de
    // ASP.NET Core no va entre comillas (filename=x.pdf; filename*=UTF-8''x.pdf),
    // así que hay que cortar en el próximo ";" — si no, el match se come todo
    // el resto del header hasta el final.
    const disposition = response.headers.get('Content-Disposition') ?? '';
    const match = /filename="([^"]+)"|filename=([^;]+)/.exec(disposition);
    const filename = (match?.[1] ?? match?.[2])?.trim() ?? fallbackName;

    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = filename;
    document.body.appendChild(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
}
