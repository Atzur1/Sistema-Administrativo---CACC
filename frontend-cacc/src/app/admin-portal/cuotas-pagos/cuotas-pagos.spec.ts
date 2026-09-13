import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { CuotasPagos } from './cuotas-pagos';
import { DiscountService } from '../../services/discounts';
import { PlayerService } from '../../services/players';
import { PaymentService } from '../../services/payments';
import { DiscountModel } from '../../models/DiscountModel';
import { PaymentModel } from '../../models/PaymentModel';
import { PlayerModel } from '../../models/PlayerModel';

const PLAYER: PlayerModel = {
    id: 577,
    fullName: 'VENICA COY, GUILLERMINA',
    document: '40.111.222',
    category: 'Femenino',
};

// The player already paid July 2026
const PAID_JULY: PaymentModel = {
    id: 1300,
    playerId: 577,
    playerName: PLAYER.fullName,
    category: PLAYER.category,
    amount: 85000,
    paymentDate: '2026-09-13',
    dueDate: null,
    period: '2026-07',
    method: 'efectivo',
    isPaid: true,
    reference: null,
    registeredAt: '2026-09-13T17:31:13',
};

describe('CuotasPagos', () => {
    let fixture: ComponentFixture<CuotasPagos>;
    let component: CuotasPagos;
    let paymentService: {
        getLatestPayments: ReturnType<typeof vi.fn>;
        getPendingFees: ReturnType<typeof vi.fn>;
        getPaymentsByPlayer: ReturnType<typeof vi.fn>;
        getTreasuryMetrics: ReturnType<typeof vi.fn>;
        createPayment: ReturnType<typeof vi.fn>;
    };

    beforeEach(async () => {
        paymentService = {
            getLatestPayments: vi.fn(() => of([])),
            getPendingFees: vi.fn(() => of([])),
            getPaymentsByPlayer: vi.fn(() => of([PAID_JULY])),
            getTreasuryMetrics: vi.fn(() => of({ collectedThisYear: 0, paymentsThisMonth: 0, pendingCount: 0 })),
            createPayment: vi.fn(),
        };

        await TestBed.configureTestingModule({
            imports: [CuotasPagos],
            providers: [
                { provide: PaymentService, useValue: paymentService },
                { provide: PlayerService, useValue: { getPlayers: () => of([PLAYER]) } },
                { provide: DiscountService, useValue: { getDiscountMap: () => of(new Map<number, DiscountModel>()) } },
            ],
        }).compileComponents();

        fixture = TestBed.createComponent(CuotasPagos);
        component = fixture.componentInstance;
        fixture.detectChanges();
    });

    afterEach(() => {
        fixture.destroy();
    });

    // Fills every field with a valid July 2026 payment for the test player
    function fillForm(method: string, month = 8, reference = '') {
        component.selectPlayer(PLAYER);
        component.paymentForm.patchValue({ periodYear: 2026, periodMonth: month, amount: 85000, method });
        component.paymentForm.patchValue({ reference });
    }

    describe('reference field', () => {
        it('starts disabled', () => {
            expect(component.paymentForm.get('reference')!.disabled).toBe(true);
        });

        it('is enabled only for a bank transfer', () => {
            component.paymentForm.patchValue({ method: 'transferencia' });
            expect(component.paymentForm.get('reference')!.enabled).toBe(true);

            component.paymentForm.patchValue({ method: 'efectivo' });
            expect(component.paymentForm.get('reference')!.disabled).toBe(true);
        });

        it('is cleared when switching from transfer to cash', () => {
            component.paymentForm.patchValue({ method: 'transferencia' });
            component.paymentForm.patchValue({ reference: 'TRX-0001' });

            component.paymentForm.patchValue({ method: 'efectivo' });

            expect(component.paymentForm.get('reference')!.value).toBe('');
        });
    });

    describe('already paid period', () => {
        it('loads the history of the selected player', () => {
            component.selectPlayer(PLAYER);

            expect(paymentService.getPaymentsByPlayer).toHaveBeenCalledWith(577);
        });

        it('blocks a period the player already paid', () => {
            fillForm('efectivo', 7);

            expect(component.paymentForm.hasError('periodAlreadyPaid')).toBe(true);
            expect(component.paymentForm.invalid).toBe(true);
            expect(component.selectedPeriodLabel()).toBe('Julio 2026');
        });

        it('allows a period the player did not pay', () => {
            fillForm('efectivo', 8);

            expect(component.paymentForm.hasError('periodAlreadyPaid')).toBe(false);
            expect(component.paymentForm.valid).toBe(true);
        });
    });

    describe('display helpers', () => {
        it('formats the period as month and year', () => {
            expect(component.formatPeriod('2026-07')).toBe('Julio 2026');
            expect(component.formatPeriod('2026-12')).toBe('Diciembre 2026');
            expect(component.formatPeriod(null)).toBe('Sin período');
        });

        it('shows the method with its label', () => {
            expect(component.methodLabel('efectivo')).toBe('Efectivo');
            expect(component.methodLabel('transferencia')).toBe('Transferencia');
            expect(component.methodLabel('cheque')).toBe('cheque');
        });
    });

    describe('submit', () => {
        it('does not call the API while the form is invalid', () => {
            component.onSubmit();

            expect(paymentService.createPayment).not.toHaveBeenCalled();
        });

        it('sends a cash payment without reference', () => {
            paymentService.createPayment.mockReturnValue(of({ ...PAID_JULY, period: '2026-08' }));
            fillForm('efectivo', 8);

            component.onSubmit();

            expect(paymentService.createPayment).toHaveBeenCalledWith({
                playerId: 577,
                periodYear: 2026,
                periodMonth: 8,
                amount: 85000,
                method: 'efectivo',
                reference: null,
            });
        });

        it('sends a transfer with the reference trimmed', () => {
            paymentService.createPayment.mockReturnValue(of({ ...PAID_JULY, period: '2026-08' }));
            fillForm('transferencia', 8, '  TRX-0001  ');

            component.onSubmit();

            expect(paymentService.createPayment.mock.calls[0][0].reference).toBe('TRX-0001');
        });

        it('after a successful payment resets the form, shows the toast and reloads the list', () => {
            paymentService.createPayment.mockReturnValue(of({ ...PAID_JULY, period: '2026-08' }));
            const latestCallsBefore = paymentService.getLatestPayments.mock.calls.length;
            fillForm('efectivo', 8);

            component.onSubmit();

            expect(component.toastKind).toBe('success');
            expect(component.toastMessage).toBe('Pago de VENICA COY, GUILLERMINA registrado: Agosto 2026.');
            expect(component.paymentForm.get('player')!.value).toBe('');
            expect(component.submitting).toBe(false);
            expect(paymentService.getLatestPayments.mock.calls.length).toBe(latestCallsBefore + 1);
        });

        it.each([
            [0, 'No se pudo conectar con el servidor. Intentá nuevamente.'],
            [401, 'Tu sesión venció. Iniciá sesión nuevamente para registrar el pago.'],
            [400, 'No se pudo registrar el pago: el período ya está abonado o hay datos inválidos.'],
            [500, 'Ocurrió un error al registrar el pago. Intentá nuevamente.'],
        ])('shows an error toast for status %i', (status, message) => {
            paymentService.createPayment.mockReturnValue(throwError(() => new HttpErrorResponse({ status })));
            fillForm('efectivo', 8);

            component.onSubmit();

            expect(component.toastKind).toBe('error');
            expect(component.toastMessage).toBe(message);
            expect(component.submitting).toBe(false);
        });
    });
});
