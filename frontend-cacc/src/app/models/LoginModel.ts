// Body of POST /api/auth/login (LoginDTO). username is the account's DNI or, during the transition, its email.
export interface LoginModel {
    username: string;
    password: string;
}

// Body of a successful login (LoginResponseDTO); also what the session keeps in sessionStorage.
export interface LoginResponseModel {
    message?: string;
    email: string;
    firstName?: string;
    lastName?: string;
    dni?: string;
    role: number;
    token: string;
    // HU-007: portales a los que puede entrar, según el backend (PortalesDeLaCuenta). Decide a dónde va al iniciar sesión.
    portales: Portal[];
}

export type Portal = 'administrativo' | 'deportivo' | 'personas';
