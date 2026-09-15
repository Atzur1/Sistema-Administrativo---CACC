import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

// Roles drive the avatar, pill and icon colors across the dashboard
type UserRole = 'admin' | 'user' | 'teacher' | 'staff';

// One row in the users table
interface UserRow {
    initials: string;
    name: string;
    email: string;
    role: UserRole;
    roleLabel: string;
    active: boolean;
    statusLabel: string;
    lastActivity: string;
}

// One entry in the role distribution panel
interface RoleSummary {
    role: UserRole;
    label: string;
    description: string;
    count: number;
}

@Component({
    selector: 'app-usuarios',
    standalone: true,
    imports: [CommonModule],
    templateUrl: './usuarios-y-permisos.html',
    styleUrl: './usuarios-y-permisos.css',
})
export class Usuarios {

    // Header
    headerMetrics = [
        { value: '12', label: 'Usuarios' },
        { value: '11', label: 'Activos' },
        { value: '1', label: 'Admin' },
    ];

    // Registered accounts — fictional demo data
    users: UserRow[] = [
        {
            initials: 'MZ',
            name: 'Mariano Zárate',
            email: 'admin@cacc.com',
            role: 'admin',
            roleLabel: 'Administrador',
            active: true,
            statusLabel: 'Activo',
            lastActivity: 'Hace 5 minutos',
        },
        {
            initials: 'CP',
            name: 'Camila Paz',
            email: 'cpaz@cacc.com',
            role: 'user',
            roleLabel: 'Usuario',
            active: true,
            statusLabel: 'Activo',
            lastActivity: 'Hace 2 horas',
        },
        {
            initials: 'RA',
            name: 'Rocío Aguirre',
            email: 'raguirre@cacc.com',
            role: 'user',
            roleLabel: 'Usuario',
            active: true,
            statusLabel: 'Activo',
            lastActivity: 'Hace 45 minutos',
        },
        {
            initials: 'TV',
            name: 'Tomás Villalba',
            email: 'tvillalba@cacc.com',
            role: 'user',
            roleLabel: 'Usuario',
            active: true,
            statusLabel: 'Activo',
            lastActivity: 'Hoy, 08:15',
        },
        {
            initials: 'IB',
            name: 'Ignacio Britos',
            email: 'ibritos@cacc.com',
            role: 'teacher',
            roleLabel: 'Profesor',
            active: true,
            statusLabel: 'Activo',
            lastActivity: 'Hoy, 09:40',
        },
        {
            initials: 'LV',
            name: 'Lautaro Vargas',
            email: 'lvargas@cacc.com',
            role: 'teacher',
            roleLabel: 'Profesor',
            active: false,
            statusLabel: 'Inactivo',
            lastActivity: 'Hace 5 días',
        },
        {
            initials: 'SM',
            name: 'Sofía Miranda',
            email: 'smiranda@cacc.com',
            role: 'teacher',
            roleLabel: 'Profesor',
            active: true,
            statusLabel: 'Activo',
            lastActivity: 'Hace 1 hora',
        },
        {
            initials: 'EB',
            name: 'Emilio Bustos',
            email: 'ebustos@cacc.com',
            role: 'teacher',
            roleLabel: 'Profesor',
            active: true,
            statusLabel: 'Activo',
            lastActivity: 'Ayer, 20:05',
        },
        {
            initials: 'VL',
            name: 'Valeria Ledesma',
            email: 'vledesma@cacc.com',
            role: 'teacher',
            roleLabel: 'Profesor',
            active: true,
            statusLabel: 'Activo',
            lastActivity: 'Hoy, 10:30',
        },
        {
            initials: 'FR',
            name: 'Franco Reinoso',
            email: 'freinoso@cacc.com',
            role: 'staff',
            roleLabel: 'Staff',
            active: true,
            statusLabel: 'Activo',
            lastActivity: 'Ayer, 18:20',
        },
        {
            initials: 'KM',
            name: 'Karina Molina',
            email: 'kmolina@cacc.com',
            role: 'staff',
            roleLabel: 'Staff',
            active: true,
            statusLabel: 'Activo',
            lastActivity: 'Hace 3 horas',
        },
        {
            initials: 'NF',
            name: 'Nicolás Ferreyra',
            email: 'nferreyra@cacc.com',
            role: 'staff',
            roleLabel: 'Staff',
            active: true,
            statusLabel: 'Activo',
            lastActivity: 'Hace 6 horas',
        },
    ];

    // How many users hold each role
    roleDistribution: RoleSummary[] = [
        {
            role: 'admin',
            label: 'Administrador',
            description: 'Control total del sistema',
            count: 1,
        },
        {
            role: 'user',
            label: 'Usuario',
            description: 'Acceso general',
            count: 3,
        },
        {
            role: 'teacher',
            label: 'Profesor',
            description: 'Cuerpo docente del club',
            count: 5,
        },
        {
            role: 'staff',
            label: 'Staff',
            description: 'Cuerpo técnico',
            count: 3,
        },
    ];
}
