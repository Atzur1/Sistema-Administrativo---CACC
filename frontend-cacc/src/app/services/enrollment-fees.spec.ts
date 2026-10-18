import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { EnrollmentFeeService } from './enrollment-fees';
import {
    EnrollmentFeeModel,
    enrollmentFeeLabel,
    enrollmentFeeStatusClass,
    enrollmentFeeStatusLabel,
    findCurrentEnrollmentFee,
    findNextEnrollmentFee,
    isEnrollmentFeeDateTaken,
} from '../models/EnrollmentFeeModel';

const FEES: EnrollmentFeeModel[] = [
    { id: 3, gender: 'Masculino', amount: 70000, startDate: '2027-03-01', endDate: null, status: 'Scheduled' },
    { id: 2, gender: 'Masculino', amount: 60000, startDate: '2026-11-01', endDate: '2027-02-28', status: 'Scheduled' },
    { id: 1, gender: 'Masculino', amount: 50000, startDate: '2026-06-01', endDate: '2026-10-31', status: 'Current' },
    { id: 0, gender: 'Masculino', amount: 40000, startDate: '2026-01-01', endDate: '2026-05-31', status: 'Previous' },
];

// Las mujeres hoy no pagan inscripción; si el club programa un arancel femenino, tiene su propia historia.
const FEES_CON_FEMENINO: EnrollmentFeeModel[] = [
    ...FEES,
    { id: 5, gender: 'Femenino', amount: 30000, startDate: '2026-11-01', endDate: null, status: 'Scheduled' },
    { id: 4, gender: 'Femenino', amount: 0, startDate: '2026-01-01', endDate: '2026-10-31', status: 'Current' },
];

// HU-033: enrollment fee, one history per gender
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
        const req = http.expectOne('/api/enrollmentfees');
        expect(req.request.method).toBe('GET');
        req.flush(FEES);

        expect(received).toEqual(FEES);
    });

    it('schedules a new fee with POST, sending gender, amount and start date', () => {
        service.createEnrollmentFee({ gender: 'Femenino', amount: 55000, startDate: '2026-12-01' }).subscribe();

        const req = http.expectOne('/api/enrollmentfees');
        expect(req.request.method).toBe('POST');
        expect(req.request.body).toEqual({ gender: 'Femenino', amount: 55000, startDate: '2026-12-01' });
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

    it('keeps one history per gender: the fee in force, the next change and the taken days do not mix', () => {
        expect(findCurrentEnrollmentFee(FEES_CON_FEMENINO, 'Masculino')?.amount).toBe(50000);
        expect(findCurrentEnrollmentFee(FEES_CON_FEMENINO, 'Femenino')?.amount).toBe(0);
        expect(findNextEnrollmentFee(FEES_CON_FEMENINO, 'Femenino')?.amount).toBe(30000);
        // El 1/11 empieza un arancel de cada género: son días distintos para cada historia
        expect(isEnrollmentFeeDateTaken(FEES_CON_FEMENINO, '2026-11-01', 'Femenino')).toBe(true);
        expect(isEnrollmentFeeDateTaken(FEES_CON_FEMENINO, '2027-03-01', 'Femenino')).toBe(false);
        expect(isEnrollmentFeeDateTaken(FEES_CON_FEMENINO, '2027-03-01', 'Masculino')).toBe(true);
    });

    it('an unknown gender has no fee in force (it is not charged)', () => {
        expect(findCurrentEnrollmentFee(FEES, 'Femenino')).toBeNull();
    });

    it('names each enrollment fee by gender', () => {
        expect(enrollmentFeeLabel('Masculino')).toBe('Inscripción masculina');
        expect(enrollmentFeeLabel('Femenino')).toBe('Inscripción femenina');
    });

    it('translates the status and picks the pill the screen already styles', () => {
        expect(enrollmentFeeStatusLabel('Current')).toBe('Vigente');
        expect(enrollmentFeeStatusLabel('Scheduled')).toBe('Programado');
        expect(enrollmentFeeStatusLabel('Previous')).toBe('Anterior');
        expect(enrollmentFeeStatusClass('Current')).toBe('status-current');
        expect(enrollmentFeeStatusClass('Scheduled')).toBe('status-scheduled');
    });
});
