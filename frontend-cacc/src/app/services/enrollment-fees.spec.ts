import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { EnrollmentFeeService } from './enrollment-fees';
import {
    EnrollmentFeeModel,
    enrollmentFeeStatusClass,
    enrollmentFeeStatusLabel,
    findCurrentEnrollmentFee,
    findNextEnrollmentFee,
    isEnrollmentFeeDateTaken,
} from '../models/EnrollmentFeeModel';

const FEES: EnrollmentFeeModel[] = [
    { id: 3, amount: 70000, startDate: '2027-03-01', endDate: null, status: 'Scheduled' },
    { id: 2, amount: 60000, startDate: '2026-11-01', endDate: '2027-02-28', status: 'Scheduled' },
    { id: 1, amount: 50000, startDate: '2026-06-01', endDate: '2026-10-31', status: 'Current' },
    { id: 0, amount: 40000, startDate: '2026-01-01', endDate: '2026-05-31', status: 'Previous' },
];

// HU-033: enrollment fee of the men's squad
describe('EnrollmentFeeService', () => {
    let service: EnrollmentFeeService;
    let http: HttpTestingController;

    beforeEach(() => {
        TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
        service = TestBed.inject(EnrollmentFeeService);
        http = TestBed.inject(HttpTestingController);
    });

    afterEach(() => http.verify());

    it('reads the fee history with GET', () => {
        let received: EnrollmentFeeModel[] | undefined;

        service.getAllEnrollmentFees().subscribe((fees) => (received = fees));
        const req = http.expectOne('http://localhost:5118/api/enrollmentfees');
        expect(req.request.method).toBe('GET');
        req.flush(FEES);

        expect(received).toEqual(FEES);
    });

    it('schedules a new fee with POST, sending amount and start date', () => {
        service.createEnrollmentFee({ amount: 55000, startDate: '2026-12-01' }).subscribe();

        const req = http.expectOne('http://localhost:5118/api/enrollmentfees');
        expect(req.request.method).toBe('POST');
        expect(req.request.body).toEqual({ amount: 55000, startDate: '2026-12-01' });
        req.flush(FEES[0]);
    });
});

describe('EnrollmentFeeModel helpers', () => {
    it('finds the fee in force', () => {
        expect(findCurrentEnrollmentFee(FEES)?.amount).toBe(50000);
        expect(findCurrentEnrollmentFee([FEES[0]])).toBeNull();
    });

    it('finds the closest scheduled change, not the newest one', () => {
        expect(findNextEnrollmentFee(FEES)?.startDate).toBe('2026-11-01');
        expect(findNextEnrollmentFee([FEES[2], FEES[3]])).toBeNull();
    });

    it('tells whether a start date is already taken', () => {
        expect(isEnrollmentFeeDateTaken(FEES, '2026-11-01')).toBe(true);
        expect(isEnrollmentFeeDateTaken(FEES, '2026-11-02')).toBe(false);
    });

    it('translates the status and picks the pill the screen already styles', () => {
        expect(enrollmentFeeStatusLabel('Current')).toBe('Vigente');
        expect(enrollmentFeeStatusLabel('Scheduled')).toBe('Programado');
        expect(enrollmentFeeStatusLabel('Previous')).toBe('Anterior');
        expect(enrollmentFeeStatusClass('Current')).toBe('status-current');
        expect(enrollmentFeeStatusClass('Scheduled')).toBe('status-scheduled');
    });
});
