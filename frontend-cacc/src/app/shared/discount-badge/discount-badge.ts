import { Component, Input } from '@angular/core';
import { DiscountModel, formatBenefitValue, formatIsoDate } from '../../models/DiscountModel';

@Component({
    selector: 'app-discount-badge',
    standalone: true,
    imports: [],
    templateUrl: './discount-badge.html',
    styleUrl: './discount-badge.css',
})
export class DiscountBadge {

    // With no active discount the component renders nothing, so the badge
    // disappears on its own when it expires or is deactivated, leaving no
    // residual nodes behind.
    @Input() discount: DiscountModel | null = null;

    // The grid already has a column with the amount, so there the badge only
    // carries the reason. The benefit panel turns this on to read "Media Beca
    // 50 %" in a single label.
    @Input() showValue = false;

    // Only a benefit that applies today gets a badge. A scheduled one has not
    // started and an expired one no longer counts, so labelling either would
    // claim a reduction that is not being applied.
    get visible(): boolean {
        return this.discount !== null && this.discount.status === 'Active';
    }

    get value(): string {
        return this.discount === null ? '' : formatBenefitValue(this.discount);
    }

    // One modifier per reason so each benefit is recognisable at a glance
    get toneClass(): string {
        switch (this.discount?.type) {
            case 'Beca Completa':
                return 'tone-full';
            case 'Media Beca':
                return 'tone-half';
            case 'Descuento por Hermanos':
                return 'tone-siblings';
            default:
                return 'tone-default';
        }
    }

    get tooltipText(): string {
        if (this.discount === null) {
            return '';
        }

        // Both dates are mandatory since HU-012, so the period always reads whole
        const from = formatIsoDate(this.discount.startDate);
        const to = formatIsoDate(this.discount.endDate);

        return `Vigente desde ${from} hasta ${to}`;
    }
}
