import { Component, OnInit, inject, isDevMode } from '@angular/core';
import { CommonModule } from '@angular/common';
import { NewsStore } from '../../state/news.store';
import { NgIconComponent, provideIcons } from '@ng-icons/core';
import { flagPl } from '@ng-icons/flag-icons';
import { lucideGlobe } from '@ng-icons/lucide';

@Component({
  selector: 'app-news-feed',
  standalone: true,
  imports: [CommonModule, NgIconComponent],
  providers: [provideIcons({ flagPl, lucideGlobe })],
  template: `
    <div class="min-h-screen bg-aux1 text-aux2 font-sans selection:bg-primary/30">
      <!-- Header -->
      <header class="sticky top-0 z-50 bg-white/80 backdrop-blur-md border-b border-primary/20 p-4">
        <div class="max-w-4xl mx-auto flex justify-between items-center">
          <div class="flex items-center space-x-3">
            <img src="assets/favicon.ico" alt="InfoSkrót Logo" class="w-8 h-8 rounded" />
            <div class="flex flex-col">
              <h1 class="text-2xl font-bold tracking-tight text-aux3 leading-none">
                Info<span class="text-primary italic">Skrót</span>
              </h1>
              <span class="text-[10px] uppercase tracking-widest font-semibold opacity-60 mt-1">
                Skrót najważniejszych informacji
              </span>
            </div>
          </div>
          <div class="flex items-center space-x-4">
            @if (isDevMode) {
              <button 
                (click)="store.syncNews()"
                class="text-[10px] px-3 py-1 bg-primary/10 text-primary border border-primary/20 rounded-full hover:bg-primary/20 transition-all font-bold uppercase tracking-wider">
                Sync On Demand
              </button>
              <button 
                (click)="store.clearNews()"
                class="text-[10px] px-3 py-1 bg-red-500/10 text-red-500 border border-red-500/20 rounded-full hover:bg-red-500/20 transition-all font-bold uppercase tracking-wider">
                Wyczyść bazę
              </button>
            }
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
              <ng-icon name="flagPl" class="text-2xl"></ng-icon>
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
              <ng-icon name="lucideGlobe" class="text-2xl text-primary"></ng-icon>
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
        <article class="bg-white rounded-3xl p-8 mb-4 border border-aux1/50 transition-all">
          <div class="flex justify-between items-center mb-6">
            <span class="px-3 py-1 bg-aux1 text-aux3 text-[10px] font-bold uppercase tracking-[0.2em] rounded-full">
              {{ article.categoryName }}
            </span>
            <time class="text-[10px] opacity-30 font-medium">{{ article.publishedAt | date:'short' }}</time>
          </div>
          
          <h2 class="text-2xl font-bold mb-6 leading-tight text-aux3">
            {{ article.title }}
          </h2>
          
          <div class="text-[15px] leading-relaxed text-aux2 opacity-90 mb-8 whitespace-pre-wrap tracking-wide space-y-4">
            {{ article.summaryText }}
          </div>

          <div class="flex items-center justify-between pt-6 border-t border-aux1/30">
            <div class="flex items-center space-x-3">
              @for (url of article.sourceUrls; track url) {
                <a [href]="url" target="_blank" rel="noopener" class="group/icon relative">
                  <div class="w-8 h-8 rounded-xl bg-aux1/30 flex items-center justify-center hover:bg-aux1 transition-colors">
                    <img 
                      [src]="'https://www.google.com/s2/favicons?domain=' + url + '&sz=64'" 
                      class="w-4 h-4 grayscale group-hover/icon:grayscale-0 transition-all"
                      alt="source">
                  </div>
                  <span class="absolute -top-8 left-1/2 -translate-x-1/2 bg-aux3 text-white text-[8px] px-2 py-1 rounded opacity-0 group-hover/icon:opacity-100 transition-opacity whitespace-nowrap">
                    Otwórz źródło
                  </span>
                </a>
              }
            </div>
            
            <div class="text-[9px] uppercase tracking-widest font-bold opacity-30 italic">
              AI Summarized Essence
            </div>
          </div>
        </article>
      </ng-template>

      <!-- Floating Action Button for Mobile / PWA -->
      <button 
        (click)="store.loadTopNews()"
        class="fixed bottom-8 right-8 w-16 h-16 bg-aux3 text-white rounded-2xl shadow-2xl flex items-center justify-center hover:scale-105 active:scale-95 transition-all lg:hidden z-50">
        <span class="text-2xl">⚡</span>
      </button>
    </div>
  `,
  styles: [
    `
    :host {
      display: block;
      background-color: #f4f7f6; /* Odświeżenie tła na lżejsze */
    }
    
    .bg-aux1 { background-color: #e9edeb; }
    .text-aux2 { color: #2d3436; }
    .text-aux3 { color: #1e272e; }
    .text-primary { color: #00b894; }
    `
  ]
})
export class NewsFeedComponent implements OnInit {
  readonly store = inject(NewsStore);
  readonly isDevMode = isDevMode();

  ngOnInit() {
    this.store.loadTopNews();
  }
}
