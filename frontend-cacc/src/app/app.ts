import { Component, NgModule, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { Login } from './login/login';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, Login],
  template: `
    <app-login></app-login>
  `,
  styleUrl: './app.css'
})
export class App {
  protected readonly title = signal('frontend-cacc');
}
