import { signalStore, withState, withMethods, withComputed, patchState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { inject, computed } from '@angular/core';
import { pipe, switchMap, tap } from 'rxjs';
import { NewsArticle, NewsService, TopNewsResponse } from '../features/news.service';

export type RegionFilter = 'all' | 'poland' | 'world' | 'positive';

export interface NewsState {
  articles: NewsArticle[];
  polandArticles: NewsArticle[];
  worldArticles: NewsArticle[];
  positiveArticles: NewsArticle[];
  selectedRegion: RegionFilter;
  selectedCategory: string | null;
  isLoading: boolean;
  error: string | null;
}

const REGION_STORAGE_KEY = 'syntinfo_region_filter';

function getStoredRegion(): RegionFilter {
  try {
    const val = localStorage.getItem(REGION_STORAGE_KEY);
    if (val === 'all' || val === 'poland' || val === 'world' || val === 'positive') {
      return val;
    }
  } catch {
    // ignore local storage errors in SSR or restricted environments
  }
  return 'all';
}

const initialState: NewsState = {
  articles: [],
  polandArticles: [],
  worldArticles: [],
  positiveArticles: [],
  selectedRegion: getStoredRegion(),
  selectedCategory: null,
  isLoading: false,
  error: null,
};

export const NewsStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withComputed((store) => {
    const allSortedArticles = computed(() => {
      const combined = [...store.polandArticles(), ...store.worldArticles(), ...store.positiveArticles()];
      const uniqueMap = new Map<string, NewsArticle>();
      for (const item of combined) {
        if (!uniqueMap.has(item.id)) {
          uniqueMap.set(item.id, item);
        }
      }
      return Array.from(uniqueMap.values()).sort(
        (a, b) => new Date(b.publishedAt).getTime() - new Date(a.publishedAt).getTime()
      );
    });

    const filteredArticles = computed(() => {
      const region = store.selectedRegion();
      const category = store.selectedCategory();

      let baseList: NewsArticle[];
      if (region === 'poland') {
        baseList = store.polandArticles();
      } else if (region === 'world') {
        baseList = store.worldArticles();
      } else if (region === 'positive') {
        baseList = store.positiveArticles();
      } else {
        baseList = allSortedArticles();
      }

      if (!category) {
        return baseList;
      }

      return baseList.filter(
        (art) => art.categoryName.toLowerCase().trim() === category.toLowerCase().trim()
      );
    });

    const polandCount = computed(() => store.polandArticles().length);
    const worldCount = computed(() => store.worldArticles().length);
    const positiveCount = computed(() => store.positiveArticles().length);
    const totalCount = computed(() => allSortedArticles().length);

    const availableCategories = computed(() => {
      const counts = new Map<string, number>();
      for (const art of allSortedArticles()) {
        const name = art.categoryName.trim();
        if (name) {
          counts.set(name, (counts.get(name) || 0) + 1);
        }
      }
      return Array.from(counts.entries())
        .map(([name, count]) => ({ name, count }))
        .sort((a, b) => b.count - a.count);
    });

    return {
      allSortedArticles,
      filteredArticles,
      polandCount,
      worldCount,
      positiveCount,
      totalCount,
      availableCategories,
    };
  }),
  withMethods((store, newsService = inject(NewsService)) => ({
    setRegionFilter(region: RegionFilter) {
      patchState(store, { selectedRegion: region });
      try {
        localStorage.setItem(REGION_STORAGE_KEY, region);
      } catch {
        // ignore
      }
    },
    setCategoryFilter(category: string | null) {
      patchState(store, { selectedCategory: category });
    },
    toggleCategoryFilter(category: string) {
      const current = store.selectedCategory();
      if (current?.toLowerCase().trim() === category.toLowerCase().trim()) {
        patchState(store, { selectedCategory: null });
      } else {
        patchState(store, { selectedCategory: category.trim() });
      }
    },
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
    ),
    loadTopNews: rxMethod<void>(
      pipe(
        tap(() => patchState(store, { isLoading: true })),
        switchMap(() => 
          newsService.getTopNews().pipe(
            tap({
              next: (response: TopNewsResponse) => patchState(store, { 
                polandArticles: response.poland ?? [], 
                worldArticles: response.world ?? [], 
                positiveArticles: response.positive ?? [],
                isLoading: false 
              }),
              error: (err: any) => patchState(store, { error: err.message, isLoading: false })
            })
          )
        )
      )
    ),
    syncNews: rxMethod<void>(
      pipe(
        tap(() => console.log('Ręczna synchronizacja uruchomiona...')),
        switchMap(() => 
          newsService.syncNews().pipe(
            tap({
              next: () => console.log('Synchronizacja zakończona powodzeniem (proces tła).'),
              error: (err: any) => console.error('Błąd synchronizacji:', err)
            })
          )
        )
      )
    ),
    clearNews: rxMethod<void>(
      pipe(
        tap(() => console.log('Czyszczenie bazy danych...')),
        switchMap(() => 
          newsService.clearNews().pipe(
            tap({
              next: () => {
                console.log('Baza danych wyczyszczona.');
                patchState(store, { 
                  polandArticles: [], 
                  worldArticles: [],
                  positiveArticles: [],
                  articles: [] 
                });
              },
              error: (err: any) => console.error('Błąd podczas czyszczenia bazy:', err)
            })
          )
        )
      )
    )
  }))
);
