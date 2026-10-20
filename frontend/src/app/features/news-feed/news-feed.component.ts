import { Component, OnInit, inject, isDevMode } from '@angular/core';
import { CommonModule } from '@angular/common';
import { NewsStore } from '../../state/news.store';
import { NgIconComponent, provideIcons } from '@ng-icons/core';
import { flagPl } from '@ng-icons/flag-icons';
import { lucideGlobe, lucideLayers, lucideX, lucideFilter } from '@ng-icons/lucide';
import { BuyMeCoffeeButtonComponent } from '../../shared/components/buy-me-coffee-button/buy-me-coffee-button.component';
import { NewsletterBoxComponent } from '../../shared/components/newsletter-box/newsletter-box.component';

@Component({
  selector: 'app-news-feed',
  standalone: true,
  imports: [CommonModule, NgIconComponent, BuyMeCoffeeButtonComponent, NewsletterBoxComponent],
  providers: [provideIcons({ flagPl, lucideGlobe, lucideLayers, lucideX, lucideFilter })],
  template: `
    <div class="flex flex-col min-h-screen bg-aux1 text-aux2 font-sans selection:bg-primary/30">
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
            <app-buy-me-coffee-button></app-buy-me-coffee-button>
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
      <main class="flex-grow max-w-4xl w-full mx-auto p-4 py-8">
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
          <!-- Filter Controls Bar -->
          <div class="flex flex-wrap items-center justify-between gap-4 mb-8">
            <!-- Region Filter Tabs -->
            <div class="inline-flex p-1 bg-white/80 backdrop-blur border border-aux1/80 rounded-2xl shadow-sm">
              <button 
                (click)="store.setRegionFilter('all')"
                [attr.aria-pressed]="store.selectedRegion() === 'all'"
                [ngClass]="store.selectedRegion() === 'all' 
                  ? 'bg-aux3 text-white shadow-sm' 
                  : 'text-aux2 hover:bg-aux1/40'"
                class="region-tab-all flex items-center space-x-1.5 px-4 py-2 rounded-xl text-xs font-bold tracking-wide transition-all">
                <ng-icon name="lucideLayers" class="text-sm"></ng-icon>
                <span>Wszystkie</span>
                <span class="ml-1 text-[10px] opacity-75 font-semibold">({{ store.totalCount() }})</span>
              </button>
              <button 
                (click)="store.setRegionFilter('poland')"
                [attr.aria-pressed]="store.selectedRegion() === 'poland'"
                [ngClass]="store.selectedRegion() === 'poland' 
                  ? 'bg-aux3 text-white shadow-sm' 
                  : 'text-aux2 hover:bg-aux1/40'"
                class="region-tab-poland flex items-center space-x-1.5 px-4 py-2 rounded-xl text-xs font-bold tracking-wide transition-all">
                <ng-icon name="flagPl" class="text-sm"></ng-icon>
                <span>Polska</span>
                <span class="ml-1 text-[10px] opacity-75 font-semibold">({{ store.polandCount() }})</span>
              </button>
              <button 
                (click)="store.setRegionFilter('world')"
                [attr.aria-pressed]="store.selectedRegion() === 'world'"
                [ngClass]="store.selectedRegion() === 'world' 
                  ? 'bg-aux3 text-white shadow-sm' 
                  : 'text-aux2 hover:bg-aux1/40'"
                class="region-tab-world flex items-center space-x-1.5 px-4 py-2 rounded-xl text-xs font-bold tracking-wide transition-all">
                <ng-icon name="lucideGlobe" class="text-sm text-primary"></ng-icon>
                <span>Świat</span>
                <span class="ml-1 text-[10px] opacity-75 font-semibold">({{ store.worldCount() }})</span>
              </button>
            </div>

            <!-- Active Category Filter Badge -->
            @if (store.selectedCategory()) {
              <div class="active-category-chip flex items-center space-x-2 bg-primary/10 border border-primary/30 text-primary px-3 py-1.5 rounded-xl text-xs font-semibold">
                <span>Kategoria: <strong>{{ store.selectedCategory() }}</strong></span>
                <button 
                  (click)="store.setCategoryFilter(null)"
                  title="Wyczyść filtr kategorii"
                  class="clear-category-btn hover:bg-primary/20 p-1 rounded-full transition-colors flex items-center justify-center">
                  <ng-icon name="lucideX" class="text-xs"></ng-icon>
                </button>
              </div>
            }
          </div>

          <!-- Feed Container -->
          <div class="grid gap-6">
            @for (article of store.filteredArticles(); track article.id) {
              <article 
                [ngClass]="article.region === 1 
                  ? 'bg-[#faf7f0] border-[#ebe4d8] shadow-sm world-article' 
                  : 'bg-white border-aux1/50 shadow-sm poland-article'"
                class="article-card rounded-3xl p-6 sm:p-8 border transition-all hover:shadow-md">
                
                <!-- Card Top Meta -->
                <div class="flex justify-between items-center mb-5">
                  <button 
                    (click)="store.toggleCategoryFilter(article.categoryName)"
                    [title]="'Filtruj wg: ' + article.categoryName"
                    [ngClass]="store.selectedCategory()?.toLowerCase() === article.categoryName.toLowerCase() 
                      ? 'bg-aux3 text-white ring-2 ring-primary/40' 
                      : 'bg-aux1/80 hover:bg-aux1 text-aux3'"
                    class="category-btn px-3 py-1 text-[10px] font-bold uppercase tracking-[0.2em] rounded-full transition-all">
                    {{ article.categoryName }}
                  </button>

                  <div class="flex items-center space-x-2 text-[10px] font-bold uppercase tracking-wider text-aux2/60">
                    @if (article.region === 1) {
                      <span class="region-badge-world inline-flex items-center gap-1 text-amber-900/80 bg-amber-100/70 px-2 py-0.5 rounded-md">
                        <ng-icon name="lucideGlobe" class="text-xs text-primary"></ng-icon> Świat
                      </span>
                    } @else {
                      <span class="region-badge-poland inline-flex items-center gap-1 text-aux3/80 bg-aux1/70 px-2 py-0.5 rounded-md">
                        <ng-icon name="flagPl" class="text-xs"></ng-icon> Polska
                      </span>
                    }
                    <time class="opacity-50 font-medium">{{ article.publishedAt | date:'short' }}</time>
                  </div>
                </div>
                
                <!-- Article Body: 2/3 text on left, 1/3 image on right -->
                <div class="flex flex-col-reverse md:flex-row gap-6 items-start mb-6">
                  <!-- Text Column: 2/3 width on md+ -->
                  <div class="article-content-col flex-1 w-full md:w-2/3 flex flex-col">
                    <h2 class="article-title text-xl sm:text-2xl font-bold mb-4 leading-tight text-aux3">
                      {{ article.title }}
                    </h2>
                    <div class="article-summary text-[15px] leading-relaxed text-aux2 opacity-90 whitespace-pre-wrap tracking-wide text-justify hyphens-auto">{{ article.summaryText.trim() }}</div>
                  </div>

                  <!-- Image Column: 1/3 width on md+ -->
                  @if (article.imageUrl) {
                    <div class="article-image-col w-full md:w-1/3 shrink-0">
                      <div class="overflow-hidden rounded-2xl bg-aux1/20 border border-black/5 aspect-[4/3] w-full">
                        <img 
                          [src]="article.imageUrl" 
                          [alt]="article.title"
                          loading="lazy"
                          (error)="onImageError($event)"
                          class="article-image w-full h-full object-cover transition-transform duration-500 hover:scale-105" />
                      </div>
                    </div>
                  }
                </div>

                <!-- Footer: Sources & Essence Tag -->
                <div class="flex items-center justify-between pt-5 border-t border-aux1/30">
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
            } @empty {
              <div class="text-center py-16 bg-white/50 rounded-3xl border border-aux1/50">
                <p class="text-sm italic opacity-60 mb-3">Brak wiadomości spełniających wybrane kryteria.</p>
                @if (store.selectedCategory()) {
                  <button 
                    (click)="store.setCategoryFilter(null)"
                    class="text-xs px-4 py-2 bg-primary/10 text-primary border border-primary/20 rounded-full hover:bg-primary/20 transition-all font-semibold">
                    Wyczyść filtr kategorii
                  </button>
                }
              </div>
            }
          </div>
        }

        <!-- Newsletter Subscription Box -->
        <div class="mt-12">
          <app-newsletter-box></app-newsletter-box>
        </div>
      </main>

      <!-- Footer -->
      <footer class="bg-white border-t border-aux1/50 py-8 px-4 mt-auto">
        <div class="max-w-4xl mx-auto text-center space-y-3">
          <div class="flex justify-center mb-3">
            <app-buy-me-coffee-button></app-buy-me-coffee-button>
          </div>
          <p class="text-xs font-bold text-aux3">
            Info Skrót - Twój codzienny skrót informacji i wiadomości w pigułce.
          </p>
          <p class="text-[11px] text-aux2 opacity-70 leading-relaxed max-w-xl mx-auto">
            Bądź na bieżąco każdego dnia bez szumu informacyjnego. Nasz portal agreguje najważniejsze wydarzenia z kraju i ze świata, serwując je w postaci zwięzłej info pigułki. Szybka prasówka i esencja wiadomości dostosowana do Twojego tempa życia.
          </p>
          <p class="text-[10px] text-aux2 opacity-40">
            &copy; 2026 InfoSkrót. Wszelkie prawa zastrzeżone.
          </p>
        </div>
      </footer>
    </div>
  `,
  styles: [
    `
    :host {
      display: block;
      background-color: #f4f7f6;
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

  onImageError(event: Event) {
    const imgEl = event.target as HTMLImageElement;
    if (imgEl && imgEl.parentElement) {
      // Hide container smoothly if external image fails to load
      imgEl.parentElement.style.display = 'none';
    }
  }
}
