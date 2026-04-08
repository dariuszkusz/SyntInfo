import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { NewsStore } from '../../state/news.store';

@Component({
  selector: 'app-news-feed',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="min-h-screen bg-aux1 text-aux2 font-sans selection:bg-primary/30">
      <!-- Header -->
      <header class="sticky top-0 z-50 bg-white/80 backdrop-blur-md border-b border-primary/20 p-4">
        <div class="max-w-4xl mx-auto flex justify-between items-center">
          <h1 class="text-2xl font-bold tracking-tight text-aux3">
            Synt<span class="text-primary italic">Info</span>
          </h1>
          <div class="text-xs uppercase tracking-widest font-semibold opacity-60">
            Automated AI News Aggregator
          </div>
        </div>
      </header>

      <!-- Main Feed -->
      <main class="max-w-4xl mx-auto p-4 py-8">
        @if (store.isLoading()) {
          <div class="flex flex-col items-center justify-center py-20 space-y-4">
            <div class="animate-spin rounded-full h-12 w-12 border-b-2 border-primary"></div>
            <p class="text-sm italic animate-pulse">Syncing the world's news...</p>
          </div>
        } @else if (store.error()) {
          <div class="bg-red-50 border border-red-200 text-red-700 p-4 rounded-xl">
            <p>Error loading news: {{ store.error() }}</p>
          </div>
        } @else {
          <!-- Section: Poland -->
          <section class="mb-12">
            <div class="flex items-center space-x-3 mb-6">
              <span class="text-2xl">🇵🇱</span>
              <h2 class="text-xl font-bold uppercase tracking-widest text-aux3">Polska</h2>
              <div class="h-px flex-1 bg-primary/20"></div>
            </div>
            
            <div class="grid gap-6">
              @for (article of store.polandArticles(); track article.id) {
                <ng-container *ngTemplateOutlet="articleCard; context: { $implicit: article }"></ng-container>
              } @empty {
                <div class="text-center py-10 opacity-40 italic">
                  <p>Brak najnowszych wiadomości z Polski.</p>
                </div>
              }
            </div>
          </section>

          <!-- Section: World -->
          <section>
            <div class="flex items-center space-x-3 mb-6">
              <span class="text-2xl">🌍</span>
              <h2 class="text-xl font-bold uppercase tracking-widest text-aux3">Świat</h2>
              <div class="h-px flex-1 bg-primary/20"></div>
            </div>
            
            <div class="grid gap-6">
              @for (article of store.worldArticles(); track article.id) {
                <ng-container *ngTemplateOutlet="articleCard; context: { $implicit: article }"></ng-container>
              } @empty {
                <div class="text-center py-10 opacity-40 italic">
                  <p>Brak najnowszych wiadomości ze świata.</p>
                </div>
              }
            </div>
          </section>
        }
      </main>

      <!-- Article Card Template -->
      <ng-template #articleCard let-article>
        <article class="group bg-white rounded-2xl p-6 shadow-sm hover:shadow-xl transition-all duration-500 border border-transparent hover:border-primary/30">
          <div class="flex justify-between items-start mb-3">
            <span class="px-2 py-1 bg-primary/10 text-primary text-[10px] font-bold uppercase tracking-wider rounded">
              {{ article.categoryName }}
            </span>
            <time class="text-[10px] opacity-40 font-mono">{{ article.publishedAt | date:'short' }}</time>
          </div>
          
          <h2 class="text-xl font-bold mb-3 leading-tight group-hover:text-primary transition-colors">
            {{ article.title }}
          </h2>
          
          <p class="text-sm leading-relaxed opacity-80 mb-6 line-clamp-3">
            {{ article.summaryText }}
          </p>

          <div class="flex items-center justify-between">
            <div class="flex -space-x-2">
              @for (url of article.sourceUrls.slice(0, 3); track url) {
                <div class="w-6 h-6 rounded-full bg-aux1 border-2 border-white flex items-center justify-center overflow-hidden">
                  <span class="text-[8px] font-bold">{{ url.charAt(0).toUpperCase() }}</span>
                </div>
              }
            </div>
            <button class="text-xs font-bold uppercase tracking-widest text-aux3 hover:text-primary flex items-center group/btn">
              Czytaj więcej 
              <span class="ml-1 group-hover/btn:translate-x-1 transition-transform">→</span>
            </button>
          </div>
        </article>
      </ng-template>

      <!-- Floating Action Button for Mobile / PWA -->
      <button 
        (click)="store.loadTopNews()"
        class="fixed bottom-6 right-6 w-14 h-14 bg-aux3 text-white rounded-full shadow-2xl flex items-center justify-center hover:scale-110 active:scale-95 transition-all lg:hidden">
        <span class="text-xl">⚡</span>
      </button>
    </div>
  `,
  styles: [
    `
    :host {
      display: block;
    }
    `
  ]
})
export class NewsFeedComponent implements OnInit {
  readonly store = inject(NewsStore);

  ngOnInit() {
    this.store.loadTopNews();
  }
}
