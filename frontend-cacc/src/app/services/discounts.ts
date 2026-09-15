import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { DiscountModel, DiscountRequest } from '../models/DiscountModel';

@Injectable({
    providedIn: 'root',
})
export class DiscountService {
    readonly API_URL = 'http://localhost:5118/api';

    constructor(private http: HttpClient) {}

    getActiveDiscounts(): Observable<DiscountModel[]> {
        return this.http.get<DiscountModel[]>(`${this.API_URL}/players/discounts`);
    }

    // Every assignment ever granted, active and cancelled alike, for the
    // administration table.
    getAllDiscounts(): Observable<DiscountModel[]> {
        return this.http.get<DiscountModel[]>(`${this.API_URL}/players/discounts/all`);
    }

    // The reasons the club accepts. They come from the catalogue table instead of
    // a list hardcoded here, so the form can never offer a motive the API
    // rejects.
    getDiscountTypes(): Observable<string[]> {
        return this.http.get<string[]>(`${this.API_URL}/players/discounts/types`);
    }

    // The benefit in force for a player, the way HU-014 defined it: an expired or
    // cancelled one answers 404. A 404 means "has none", and the caller turns it
    // into null rather than an error.
    getDiscountByPlayer(playerId: number): Observable<DiscountModel> {
        return this.http.get<DiscountModel>(`${this.API_URL}/players/${playerId}/discount`);
    }

    // The open assignment of a player, in force or already expired. The popup
    // asks for this one: an expired benefit still occupies the only active slot,
    // so it has to be shown instead of offering an empty form.
    getAssignedDiscount(playerId: number): Observable<DiscountModel> {
        return this.http.get<DiscountModel>(
            `${this.API_URL}/players/${playerId}/discount?includeExpired=true`
        );
    }

    assignDiscount(playerId: number, request: DiscountRequest): Observable<DiscountModel> {
        return this.http.post<DiscountModel>(`${this.API_URL}/players/${playerId}/discount`, request);
    }

    updateDiscount(playerId: number, request: DiscountRequest): Observable<DiscountModel> {
        return this.http.put<DiscountModel>(`${this.API_URL}/players/${playerId}/discount`, request);
    }

    // Answers 204 with no body: the benefit is kept as history and only flipped
    // to inactive.
    cancelDiscount(playerId: number): Observable<void> {
        return this.http.delete<void>(`${this.API_URL}/players/${playerId}/discount`);
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
