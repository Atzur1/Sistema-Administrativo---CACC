import { Component, Input } from '@angular/core';
import { DiscountModel } from '../../models/DiscountModel';

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

    get visible(): boolean {
        return this.discount !== null && this.discount.isActive;
    }

    get tooltipText(): string {
        if (this.discount === null) {
            return '';
        }
        const from = this.formatDate(this.discount.startDate);
        const to = this.formatDate(this.discount.endDate);
        return `Vigente desde ${from} hasta ${to}`;
    }

    // The backend sends ISO (yyyy-MM-dd) and the view shows it as read here
    private formatDate(isoDate: string): string {
        const [year, month, day] = isoDate.split('-');
        return `${day}/${month}/${year}`;
    }
}
