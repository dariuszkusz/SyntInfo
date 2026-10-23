import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface NewsletterResponse {
  success: boolean;
  message: string;
}

@Injectable({
  providedIn: 'root'
})
export class NewsletterService {
  private http = inject(HttpClient);
  private apiUrl = '/api/newsletter';

  subscribe(email: string, name?: string): Observable<NewsletterResponse> {
    return this.http.post<NewsletterResponse>(`${this.apiUrl}/subscribe`, { email, name });
  }

  unsubscribe(email: string): Observable<NewsletterResponse> {
    return this.http.post<NewsletterResponse>(`${this.apiUrl}/unsubscribe`, { email });
  }
}
