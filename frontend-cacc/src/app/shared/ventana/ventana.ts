import { A11yModule } from '@angular/cdk/a11y';
import { Component, EventEmitter, HostListener, Input, OnDestroy, OnInit, Output } from '@angular/core';

let siguienteId = 0;

// Menús que se muestran sobre <body> (desplegables, calendario, ayudas "?"): mientras uno está abierto, el
// primer Esc lo cierra a él y no a la ventana que lo contiene.
const MENUS_FLOTANTES = '.cs-menu, .dp-panel, .help-tooltip';

// Ventana de confirmación única del Portal Administrativo (HU-094). Cada pantalla pone su contenido adentro
// y la ventana resuelve la forma y el comportamiento comunes:
//   - título, subtítulo, cuerpo, mensaje de error y acciones Volver / Confirmar (o botones propios);
//   - se cierra con Esc, con la × y al tocar el fondo, salvo mientras la operación está en curso;
//   - el foco queda adentro mientras está abierta y vuelve al botón que la abrió al cerrarse;
//   - la acción de riesgo va con el tono de peligro;
//   - fondo oscurecido sin desenfoque (blur), por costo de render.
//
// Uso:
//   <app-ventana titulo="Deshabilitar acceso" [subtitulo]="nombre" [peligro]="true"
//                textoConfirmar="Deshabilitar" textoEnCurso="Deshabilitando…" [enCurso]="enviando()"
//                [error]="error()" (confirmar)="deshabilitar()" (cerrar)="cerrarVentana()">
//     <p>Corta el acceso de esta persona…</p>
//   </app-ventana>
@Component({
  selector: 'app-ventana',
  standalone: true,
  imports: [A11yModule],
  templateUrl: './ventana.html',
  styleUrl: './ventana.css',
})
export class Ventana implements OnInit, OnDestroy {
  @Input({ required: true }) titulo = '';
  @Input() subtitulo: string | null | undefined = '';
  @Input() error: string | null | undefined = null;

  @Input() textoVolver = 'Volver';
  @Input() textoConfirmar = 'Confirmar';
  // Texto del botón Confirmar mientras la operación está en curso ("Guardando…").
  @Input() textoEnCurso = '';
  @Input() textoCerrar = 'Cerrar';

  // La acción borra o corta algo: Confirmar va en rojo y el borde de la ventana también.
  @Input() peligro = false;
  // Mientras es true no se puede cerrar ni volver a confirmar.
  @Input() enCurso = false;
  @Input() confirmarDeshabilitado = false;

  // false: sin Volver / Confirmar (la pantalla pone sus botones con el atributo ventanaAcciones, o ninguno).
  @Input() conAcciones = true;
  // id de un <form> del cuerpo: Confirmar lo envía (type="submit"), así funciona también Enter en los campos.
  @Input() formulario: string | null = null;

  @Input() forma: 'centrada' | 'angosta' | 'ancha' | 'lateral' = 'centrada';

  // Adónde vuelve el foco al cerrar si el botón que abrió la ventana ya no existe. Pasa con las opciones de
  // un menú ("⋯"): el menú se cierra al elegir y la opción desaparece, así que se indica el botón del menú.
  @Input() focoAlCerrar: HTMLElement | null = null;

  @Output() readonly confirmar = new EventEmitter<void>();
  // Si nadie escucha "volver", el botón Volver cierra la ventana.
  @Output() readonly volver = new EventEmitter<void>();
  @Output() readonly cerrar = new EventEmitter<void>();

  readonly tituloId = `ventana-titulo-${++siguienteId}`;
  readonly subtituloId = `ventana-subtitulo-${siguienteId}`;

  // Lo que tenía el foco al abrirse. cdkTrapFocus le devuelve el foco solo; esto es para saber si ya no existe.
  private abridor: Element | null = null;

  ngOnInit(): void {
    this.abridor = document.activeElement;
  }

  ngOnDestroy(): void {
    const abridor = this.abridor;
    const respaldo = this.focoAlCerrar;
    if (!respaldo) {
      return;
    }
    // Después de que cdkTrapFocus intentó devolver el foco y de que la pantalla se redibujó.
    setTimeout(() => {
      const abridorPerdido = !abridor || abridor === document.body || !abridor.isConnected;
      const focoSuelto = !document.activeElement || document.activeElement === document.body;
      if (abridorPerdido && focoSuelto && respaldo.isConnected) {
        respaldo.focus();
      }
    });
  }

  intentarCerrar(): void {
    if (!this.enCurso) {
      this.cerrar.emit();
    }
  }

  // Solo el fondo cierra: un clic dentro de la ventana no llega acá como destino.
  alTocarFondo(event: MouseEvent): void {
    if (event.target === event.currentTarget) {
      this.intentarCerrar();
    }
  }

  alVolver(): void {
    if (this.enCurso) {
      return;
    }
    if (this.volver.observed) {
      this.volver.emit();
    } else {
      this.cerrar.emit();
    }
  }

  alConfirmar(): void {
    if (!this.enCurso && !this.confirmarDeshabilitado && !this.formulario) {
      this.confirmar.emit();
    }
  }

  @HostListener('document:keydown.escape')
  alEscape(): void {
    if (!document.querySelector(MENUS_FLOTANTES)) {
      this.intentarCerrar();
    }
  }
}
