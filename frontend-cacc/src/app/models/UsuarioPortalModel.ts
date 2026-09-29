// Persona cargada por el otro equipo (FK_id_rol de SuperAdmin o Administrador)
// que todavía no tiene acceso al Portal Administrativo.
export interface UsuarioCandidato {
  idUsuario: number;
  nombre: string;
  apellido: string;
  dni: string;
  email: string;
  // Solo una sugerencia para precargar el desplegable de rol: el rol real lo
  // elige el SuperAdmin al habilitar, no se copia de acá.
  fkIdRolSugerido: RolPortal;
}

// Alguien con acceso_portal = 1 hoy.
export interface UsuarioPortal {
  idUsuario: number;
  nombre: string;
  apellido: string;
  dni: string;
  email: string;
  rolPortal: RolPortal;
  activo: boolean;
  activacionPendiente: boolean;
}

export type RolPortal = 1 | 2;

export const ROL_PORTAL_LABELS: Record<RolPortal, string> = {
  1: 'SuperAdmin',
  2: 'Administrador',
};

export interface HabilitarUsuarioRequest {
  email: string;
  rolPortal: RolPortal;
}

export interface CambiarRolRequest {
  rolPortal: RolPortal;
}

export interface EditarCandidatoRequest {
  nombre: string;
  apellido: string;
  email: string;
}
