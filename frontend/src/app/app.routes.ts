import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./features/news-feed/news-feed.component').then(m => m.NewsFeedComponent)
  }
];
