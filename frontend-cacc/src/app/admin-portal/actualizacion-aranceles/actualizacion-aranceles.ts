import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { AbstractControl, FormBuilder, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { BaseChartDirective } from 'ng2-charts';
import { Chart, ChartConfiguration, registerables } from 'chart.js';
import { ArancelesService } from '../../services/aranceles';
import { PagosService } from '../../services/pagos';
import { EnrollmentFeeService } from '../../services/enrollment-fees';
import {
    EnrollmentFeeModel,
    EnrollmentFeeGender,
    enrollmentFeeLabel,
    enrollmentFeeStatusLabel,
    enrollmentFeeStatusClass,
} from '../../models/EnrollmentFeeModel';
import { CustomSelect, CustomSelectOption } from '../../shared/custom-select/custom-select';
import { HelpHint } from '../../shared/help-hint/help-hint';
import { CustomDatepicker } from '../../shared/custom-datepicker/custom-datepicker';
import { Ventana } from '../../shared/ventana/ventana';
import { NotificationService } from '../../shared/notifications/notification.service';
import { TariffChangeRequest, TariffChangeType, TariffHistoryItemModel, TariffImpactModel } from '../../models/TariffModel';
import { Ventana } from '../../shared/ventana/ventana';
import { IncreaseMode, needsCurrentTariff, sanitizePercentage } from './tariff-increase';
import { SECCIONES } from '../secciones';

Chart.register(...registerables);

// One row in the "Historial y aranceles programados" table
interface FeeRow {
    id: number;
    // Same value as the form's target ('genero:Masculino', 'categoria:3', 'inscripcion:Femenino'), so the
    // form can look up the current tariff of the destination it is about to change
    target: string;
    // 'male' renders the "Masculino" pill, 'female' the "Femenino" one, 'enrollment' the "Inscripción" one
    // and 'category' the pill of a fee set for one category (it overrides the gender fee)
    category: 'male' | 'female' | 'enrollment' | 'category';
    categoryLabel: string;
    amount: string;
    // Stored as ISO so the dates compare and sort directly
    validFrom: string;
    // Empty while the fee has no replacement scheduled after it
    validTo: string;
    estado: 'Vigente' | 'Programado' | 'Anterior';
    responsable: string;
    // Solo los aranceles de cuota del mes en curso en adelante se pueden cancelar
    cancelable: boolean;
}

const CURRENCY_FULL = new Intl.NumberFormat('es-AR', {
    style: 'currency',
    currency: 'ARS',
    maximumFractionDigits: 0,
});

// HU-106: los montos del resumen de impacto se muestran tal como los calculó el backend (con centavos si los hay).
const CURRENCY_IMPACTO = new Intl.NumberFormat('es-AR', {
    style: 'currency',
    currency: 'ARS',
    minimumFractionDigits: 0,
    maximumFractionDigits: 2,
});

// Modo del formulario (HU-069) -> tipo de cambio que entiende el backend.
const TIPO_DE_CAMBIO: Record<IncreaseMode, TariffChangeType> = {
    absolute: 'absoluto',
    percentage: 'porcentaje',
    fixed: 'montoFijo',
};

@Component({
    selector: 'app-actualizacion-aranceles',
    standalone: true,
    imports: [CommonModule, ReactiveFormsModule, BaseChartDirective, CustomSelect, CustomDatepicker, HelpHint, Ventana],
    templateUrl: './actualizacion-aranceles.html',
    styleUrl: './actualizacion-aranceles.css',
})
export class ActualizacionAranceles implements OnInit {
  // Nombre oficial de la sección (HU-103): el mismo del menú lateral y de la barra superior.
  readonly tituloPantalla = SECCIONES['actualizacion-aranceles'];


    headerMetrics = [
        { value: '—', label: 'Arancel masculino' },
        { value: '—', label: 'Arancel femenino' },
        { value: '—', label: 'Próximo cambio' },
    ];

    feeForm: FormGroup;

    // What the fee applies to: a gender, the enrollment fee, or one category. A category fee
    // wins over the gender fee for the players of that category, whatever their gender.
    // Values: 'genero:Masculino' | 'genero:Femenino' | 'inscripcion:Masculino' | 'inscripcion:Femenino' | 'categoria:<id>'
    targets: CustomSelectOption[] = [
        { value: 'genero:Masculino', label: 'Masculino' },
        { value: 'genero:Femenino', label: 'Femenino' },
        { value: 'inscripcion:Masculino', label: 'Inscripción masculina' },
        { value: 'inscripcion:Femenino', label: 'Inscripción femenina' },
    ];

    // La cuota mensual y la inscripción son dos cosas distintas con sus propios valores, formulario e historial,
    // aunque comparten el tablero. La inscripción se cobra una vez por año a cada jugador (su año empieza el día de su alta).
    vista: 'cuota' | 'inscripcion' = 'cuota';

    enviando = false;

    // Every fee ever set, newest first: past periods, the current one and the
    // changes already scheduled ahead
    feeRows: FeeRow[] = [];

    // Lo que se ve en el input de Nuevo monto ("1.000"). El control del form (feeForm.get('amount'))
    // guarda el número limpio sin puntos ("1000"), que es lo que se valida y se manda al backend.
    montoDisplay = '';

    // HU-069: cómo se interpreta lo que se escribe en el campo del monto. Solo la cuota mensual ofrece los tres;
    // la inscripción sigue siendo siempre un monto absoluto.
    increaseMode: IncreaseMode = 'absolute';

    readonly increaseModes: { value: IncreaseMode; label: string }[] = [
        { value: 'absolute', label: 'Monto absoluto' },
        { value: 'percentage', label: 'Porcentaje (+%)' },
        { value: 'fixed', label: 'Monto fijo a sumar (+$)' },
    ];

    private monthLabels = [
        'Ene', 'Feb', 'Mar', 'Abr', 'May', 'Jun',
        'Jul', 'Ago', 'Sep', 'Oct', 'Nov', 'Dic',
    ];

    // ===== FEE EVOLUTION CHART =====
    // Both series are read off the history above, so the chart can never drift
    // away from the table
    feeChartData: ChartConfiguration<'line'>['data'] = { labels: [], datasets: [] };

    feeChartOptions: ChartConfiguration<'line'>['options'] = {
        responsive: true,
        maintainAspectRatio: false,
        interaction: { mode: 'index', intersect: false },
        // Points reveal from left to right as each one is delayed by its index
        animation: {
            duration: 800,
            easing: 'easeOutQuart',
            delay: (context) =>
                context.type === 'data' && context.mode === 'default'
                    ? context.dataIndex * 90
                    : 0,
        },
        layout: {
            padding: { top: 4, right: 4 },
        },
        plugins: {
            // Two series here, so unlike the resumen-general chart this one
            // needs a legend to tell them apart
            legend: {
                display: true,
                position: 'bottom',
                labels: {
                    color: '#555a55',
                    font: { size: 11 },
                    boxWidth: 10,
                    boxHeight: 10,
                    usePointStyle: true,
                    pointStyle: 'circle',
                    padding: 14,
                },
            },
            tooltip: {
                backgroundColor: '#000000',
                titleColor: '#ffffff',
                bodyColor: '#ffffff',
                borderWidth: 0,
                callbacks: {
                    label: (context) =>
                        ` ${context.dataset.label}: ${this.formatAmount(Number(context.parsed.y))}`,
                },
            },
        },
        scales: {
            x: {
                grid: { display: false },
                border: { display: false },
                ticks: {
                    color: '#555a55',
                    font: { size: 10 },
                },
            },
            y: {
                grid: { color: '#f1f5f1' },
                border: { display: false },
                ticks: {
                    color: '#555a55',
                    font: { size: 10 },
                    callback: (value) => this.formatAmount(Number(value)),
                },
            },
        },
    };

    constructor(
        private fb: FormBuilder,
        private arancelesService: ArancelesService,
        private enrollmentFeeService: EnrollmentFeeService,
        private pagosService: PagosService,
        private cdr: ChangeDetectorRef,
        private notifications: NotificationService
    ) {
        this.feeForm = this.fb.group({
            target: ['', [Validators.required]],
            amount: ['', [Validators.required, this.montoMinimo]],
            validFrom: ['', [Validators.required, this.noAnteriorAHoy]],
        });
        // El mínimo del monto depende del destino (la inscripción admite $0): se revalida al cambiarlo.
        this.feeForm.get('target')?.valueChanges.subscribe(() => this.feeForm.get('amount')?.updateValueAndValidity());
        // HU-106: un resumen de impacto vale solo para lo que se simuló. Si cambia cualquier dato del cambio, se descarta
        // y hay que volver a revisar el impacto.
        this.feeForm.valueChanges.subscribe(() => {
            if (this.impacto && !this.confirmandoImpacto) {
                this.descartarImpacto();
                this.cdr.detectChanges();
            }
        });
    }

    // Regla del club: no se programan aranceles (mensuales ni de inscripción) con fecha pasada. El servidor
    // lo rechaza igual; acá el calendario no deja elegirla y el botón queda deshabilitado.
    // Fecha local del navegador en ISO (aaaa-mm-dd), que se compara como texto sin problemas de zona horaria.
    get hoy(): string {
        const ahora = new Date();
        return `${ahora.getFullYear()}-${`${ahora.getMonth() + 1}`.padStart(2, '0')}-${`${ahora.getDate()}`.padStart(2, '0')}`;
    }

    get fechaPasada(): boolean {
        return !!this.feeForm?.get('validFrom')?.hasError('fechaPasada');
    }

    // La cuota y el arancel de una categoría tienen que ser mayores a $0. La inscripción admite $0: significa que ese
    // género no paga inscripción desde esa fecha (hoy, las mujeres).
    // En porcentaje o monto fijo a sumar, lo escrito tiene que ser mayor a 0 y el destino necesita un arancel vigente
    // sobre el que calcular el aumento.
    private montoMinimo = (control: AbstractControl): ValidationErrors | null => {
        if (control.value === '' || control.value == null) {
            return null; // De eso se ocupa Validators.required
        }
        const destino = this.feeForm?.get('target')?.value;
        const valor = Number(control.value);
        if (needsCurrentTariff(this.increaseMode)) {
            if (destino && this.arancelVigente(destino) == null) {
                return { sinArancelVigente: true };
            }
            return valor > 0 ? null : { min: { min: 0, actual: valor } };
        }
        const minimo = typeof destino === 'string' && destino.startsWith('inscripcion:') ? 0 : 1;
        return valor < minimo ? { min: { min: minimo, actual: valor } } : null;
    };

    // Monto del arancel que rige hoy para un destino, o null si nunca tuvo uno (o si ya no hay ninguno vigente).
    arancelVigente(target: string): number | null {
        const fila = this.feeRows.find(r => r.target === target && r.estado === 'Vigente');
        return fila ? this.parseAmount(fila.amount) : null;
    }

    get currentTariff(): number | null {
        const target = this.feeForm.get('target')?.value;
        return target ? this.arancelVigente(target) : null;
    }

    // ===== HU-106: resumen de impacto antes de confirmar =====
    // El monto nuevo, los jugadores y la diferencia los calcula el backend (POST /aranceles/simular), con la misma
    // lógica que después graba: el portal no hace cuentas de dinero, solo muestra el resumen y pide la confirmación.

    // Resumen abierto y el cambio exacto que se simuló (lo que se confirma es esto, no lo que diga el form después).
    impacto: TariffImpactModel | null = null;
    private cambioSimulado: TariffChangeRequest | null = null;
    simulandoImpacto = false;
    confirmandoImpacto = false;
    // Lo que se escribe para confirmar un cambio que alcanza a más de una categoría.
    confirmacionEscrita = '';
    errorImpacto: string | null = null;
    // Aviso cuando el backend rechazó la confirmación porque el impacto cambió (409) y se muestra el nuevo.
    avisoImpactoActualizado: string | null = null;

    // El cambio que arma el form: destino, tipo y valor tal como se escribieron (sin calcular nada acá).
    private cambioDelFormulario(): TariffChangeRequest | null {
        const { target, amount, validFrom } = this.feeForm.value;
        if (typeof target !== 'string' || amount === '' || amount == null || !validFrom) {
            return null;
        }
        const destino = target.startsWith('categoria:')
            ? { idCategoria: Number(target.slice('categoria:'.length)) }
            : { genero: target.slice('genero:'.length) };
        return { ...destino, tipo: TIPO_DE_CAMBIO[this.increaseMode], valor: Number(amount), vigenteDesde: validFrom };
    }

    // Confirmar se habilita con el número exacto de jugadores (sin espacios ni otro texto) cuando el cambio alcanza a
    // más de una categoría; con una sola alcanza con el botón.
    get confirmacionValida(): boolean {
        const impacto = this.impacto;
        if (!impacto || this.confirmandoImpacto) {
            return false;
        }
        return !impacto.requiereConfirmacionReforzada || this.confirmacionEscrita === String(impacto.jugadores);
    }

    // "noviembre de 2026": el mes cuya cuota se usa para el impacto mensual.
    mesTexto(isoDate: string): string {
        const [year, month] = isoDate.slice(0, 10).split('-').map(Number);
        return new Date(year, month - 1, 1).toLocaleDateString('es-AR', { month: 'long', year: 'numeric' });
    }

    // "Nuevo arancel del género Masculino" / "Nuevo arancel de la categoría Sub 15".
    destinoImpacto(impacto: TariffImpactModel): string {
        return impacto.genero ? `género ${impacto.genero}` : `categoría ${impacto.nombreCategoria ?? impacto.idCategoria}`;
    }

    // Qué escribió el SuperAdmin, dicho en palabras: el tipo no depende de un color ni de un ícono.
    cambioImpactoTexto(impacto: TariffImpactModel): string {
        switch (impacto.tipo) {
            case 'porcentaje': return `Aumento del ${impacto.valor.toLocaleString('es-AR')} % sobre el arancel vigente`;
            case 'montoFijo': return `Suma de ${this.moneda(impacto.valor)} al arancel vigente`;
            default: return `Monto fijo de ${this.moneda(impacto.valor)}`;
        }
    }

    moneda(valor: number | null): string {
        return valor == null ? 'Sin arancel' : CURRENCY_IMPACTO.format(valor);
    }

    // Diferencia con signo y en palabras ("+$8.500 (aumento)", "−$2.000 (baja)"): no se indica solo con color.
    diferenciaTexto(valor: number): string {
        if (valor === 0) {
            return 'Sin cambio';
        }
        return `${valor > 0 ? '+' : '−'}${CURRENCY_IMPACTO.format(Math.abs(valor))} (${valor > 0 ? 'aumento' : 'baja'})`;
    }

    diferenciaClase(valor: number): string {
        return valor > 0 ? 'impacto-sube' : valor < 0 ? 'impacto-baja' : '';
    }

    get categoriasImpactoTexto(): string {
        const nombres = [...new Set((this.impacto?.lineas ?? []).map(l => l.nombreCategoria ?? 'Sin categoría'))];
        return nombres.join(', ');
    }

    private simularImpacto(cambio: TariffChangeRequest) {
        this.simulandoImpacto = true;
        this.arancelesService.simular(cambio).subscribe({
            next: (impacto) => {
                this.simulandoImpacto = false;
                this.cambioSimulado = cambio;
                this.mostrarImpacto(impacto);
                this.cdr.detectChanges();
            },
            error: (err: HttpErrorResponse) => {
                this.simulandoImpacto = false;
                this.notifications.notify(err.error?.mensaje ?? 'No se pudo calcular el impacto del cambio. Intentá de nuevo.', 'error');
                this.cdr.detectChanges();
            },
        });
    }

    private mostrarImpacto(impacto: TariffImpactModel) {
        this.impacto = impacto;
        this.confirmacionEscrita = '';
        this.errorImpacto = null;
    }

    // Como el resto de los handlers de esta pantalla, repinta a mano: el gráfico de Chart.js deja eventos fuera de la
    // zona de Angular y, sin esto, "Confirmar actualización" seguía deshabilitado con la cantidad ya bien escrita.
    onConfirmacionEscrita(target: HTMLInputElement) {
        this.confirmacionEscrita = target.value;
        this.cdr.detectChanges();
    }

    // Volver / Esc / ×: no se graba nada y el resumen deja de valer.
    cerrarImpacto() {
        if (!this.confirmandoImpacto) {
            this.descartarImpacto();
            this.cdr.detectChanges();
        }
    }

    private descartarImpacto() {
        this.impacto = null;
        this.cambioSimulado = null;
        this.confirmacionEscrita = '';
        this.errorImpacto = null;
        this.avisoImpactoActualizado = null;
    }

    confirmarImpacto() {
        const impacto = this.impacto;
        const cambio = this.cambioSimulado;
        if (!impacto || !cambio || !this.confirmacionValida) {
            return;
        }
        this.confirmandoImpacto = true;
        this.errorImpacto = null;
        const jugadores = impacto.requiereConfirmacionReforzada ? Number(this.confirmacionEscrita) : null;
        this.arancelesService.programar(cambio, impacto.huella, jugadores).subscribe({
            next: (respuesta) => {
                // Recién acá, con la respuesta del backend: el cambio ya está grabado.
                this.confirmandoImpacto = false;
                const grabado = respuesta.impacto ?? impacto;
                this.descartarImpacto();
                this.feeForm.reset({ target: '', amount: '', validFrom: '' });
                this.montoDisplay = '';
                this.notifications.notify(this.mensajeResultado(grabado, respuesta.mensaje), 'success');
                this.cargarDatos();
                this.cdr.detectChanges();
            },
            error: (err: HttpErrorResponse) => {
                this.confirmandoImpacto = false;
                if (err.status === 409 && err.error?.impacto) {
                    // Cambiaron los datos desde la simulación: no se grabó nada. Se muestra el impacto nuevo para revisarlo.
                    this.mostrarImpacto(err.error.impacto as TariffImpactModel);
                    this.avisoImpactoActualizado = err.error.mensaje ?? 'El impacto cambió desde que lo revisaste: revisá los nuevos números.';
                } else {
                    // No se grabó nada: el resumen sigue abierto para reintentar.
                    this.errorImpacto = err.error?.mensaje ?? 'No se pudo programar el arancel. No se guardó ningún cambio: intentá de nuevo.';
                }
                this.cdr.detectChanges();
            },
        });
    }

    private mensajeResultado(impacto: TariffImpactModel, mensajeBackend: string | undefined): string {
        const jugadores = impacto.jugadores === 1 ? '1 jugador' : `${impacto.jugadores} jugadores`;
        const resumen = `Aranceles actualizados correctamente: ${this.destinoImpacto(impacto)} pasa a ${this.moneda(impacto.montoNuevo)} ` +
            `desde el ${this.formatDate(impacto.vigenteDesde)} (${jugadores}, ${this.diferenciaTexto(impacto.diferenciaMensual).toLowerCase()} por mes).`;
        // El backend agrega qué pasa con las cuotas del mes ("Las cuotas con este monto se generan el...").
        const detalle = (mensajeBackend ?? '').replace(/^Arancel programado correctamente\.\s*/, '');
        return detalle ? `${resumen} ${detalle}` : resumen;
    }

    get amountLabel(): string {
        switch (this.increaseMode) {
            case 'percentage': return 'Porcentaje de aumento';
            case 'fixed': return 'Monto a sumar';
            default: return 'Nuevo monto';
        }
    }

    get amountPlaceholder(): string {
        switch (this.increaseMode) {
            case 'percentage': return '12,5';
            case 'fixed': return '10.000';
            default: return '92.000';
        }
    }

    // Mensaje debajo del campo. Que el destino no tenga arancel vigente se avisa apenas se elige (no hace falta
    // escribir nada para saber que el porcentaje no se va a poder calcular); el resto, una vez que se tocó el campo.
    get amountError(): string | null {
        const target = this.feeForm.get('target')?.value;
        if (needsCurrentTariff(this.increaseMode) && target && this.arancelVigente(target) == null) {
            const nombre = this.targets.find(t => t.value === target)?.label ?? 'Este destino';
            return `${nombre} no tiene un arancel vigente sobre el que calcular el aumento. Usá "Monto absoluto" para definir su primer valor.`;
        }
        const control = this.feeForm.get('amount');
        if (!control || control.valid || !(control.touched || control.dirty)) {
            return null;
        }
        if (control.hasError('required')) {
            return this.increaseMode === 'percentage' ? 'Ingresá el porcentaje de aumento.'
                : this.increaseMode === 'fixed' ? 'Ingresá el monto a sumar.'
                : 'Ingresá el nuevo monto.';
        }
        if (control.hasError('min')) {
            return this.increaseMode === 'percentage' ? 'El porcentaje tiene que ser mayor a 0.'
                : this.increaseMode === 'fixed' ? 'El monto a sumar tiene que ser mayor a $0.'
                : 'El monto tiene que ser mayor a $0.';
        }
        return null;
    }

    // Al cambiar de modo, lo escrito deja de significar lo mismo (10 no es $10 ni 10%): se limpia el campo.
    setIncreaseMode(mode: IncreaseMode) {
        if (this.increaseMode === mode) {
            return;
        }
        this.increaseMode = mode;
        this.montoDisplay = '';
        const amount = this.feeForm.get('amount');
        amount?.setValue('');
        amount?.markAsPristine();
        amount?.markAsUntouched();
        this.cdr.detectChanges();
    }

    // Porcentaje con hasta dos decimales: se muestra con coma ("12,5") y el formulario guarda "12.5".
    onPorcentajeInput(target: HTMLInputElement) {
        const { display, value } = sanitizePercentage(target.value);
        target.value = display;
        this.montoDisplay = display;
        this.feeForm.patchValue({ amount: value });
        this.feeForm.get('amount')?.markAsDirty();
    }

    onAmountInput(target: HTMLInputElement) {
        if (this.increaseMode === 'percentage') {
            this.onPorcentajeInput(target);
        } else {
            this.onMontoInput(target);
        }
    }

    private noAnteriorAHoy = (control: AbstractControl): ValidationErrors | null => {
        const valor = control.value as string;
        return valor && valor < this.hoy ? { fechaPasada: true } : null;
    };

    ngOnInit() {
        this.cargarCategorias();
        this.cargarDatos();
    }

    // Destinos que ofrece el formulario en cada pestaña: la cuota va por género o categoría; la inscripción, por género.
    get targetsVista(): CustomSelectOption[] {
        return this.targets.filter(t => this.vista === 'inscripcion'
            ? String(t.value).startsWith('inscripcion:')
            : !String(t.value).startsWith('inscripcion:'));
    }

    // El historial de la pestaña activa (la inscripción y la cuota no se mezclan).
    get filasVista(): FeeRow[] {
        return this.feeRows.filter(r => (r.category === 'enrollment') === (this.vista === 'inscripcion'));
    }

    // Métricas del encabezado de la pestaña activa.
    get metricasVista(): { value: string; label: string }[] {
        return this.vista === 'inscripcion' ? this.inscripcionMetrics : this.headerMetrics;
    }

    private get inscripcionMetrics(): { value: string; label: string }[] {
        const vigente = (genero: EnrollmentFeeGender) => {
            const fila = this.feeRows.find(r => r.category === 'enrollment' && r.estado === 'Vigente' && r.categoryLabel === enrollmentFeeLabel(genero));
            return fila ? fila.amount : 'Sin definir';
        };
        const proxima = this.feeRows
            .filter(r => r.category === 'enrollment' && r.estado === 'Programado')
            .sort((a, b) => a.validFrom.localeCompare(b.validFrom))[0];
        return [
            { value: vigente('Masculino'), label: 'Inscripción masculina' },
            { value: vigente('Femenino'), label: 'Inscripción femenina' },
            { value: proxima ? this.formatDate(proxima.validFrom) : 'Sin cambios', label: 'Próximo cambio' },
        ];
    }

    setVista(vista: 'cuota' | 'inscripcion') {
        if (this.vista === vista) {
            return;
        }
        this.vista = vista;
        // Cada pestaña tiene su propio formulario: lo cargado en una no se arrastra a la otra.
        this.increaseMode = 'absolute';
        this.feeForm.reset({ target: '', amount: '', validFrom: '' });
        this.montoDisplay = '';
        this.cdr.detectChanges();
    }

    // Las categorías del club se suman como destinos posibles del arancel.
    private cargarCategorias() {
        this.pagosService.getCategorias().subscribe({
            next: (categorias) => {
                this.targets = [
                    ...this.targets,
                    ...categorias.map(c => ({ value: `categoria:${c.idCategoria}`, label: `Categoría ${c.nombre}` })),
                ];
                this.cdr.detectChanges();
            },
            error: () => {},
        });
    }

    // Reformatea el Nuevo monto con puntos de miles a medida que se escribe, preservando la
    // posición del cursor por cantidad de dígitos (no por índice de caracter, que cambia cada
    // vez que se agrega o saca un punto) — así corregir un número a mitad de la escritura no
    // hace saltar el cursor al final.
    onMontoInput(target: HTMLInputElement) {
        const cursorPos = target.selectionStart ?? target.value.length;
        const digitsBeforeCursor = target.value.slice(0, cursorPos).replace(/\D/g, '').length;

        const digitsOnly = target.value.replace(/\D/g, '').slice(0, 12);
        const formatted = digitsOnly ? Number(digitsOnly).toLocaleString('es-AR') : '';

        // Se escribe el DOM a mano, no solo vía el binding [value]: si el texto recalculado da
        // igual al montoDisplay anterior (ej. al borrar un punto de formato, que reaparece solo),
        // Angular no vuelve a tocar el input porque "no cambió" desde su óptica — pero el navegador
        // ya había mutado el value nativamente al borrar, y sin esto queda esa edición sin corregir.
        target.value = formatted;
        this.montoDisplay = formatted;
        this.feeForm.patchValue({ amount: digitsOnly });
        this.feeForm.get('amount')?.markAsDirty();

        queueMicrotask(() => {
            let newPos = digitsBeforeCursor === 0 ? 0 : formatted.length;
            let digitsSeen = 0;
            for (let i = 0; i < formatted.length && digitsBeforeCursor > 0; i++) {
                if (/\d/.test(formatted[i])) {
                    digitsSeen++;
                }
                if (digitsSeen === digitsBeforeCursor) {
                    newPos = i + 1;
                    break;
                }
            }
            target.setSelectionRange(newPos, newPos);
        });
    }

    private cargarDatos() {
        this.arancelesService.getResumen().subscribe({
            next: (resumen) => {
                this.headerMetrics = [
                    { value: resumen.arancelMasculinoVigente != null ? CURRENCY_FULL.format(resumen.arancelMasculinoVigente) : '—', label: 'Arancel masculino' },
                    { value: resumen.arancelFemeninoVigente != null ? CURRENCY_FULL.format(resumen.arancelFemeninoVigente) : '—', label: 'Arancel femenino' },
                    { value: resumen.proximoCambioFecha ? this.formatDate(resumen.proximoCambioFecha) : 'Sin cambios', label: 'Próximo cambio' },
                ];
                this.cdr.detectChanges();
            },
            error: () => {},
        });

        this.arancelesService.getHistorial().subscribe({
            next: (historial) => {
                const cuotaRows = historial.map(mapHistorialItem);
                this.enrollmentFeeService.getAllEnrollmentFees().subscribe({
                    next: (fees) => {
                        const inscripcionRows = fees.map(mapEnrollmentFeeItem);
                        // Merge and sort newest validFrom first
                        this.feeRows = [...cuotaRows, ...inscripcionRows].sort((a, b) =>
                            b.validFrom.localeCompare(a.validFrom)
                        );
                        this.feeChartData = this.buildChartData();
                        // El arancel vigente de cada destino pudo cambiar: el porcentaje / monto fijo se revalida
                        this.feeForm.get('amount')?.updateValueAndValidity();
                        this.cdr.detectChanges();
                    },
                    error: () => {
                        this.feeRows = cuotaRows;
                        this.feeChartData = this.buildChartData();
                        this.feeForm.get('amount')?.updateValueAndValidity();
                        this.cdr.detectChanges();
                    },
                });
            },
            error: () => {},
        });
    }

    // Series for both categories, from January up to the current month, priced
    // with the fee that was in force on the first day of each month
    private buildChartData(): ChartConfiguration<'line'>['data'] {
        const now = new Date();
        const year = now.getFullYear();
        const monthCount = now.getMonth() + 1;

        const monthlyAmounts = (category: 'male' | 'female') =>
            Array.from({ length: monthCount }, (_, index) => {
                const reference = `${year}-${String(index + 1).padStart(2, '0')}-01`;
                const row = this.feeRows.find(fee =>
                    fee.category === category &&
                    fee.validFrom <= reference &&
                    (!fee.validTo || reference <= fee.validTo)
                );
                return row ? this.parseAmount(row.amount) : null;
            });

        return {
            labels: this.monthLabels.slice(0, monthCount),
            datasets: [
                {
                    label: 'Masculino',
                    data: monthlyAmounts('male'),
                    borderColor: '#3b82f6',
                    borderWidth: 2.5,
                    fill: false,
                    pointBackgroundColor: '#ffffff',
                    pointBorderColor: '#3b82f6',
                    pointBorderWidth: 2,
                    pointRadius: 4,
                    pointHoverRadius: 6,
                    tension: 0.35,
                },
                {
                    label: 'Femenino',
                    data: monthlyAmounts('female'),
                    borderColor: '#8b5cf6',
                    borderWidth: 2.5,
                    fill: false,
                    pointBackgroundColor: '#ffffff',
                    pointBorderColor: '#8b5cf6',
                    pointBorderWidth: 2,
                    pointRadius: 4,
                    pointHoverRadius: 6,
                    tension: 0.35,
                },
            ],
        };
    }

    // '$85.000' -> 85000
    private parseAmount(amount: string): number {
        return Number(amount.replace(/\D/g, ''));
    }

    // 85000 -> '$85.000'
    private formatAmount(value: number): string {
        return `$${value.toLocaleString('es-AR')}`;
    }

    get formSubtitle(): string {
        const target = this.feeForm.get('target')?.value;
        if (this.vista === 'inscripcion' && !String(target).startsWith('inscripcion:')) {
            return 'Elegí el género y definí el valor de la inscripción. Se cobra una vez por año a cada jugador; con $0 ese género deja de pagarla.';
        }
        if (target === 'inscripcion:Masculino') {
            return 'Definí el próximo valor de la inscripción masculina. Se cobra una vez por año a cada jugador, y cada renovación usa el valor vigente el día de su aniversario.';
        }
        if (target === 'inscripcion:Femenino') {
            return 'Hoy las mujeres no pagan inscripción. Si el club decide cobrarla, programá acá su valor; con $0 deja de cobrarse. Se cobra una vez por año a cada jugadora, y cada renovación usa el valor vigente el día de su aniversario.';
        }
        if (typeof target === 'string' && target.startsWith('categoria:')) {
            return 'Definí la cuota de esta categoría. Reemplaza al arancel de género para sus jugadores, sea cual sea su género. Los cambios no son retroactivos.';
        }
        return 'Definí el próximo valor de cuota. Si nadie lo cambia, se sigue cobrando el vigente todos los meses. Los cambios no son retroactivos: solo aplican a los meses posteriores a su entrada en vigencia.';
    }

    // El estado ya viene calculado desde el backend (Vigente/Programado/Anterior).
    status(row: FeeRow): 'current' | 'scheduled' | 'previous' {
        switch (row.estado) {
            case 'Vigente': return 'current';
            case 'Programado': return 'scheduled';
            default: return 'previous';
        }
    }

    statusLabel(row: FeeRow): string {
        return row.estado;
    }

    // Fecha desde la que el arancel siguiente lo reemplaza (el día después del último día que
    // rigió). Un arancel que nadie reemplazó se sigue cobrando todos los meses: no tiene fecha
    // de fin, así que no muestra ninguna.
    replacedOnLabel(row: FeeRow): string {
        return row.validTo ? this.formatDate(this.nextDay(row.validTo)) : '—';
    }

    private nextDay(isoDate: string): string {
        const [year, month, day] = isoDate.slice(0, 10).split('-').map(Number);
        return new Date(Date.UTC(year, month - 1, day + 1)).toISOString().slice(0, 10);
    }

    // ISO dates are shown the way they are read locally
    formatDate(isoDate: string): string {
        const [year, month, day] = isoDate.slice(0, 10).split('-');
        return `${day}/${month}/${year}`;
    }

    // ===== Cancelar un arancel =====

    // Arancel que el administrador está por cancelar (muestra la confirmación)
    cancelTarget: FeeRow | null = null;
    cancelando = false;

    askCancel(row: FeeRow) {
        this.cancelTarget = row;
    }

    closeCancel() {
        if (!this.cancelando) {
            this.cancelTarget = null;
        }
    }

    confirmCancel() {
        const row = this.cancelTarget;
        if (!row || this.cancelando) {
            return;
        }

        this.cancelando = true;
        this.arancelesService.cancelar(row.id).subscribe({
            next: () => {
                this.cancelando = false;
                this.cancelTarget = null;
                this.notifications.notify(
                    'Arancel cancelado. Se quitaron sus cuotas pendientes sin pagos; se vuelven a emitir al programar un arancel nuevo o en la próxima ejecución automática.',
                    'success',
                );
                this.cargarDatos();
                this.cdr.detectChanges();
            },
            error: (err: HttpErrorResponse) => {
                this.cancelando = false;
                this.cancelTarget = null;
                // Ej.: "No se puede cancelar este arancel: 2 cuotas emitidas con él ya tienen pagos registrados."
                this.notifications.notify(err.error?.mensaje ?? 'No se pudo cancelar el arancel. Intentá de nuevo.', 'error');
                this.cdr.detectChanges();
            },
        });
    }

    onSubmit() {
        if (this.feeForm.invalid) {
            this.feeForm.markAllAsTouched();
            return;
        }

        const { target, validFrom, amount } = this.feeForm.value;

        // HU-106: la cuota mensual no se graba de un saque. Primero el backend calcula el impacto (sin grabar nada) y se
        // muestra el resumen; recién al confirmarlo se programa (confirmarImpacto).
        if (!(typeof target === 'string' && target.startsWith('inscripcion:'))) {
            const cambio = this.cambioDelFormulario();
            if (cambio == null) {
                this.feeForm.markAllAsTouched();
                return;
            }
            this.simularImpacto(cambio);
            return;
        }

        // Inscripción: monto absoluto, sin modos de HU-069 ni resumen de impacto.
        this.enviando = true;
        const gender = target.slice('inscripcion:'.length) as EnrollmentFeeGender;
        this.enrollmentFeeService.createEnrollmentFee({ gender, amount: Number(amount), startDate: validFrom }).subscribe({
            next: () => {
                this.enviando = false;
                this.feeForm.reset({ target: '', amount: '', validFrom: '' });
                this.montoDisplay = '';
                this.notifications.notify(
                    Number(amount) === 0
                        ? `Desde esa fecha no se cobra la ${enrollmentFeeLabel(gender).toLowerCase()}.`
                        : `Nuevo valor de la ${enrollmentFeeLabel(gender).toLowerCase()} programado correctamente.`,
                    'success',
                );
                this.cargarDatos();
                this.cdr.detectChanges();
            },
            error: (err: HttpErrorResponse) => {
                this.enviando = false;
                const mensaje = err.error?.mensaje ?? 'No se pudo programar el valor de inscripción. Intentá de nuevo.';
                this.notifications.notify(mensaje, 'error');
                this.cdr.detectChanges();
            },
        });
    }
}

function mapHistorialItem(item: TariffHistoryItemModel): FeeRow {
    // Un arancel es por categoría (genero null) o por género
    const porCategoria = item.genero == null;
    return {
        id: item.idArancel,
        target: porCategoria ? `categoria:${item.idCategoria}` : `genero:${item.genero}`,
        category: porCategoria ? 'category' : item.genero === 'Femenino' ? 'female' : 'male',
        categoryLabel: porCategoria ? `Categoría ${item.nombreCategoria ?? item.idCategoria}` : (item.genero as string),
        amount: `$${item.monto.toLocaleString('es-AR')}`,
        validFrom: item.vigenteDesde.slice(0, 10),
        validTo: item.vigenteHasta ? item.vigenteHasta.slice(0, 10) : '',
        estado: item.estado,
        responsable: responsableNombre(item.responsableNombre, item.responsableApellido),
        cancelable: item.puedeCancelar,
    };
}

function mapEnrollmentFeeItem(fee: EnrollmentFeeModel): FeeRow {
    const estadoMap: Record<string, 'Vigente' | 'Programado' | 'Anterior'> = {
        Current: 'Vigente',
        Scheduled: 'Programado',
        Previous: 'Anterior',
    };
    return {
        id: fee.id,
        target: `inscripcion:${fee.gender}`,
        category: 'enrollment',
        categoryLabel: enrollmentFeeLabel(fee.gender),
        amount: fee.amount === 0 ? 'No se cobra' : `$${fee.amount.toLocaleString('es-AR')}`,
        validFrom: fee.startDate.slice(0, 10),
        validTo: fee.endDate ? fee.endDate.slice(0, 10) : '',
        estado: estadoMap[fee.status] ?? 'Anterior',
        responsable: responsableNombre(fee.responsibleName, fee.responsibleSurname),
        cancelable: false,
    };
}

function responsableNombre(nombre?: string | null, apellido?: string | null): string {
    return [nombre, apellido].filter(Boolean).join(' ') || 'Sin registro histórico';
}
