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
  // carries the reason. The benefit panel turns this on to read "Becado
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

  // Catálogo simplificado a dos categorías (HU-033): Becado en violeta, Descuento en
  // ámbar. Los nombres de motivo viejos (Beca Completa, Media Beca, Descuento por
  // Hermanos) ya no existen en TIPO_DESCUENTO tras la migración, pero un registro
  // histórico que por algún motivo todavía los tuviera cae en tone-default en vez de
  // romper — nunca se deja un beneficio sin badge por un nombre que no matchea.
  get toneClass(): string {
    switch (this.discount?.type) {
      case 'Becado':
        return 'tone-becado';
      case 'Descuento':
        return 'tone-descuento';
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
