import { signalStore, withState, withMethods, patchState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { inject } from '@angular/core';
import { pipe, switchMap, tap } from 'rxjs';
import { NewsArticle, NewsService } from '../features/news.service';

export interface NewsState {
  articles: NewsArticle[];
  isLoading: boolean;
  error: string | null;
}

const initialState: NewsState = {
  articles: [],
  isLoading: false,
  error: null,
};

export const NewsStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withMethods((store, newsService = inject(NewsService)) => ({
    loadArticles: rxMethod<void>(
      pipe(
        tap(() => patchState(store, { isLoading: true })),
        switchMap(() => 
          newsService.getArticles().pipe(
            tap({
              next: (articles) => patchState(store, { articles, isLoading: false }),
              error: (err) => patchState(store, { error: err.message, isLoading: false })
            })
          )
        )
      )
    )
  }))
);
