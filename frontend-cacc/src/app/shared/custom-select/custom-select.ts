import {
    Component,
    ElementRef,
    HostListener,
    Input,
    NgZone,
    forwardRef,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

// Reemplaza al <select> nativo: el menú flotante abierto de un <select> lo
// dibuja el sistema operativo y no se puede estilizar (queda gris genérico
// de Windows sin importar el CSS que se le ponga al elemento cerrado). Este
// componente es un ControlValueAccessor, así que funciona con
// formControlName exactamente igual que un <select> — mismo binding, misma
// validación, sin tocar el FormGroup que ya lo usa.
@Component({
    selector: 'app-custom-select',
    standalone: true,
    imports: [CommonModule],
    templateUrl: './custom-select.html',
    styleUrl: './custom-select.css',
    // El "id" recibido se reenvía al <button> interno (ver el template) para que
    // <label for="..."> lo siga enfocando; acá se lo saca del host para que no
    // quede duplicado en dos elementos del DOM.
    host: { '[attr.id]': 'null' },
    providers: [
        {
            provide: NG_VALUE_ACCESSOR,
            useExisting: forwardRef(() => CustomSelect),
            multi: true,
        },
    ],
})
export class CustomSelect implements ControlValueAccessor {
    @Input() options: (string | number)[] = [];
    @Input() placeholder = 'Seleccionar';
    // Se reenvía al <button> interno para que un <label for="..."> externo
    // lo siga enfocando, igual que hacía con el <select> nativo que reemplaza.
    @Input() id: string | null = null;

    isOpen = false;
    disabled = false;
    value: string | number | null = null;

    private onChange: (value: string | number | null) => void = () => {};
    private onTouched: () => void = () => {};

    constructor(private elementRef: ElementRef<HTMLElement>, private ngZone: NgZone) {}

    writeValue(value: string | number | null): void {
        this.value = value;
    }

    registerOnChange(fn: (value: string | number | null) => void): void {
        this.onChange = fn;
    }

    registerOnTouched(fn: () => void): void {
        this.onTouched = fn;
    }

    setDisabledState(isDisabled: boolean): void {
        this.disabled = isDisabled;
    }

    get hasValue(): boolean {
        return this.value !== null && this.value !== '';
    }

    toggleDropdown(): void {
        if (this.disabled) {
            return;
        }
        // Páginas con gráficos (ng2-charts/Chart.js) sacan a propósito su
        // renderizado de la zona de Angular por rendimiento, y esa zona
        // "outside" a veces queda pegada a la navegación que sigue — un
        // <select> propio con formControlName necesita la detección de
        // cambios automática de Angular para abrir el menú, así que fuerza
        // la reentrada acá en vez de asumir que ya está dentro. Sin esto: el
        // botón registra el click y cambia el estado, pero el menú nunca se
        // pinta hasta el próximo evento que sí corra dentro de la zona (por
        // eso solo se notaba después de recargar la página).
        this.ngZone.run(() => {
            this.isOpen = !this.isOpen;
            if (!this.isOpen) {
                this.onTouched();
            }
        });
    }

    selectOption(option: string | number): void {
        this.ngZone.run(() => {
            this.value = option;
            this.onChange(option);
            this.onTouched();
            this.isOpen = false;
        });
    }

    // Cierra el menú si el click fue afuera del componente (trigger + lista)
    @HostListener('document:click', ['$event'])
    onDocumentClick(event: MouseEvent): void {
        if (this.isOpen && !this.elementRef.nativeElement.contains(event.target as Node)) {
            this.ngZone.run(() => {
                this.isOpen = false;
                this.onTouched();
            });
        }
    }

    @HostListener('document:keydown.escape')
    onEscape(): void {
        if (this.isOpen) {
            this.ngZone.run(() => {
                this.isOpen = false;
            });
        }
    }
}
