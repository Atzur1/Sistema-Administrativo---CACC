import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { PlayerModel } from '../models/PlayerModel';

@Injectable({
    providedIn: 'root',
})
export class PlayerService {
    readonly API_URL = 'http://localhost:5118/api';

    constructor(private http: HttpClient) {}

    getPlayers(): Observable<PlayerModel[]> {
        return this.http.get<PlayerModel[]>(`${this.API_URL}/players`);
    }
}
