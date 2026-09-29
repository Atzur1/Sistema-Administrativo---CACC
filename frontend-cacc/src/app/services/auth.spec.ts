import { TestBed } from '@angular/core/testing';

import { AuthService } from './auth';
import { HttpClient, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

describe('AuthService', () => {
  let service: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    sessionStorage.clear();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('restores a valid session after the service is recreated', () => {
    const token = `header.${btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + 600 }))}.signature`;
    service.login('admin@cacc.com', 'test-password').subscribe();
    http.expectOne('/api/auth/login').flush({ email: 'admin@cacc.com', rol: 1, token });

    const fresh = new AuthService(TestBed.inject(HttpClient));
    expect(fresh.isAuthenticated()).toBe(true);
  });
});
