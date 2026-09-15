import { Component, Input } from '@angular/core';
import { DiscountModel, formatBenefitValue } from '../../models/DiscountModel';

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

    get visible(): boolean {
        return this.discount !== null && this.discount.isActive;
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

        const from = this.formatDate(this.discount.startDate);

        // An empty end date means the benefit runs until somebody cancels it
        if (!this.discount.endDate) {
            return from ? `Vigente desde ${from}, sin fecha de vencimiento` : 'Vigente, sin fecha de vencimiento';
        }

        const to = this.formatDate(this.discount.endDate);
        return `Vigente desde ${from} hasta ${to}`;
    }

    // The backend sends ISO (yyyy-MM-dd) and the view shows it as read here
    private formatDate(isoDate: string): string {
        if (!isoDate) {
            return '';
        }
        const [year, month, day] = isoDate.split('-');
        return `${day}/${month}/${year}`;
    }
}
