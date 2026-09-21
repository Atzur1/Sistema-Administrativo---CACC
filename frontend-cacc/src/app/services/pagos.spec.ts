import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { PagosService } from './pagos';
import { PlayerAccountModel } from '../models/PlayerAccountModel';

// HU-029: the roster is read-only, so the service only ever issues GET requests.
describe('PagosService - player accounts', () => {
    let service: PagosService;
    let http: HttpTestingController;

    beforeEach(() => {
        TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
        service = TestBed.inject(PagosService);
        http = TestBed.inject(HttpTestingController);
    });

    afterEach(() => http.verify());

    it('asks for the whole roster with GET and onlyDebtors=false', () => {
        service.getPlayerAccounts(false).subscribe();

        const req = http.expectOne('http://localhost:5118/api/pagos/player-accounts?onlyDebtors=false');
        expect(req.request.method).toBe('GET');
        req.flush([]);
    });

    it('asks for debtors only with GET and onlyDebtors=true', () => {
        service.getPlayerAccounts(true).subscribe();

        const req = http.expectOne('http://localhost:5118/api/pagos/player-accounts?onlyDebtors=true');
        expect(req.request.method).toBe('GET');
        req.flush([]);
    });

    it('returns the rows exactly as the API sends them', () => {
        const rows: PlayerAccountModel[] = [
            { playerId: 2, firstName: 'BAUTISTA', lastName: 'SANCHEZ', dni: '99000002', category: 'AFA 20067', amountOwed: 255000, pendingInstallments: 3 },
        ];
        let received: PlayerAccountModel[] | undefined;

        service.getPlayerAccounts(true).subscribe((result) => (received = result));
        http.expectOne(() => true).flush(rows);

        expect(received).toEqual(rows);
    });
});
