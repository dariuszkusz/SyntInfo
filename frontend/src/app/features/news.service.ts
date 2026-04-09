import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export enum SourceRegion {
  Poland = 0,
  World = 1
}

export interface NewsArticle {
  id: string;
  title: string;
  originalTitle: string;
  summaryText: string;
  publishedAt: string;
  sourceUrls: string[];
  categoryName: string;
  region: SourceRegion;
}

export interface TopNewsResponse {
  poland: NewsArticle[];
  world: NewsArticle[];
}

@Injectable({
  providedIn: 'root'
})
export class NewsService {
  private http = inject(HttpClient);
  private apiUrl = '/api/news'; 

  getArticles(page: number = 1, pageSize: number = 20, region?: SourceRegion): Observable<NewsArticle[]> {
    const params: any = { page, pageSize };
    if (region !== undefined) {
      params.region = region;
    }
    return this.http.get<NewsArticle[]>(this.apiUrl, { params });
  }

  getTopNews(): Observable<TopNewsResponse> {
    return this.http.get<TopNewsResponse>(`${this.apiUrl}/top`);
  }

  syncNews(): Observable<any> {
    return this.http.post(`${this.apiUrl}/sync`, {});
  }
}
