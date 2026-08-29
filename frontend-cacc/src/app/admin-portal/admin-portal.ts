import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, RouterLink, RouterLinkActive, Router, RouterOutlet } from '@angular/router';
import { AuthService } from '../services/auth';

@Component({
    selector: 'app-admin-portal',
    standalone: true,
    imports: [CommonModule, RouterOutlet, RouterLink, RouterLinkActive],
    templateUrl: './admin-portal.html',
    styleUrl: './admin-portal.css',
})
export class AdminPortal {
    constructor(
        private router: Router,
        private authService: AuthService
    ) {}

// Go back to the portals selection screen
cambiarPortal() {
    this.router.navigate(['/portales']);
}

// Log out and return to the login screen
cerrarSesion() {
        this.authService.logout();
        this.router.navigate(['/']);
    }
}