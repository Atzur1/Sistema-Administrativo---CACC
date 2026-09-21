import { Component, ElementRef, HostListener, NgZone, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpResponse } from '@angular/common/http';
import { forkJoin, Observable } from 'rxjs';
import { ReportesService } from '../../services/reportes';
import { PagosService } from '../../services/pagos';
import { DiscountService } from '../../services/discounts';
import { ArancelesService } from '../../services/aranceles';
import { NotificationService } from '../../shared/notifications/notification.service';
import { triggerBlobDownload } from '../../shared/download-file';
import { formatCompactCurrency } from '../../shared/format-currency';

type ReportCardId = 'deudas' | 'cuotas' | 'becados' | 'aranceles';
type ExportFormat = 'pdf' | 'csv';

interface CardStat {
    value: string;
    label: string;
}

interface ReportCard {
    id: ReportCardId;
    title: string;
    description: string;
    icon: ReportCardId;
    stats: CardStat[];
}

// Pantalla "Reportes": una caja por cada pantalla de Gestión que maneja datos,
// con su propio resumen (para que no sea una caja vacía) y su exportación a
// PDF o CSV — reemplaza los gráficos con datos hardcodeados que tenía antes
// (recaudación mensual, cuotas impagas, etc.), que nunca estuvieron
// conectados a datos reales.
@Component({
    selector: 'app-reportes',
    standalone: true,
    imports: [CommonModule],
    templateUrl: './reportes.html',
    styleUrl: './reportes.css',
})
export class Reportes implements OnInit {
    // Signal (no un array plano + ChangeDetectorRef): esta app usa FetchBackend
    // por default y sus respuestas no siempre disparan la detección de cambios
    // basada en zone.js (mismo problema ya resuelto en otras pantallas de acá).
    // Los signals notifican a Angular directo, sin depender de la zona.
    cards = signal<ReportCard[]>([
        {
            id: 'deudas',
            title: 'Deudas y Morosidad',
            description: 'Jugadores con cuotas pendientes: nombre, DNI, categoría, meses adeudados y monto.',
            icon: 'deudas',
            stats: [],
        },
        {
            id: 'cuotas',
            title: 'Cuotas y Pagos',
            description: 'Historial de pagos recibidos: jugador, método de pago, monto y fecha.',
            icon: 'cuotas',
            stats: [],
        },
        {
            id: 'becados',
            title: 'Becados y Descuentos',
            description: 'Todos los beneficios asignados, vigentes y no vigentes, con su período y valor.',
            icon: 'becados',
            stats: [],
        },
        {
            id: 'aranceles',
            title: 'Actualización de Aranceles',
            description: 'Historial de aranceles por género: vigentes, programados y anteriores.',
            icon: 'aranceles',
            stats: [],
        },
    ]);

    // Solo puede haber una exportación en curso a la vez: alcanza con saber
    // CUÁL caja la está generando para deshabilitar únicamente su botón.
    exportandoId = signal<ReportCardId | null>(null);
    exportMenuOpenId = signal<ReportCardId | null>(null);

    constructor(
        private reportesService: ReportesService,
        private pagosService: PagosService,
        private discountService: DiscountService,
        private arancelesService: ArancelesService,
        private notifications: NotificationService,
        private elementRef: ElementRef<HTMLElement>,
        private ngZone: NgZone,
    ) {}

    ngOnInit(): void {
        forkJoin({
            resumenPagos: this.pagosService.getResumen(),
            beneficios: this.discountService.getAllDiscounts(),
            resumenAranceles: this.arancelesService.getResumen(),
        }).subscribe({
            next: ({ resumenPagos, beneficios, resumenAranceles }) => {
                const vigentes = beneficios.filter((b) => b.isActive).length;

                this.setStats('deudas', [
                    { value: formatCompactCurrency(resumenPagos.deudaGlobalTotal), label: 'Deuda total' },
                    { value: String(resumenPagos.jugadoresMorosos), label: 'Deudores' },
                ]);
                this.setStats('cuotas', [
                    { value: formatCompactCurrency(resumenPagos.recaudadoAnioActual), label: `Recaudado ${new Date().getFullYear()}` },
                    { value: String(resumenPagos.pagosDelMes), label: 'Pagos del mes' },
                ]);
                this.setStats('becados', [
                    { value: String(vigentes), label: 'Vigentes' },
                    { value: String(beneficios.length - vigentes), label: 'No vigentes' },
                ]);
                this.setStats('aranceles', [
                    { value: resumenAranceles.arancelMasculinoVigente != null ? formatCompactCurrency(resumenAranceles.arancelMasculinoVigente) : '—', label: 'Arancel masc.' },
                    { value: resumenAranceles.arancelFemeninoVigente != null ? formatCompactCurrency(resumenAranceles.arancelFemeninoVigente) : '—', label: 'Arancel fem.' },
                ]);
            },
            error: () => {},
        });
    }

    private setStats(id: ReportCardId, stats: CardStat[]): void {
        this.cards.update((cards) => cards.map((card) => (card.id === id ? { ...card, stats } : card)));
    }

    toggleExportMenu(card: ReportCard): void {
        this.ngZone.run(() => {
            this.exportMenuOpenId.set(this.exportMenuOpenId() === card.id ? null : card.id);
        });
    }

    exportar(card: ReportCard, formato: ExportFormat): void {
        if (this.exportandoId() !== null) return;

        this.exportMenuOpenId.set(null);
        this.exportandoId.set(card.id);

        this.requestFor(card.id, formato).subscribe({
            next: (response) => {
                this.exportandoId.set(null);
                triggerBlobDownload(response, `reporte-${card.id}.${formato}`);
                this.notifications.notify(`Reporte de ${card.title} exportado correctamente.`, 'success');
            },
            error: () => {
                this.exportandoId.set(null);
                this.notifications.notify(`No se pudo exportar el reporte de ${card.title}. Intentá de nuevo.`, 'error');
            },
        });
    }

    // Cierra cualquier menú abierto si el click fue afuera de todas las cajas.
    @HostListener('document:click', ['$event'])
    onDocumentClick(event: MouseEvent): void {
        if (this.exportMenuOpenId() === null) return;

        const target = event.target as Node;
        const insideAnyMenu = Array.from(this.elementRef.nativeElement.querySelectorAll('.export-wrap'))
            .some((wrap) => wrap.contains(target));

        if (!insideAnyMenu) {
            this.ngZone.run(() => this.exportMenuOpenId.set(null));
        }
    }

    private requestFor(id: ReportCardId, formato: ExportFormat): Observable<HttpResponse<Blob>> {
        if (formato === 'pdf') {
            switch (id) {
                case 'deudas': return this.reportesService.exportarDeudoresPdf();
                case 'cuotas': return this.reportesService.exportarPagosRecientesPdf();
                case 'becados': return this.reportesService.exportarBecadosPdf();
                case 'aranceles': return this.reportesService.exportarArancelesPdf();
            }
        }

        switch (id) {
            case 'deudas': return this.reportesService.exportarDeudoresCsv();
            case 'cuotas': return this.reportesService.exportarPagosRecientesCsv();
            case 'becados': return this.reportesService.exportarBecadosCsv();
            case 'aranceles': return this.reportesService.exportarArancelesCsv();
        }
    }
}
