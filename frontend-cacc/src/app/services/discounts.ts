import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { DiscountModel } from '../models/DiscountModel';

@Injectable({
    providedIn: 'root',
})
export class DiscountService {
    readonly API_URL = 'http://localhost:5118/api';

    constructor(private http: HttpClient) {}

    getActiveDiscounts(): Observable<DiscountModel[]> {
        return this.http.get<DiscountModel[]>(`${this.API_URL}/players/discounts`);
    }

    // Every assignment ever granted, active and expired alike, for the
    // administration table.
    getAllDiscounts(): Observable<DiscountModel[]> {
        return this.http.get<DiscountModel[]>(`${this.API_URL}/players/discounts/all`);
    }

    // The grids look discounts up by player, so they are handed over indexed and
    // walked only once. Avoids filtering the whole array on every rendered row.
    getDiscountMap(): Observable<Map<number, DiscountModel>> {
        return this.getActiveDiscounts().pipe(
            map((discounts) => {
                const discountMap = new Map<number, DiscountModel>();
                discounts.forEach((discount) => {
                    discountMap.set(discount.playerId, discount);
                });
                return discountMap;
            })
        );
    }
}
