import { Component, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { Login } from './login/login'; // <--- 1. Importamos tu componente de login

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, Login], // <--- 2. Lo agregamos a los imports
  // 3. Indicamos que cargue la etiqueta de tu login en la pantalla principal
  template: `
    <app-login></app-login>
  `,
  styleUrl: './app.css'
})
export class App {
  protected readonly title = signal('frontend-cacc');
}