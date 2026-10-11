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
}
