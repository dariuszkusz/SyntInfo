import { signalStore, withState, withMethods, patchState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { inject } from '@angular/core';
import { pipe, switchMap, tap, catchError, of } from 'rxjs';
import { NewsletterService, NewsletterResponse } from '../features/newsletter/newsletter.service';

export type NewsletterMode = 'subscribe' | 'unsubscribe';

export interface NewsletterState {
  mode: NewsletterMode;
  isLoading: boolean;
  status: 'idle' | 'success' | 'error';
  message: string | null;
}

const initialState: NewsletterState = {
  mode: 'subscribe',
  isLoading: false,
  status: 'idle',
  message: null
};

export const NewsletterStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withMethods((store, newsletterService = inject(NewsletterService)) => ({
    setMode(mode: NewsletterMode) {
      patchState(store, { mode, status: 'idle', message: null });
    },

    reset() {
      patchState(store, initialState);
    },

    subscribeEmail: rxMethod<{ email: string; name?: string }>(
      pipe(
        tap(() => patchState(store, { isLoading: true, status: 'idle', message: null })),
        switchMap(({ email, name }) =>
          newsletterService.subscribe(email, name).pipe(
            tap((response: NewsletterResponse) => {
              patchState(store, {
                isLoading: false,
                status: response.success ? 'success' : 'error',
                message: response.message
              });
            }),
            catchError((err) => {
              const errMsg = err?.error?.message || 'Wystąpił błąd podczas zapisu do newslettera.';
              patchState(store, {
                isLoading: false,
                status: 'error',
                message: errMsg
              });
              return of(null);
            })
          )
        )
      )
    ),

    unsubscribeEmail: rxMethod<{ email: string }>(
      pipe(
        tap(() => patchState(store, { isLoading: true, status: 'idle', message: null })),
        switchMap(({ email }) =>
          newsletterService.unsubscribe(email).pipe(
            tap((response: NewsletterResponse) => {
              patchState(store, {
                isLoading: false,
                status: response.success ? 'success' : 'error',
                message: response.message
              });
            }),
            catchError((err) => {
              const errMsg = err?.error?.message || 'Wystąpił błąd podczas wypisywania z newslettera.';
              patchState(store, {
                isLoading: false,
                status: 'error',
                message: errMsg
              });
              return of(null);
            })
          )
        )
      )
    )
  }))
);
