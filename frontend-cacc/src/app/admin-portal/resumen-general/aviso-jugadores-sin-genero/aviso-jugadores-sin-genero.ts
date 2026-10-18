import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { JugadoresSinGeneroService } from '../../../services/jugadores-sin-genero';
import { JugadoresSinGeneroModel } from '../../../models/JugadoresSinGeneroModel';

// Aviso del Resumen General: jugadores en el club sin género cargado. Sin ese dato el sistema no encuentra el arancel que les
// corresponde y no se les emite la cuota. Solo informa: no modifica nada (los datos de cada persona se completan en el
// Portal Deportivo). No aparece si no falta ninguno.
@Component({
  selector: 'app-aviso-jugadores-sin-genero',
  standalone: true,
  templateUrl: './aviso-jugadores-sin-genero.html',
  styleUrl: './aviso-jugadores-sin-genero.css',
})
export class AvisoJugadoresSinGenero implements OnInit {
  resultado: JugadoresSinGeneroModel = { total: 0, jugadores: [] };

  // La franja arranca resumida (una línea); el listado se despliega a pedido.
  expandido = false;

  constructor(
    private service: JugadoresSinGeneroService,
    private cdr: ChangeDetectorRef,
  ) {}

  ngOnInit(): void {
    this.service.get().subscribe({
      next: (resultado) => {
        this.resultado = resultado;
        this.cdr.detectChanges();
      },
      // Si el aviso no se puede cargar, el resto del Resumen General sigue funcionando.
      error: () => {
        this.resultado = { total: 0, jugadores: [] };
        this.cdr.detectChanges();
      },
    });
  }

  // Jugadores que existen pero no entran en la lista que trae la API.
  get sinMostrar(): number {
    return Math.max(0, this.resultado.total - this.resultado.jugadores.length);
  }

  alternar(): void {
    this.expandido = !this.expandido;
    this.cdr.detectChanges();
  }
}
