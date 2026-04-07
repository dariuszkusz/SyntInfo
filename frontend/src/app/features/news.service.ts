import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface NewsArticle {
  id: string;
  title: string;
  summaryText: string;
  publishedAt: string;
  sourceUrls: string[];
  categoryName: string;
}

@Injectable({
  providedIn: 'root'
})
export class NewsService {
  private http = inject(HttpClient);
  // In development we might need a proxy or full URL
  private apiUrl = '/api/news'; 

  getArticles(page: number = 1, pageSize: number = 20): Observable<NewsArticle[]> {
    return this.http.get<NewsArticle[]>(this.apiUrl, {
      params: { page, pageSize }
    });
  }
}
