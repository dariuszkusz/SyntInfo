import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { NgIconComponent, provideIcons } from '@ng-icons/core';
import { lucideMail, lucideCheckCircle, lucideAlertCircle, lucideBell, lucideSend } from '@ng-icons/lucide';
import { NewsletterStore } from '../../../state/newsletter.store';

@Component({
  selector: 'app-newsletter-box',
  standalone: true,
  imports: [CommonModule, FormsModule, NgIconComponent],
  providers: [
    provideIcons({
      lucideMail,
      lucideCheckCircle,
      lucideAlertCircle,
      lucideBell,
      lucideSend
    })
  ],
  template: `
    <section
      data-cy="newsletter-section"
      class="w-full bg-white/90 backdrop-blur-sm border border-primary/30 rounded-3xl p-6 sm:p-8 shadow-sm transition-all duration-300 hover:shadow-md"
    >
      <div class="max-w-2xl mx-auto text-center space-y-4">
        <!-- Badge & Title -->
        <div class="inline-flex items-center gap-1.5 px-3 py-1 rounded-full bg-primary/15 text-aux2 text-xs font-semibold tracking-wide">
          <ng-icon name="lucideBell" class="text-sm"></ng-icon>
          <span>{{ store.mode() === 'subscribe' ? 'Codzienny skrót' : 'Zarządzanie subskrypcją' }}</span>
        </div>

        <h3 class="text-xl sm:text-2xl font-bold text-aux3 tracking-tight">
          {{ store.mode() === 'subscribe' ? 'Bądź na bieżąco – Newsletter InfoSkrót' : 'Wypisz się z newslettera' }}
        </h3>

        <p class="text-xs sm:text-sm text-aux2 opacity-80 leading-relaxed max-w-lg mx-auto">
          {{
            store.mode() === 'subscribe'
              ? 'Najważniejsze podsumowania wiadomości prosto na Twoją skrzynkę. Bez spamu, tylko esencja informacji.'
              : 'Szkoda, że nas opuszczasz. Podaj swój adres e-mail, aby natychmiast wyrejestrować się z bazy.'
          }}
        </p>

        <!-- Form -->
        <form (ngSubmit)="onSubmit()" class="mt-4 flex flex-col sm:flex-row gap-3 max-w-md mx-auto">
          <div class="relative flex-grow">
            <span class="absolute inset-y-0 left-0 pl-3.5 flex items-center pointer-events-none text-aux2 opacity-50">
              <ng-icon name="lucideMail" class="text-base"></ng-icon>
            </span>
            <input
              type="email"
              name="newsletterEmail"
              [(ngModel)]="email"
              [disabled]="store.isLoading()"
              data-cy="newsletter-email-input"
              required
              placeholder="twoj@email.pl"
              class="w-full pl-10 pr-4 py-2.5 text-sm rounded-2xl bg-aux1/40 border border-aux2/20 text-aux3 placeholder:text-aux2/40 focus:outline-none focus:ring-2 focus:ring-primary focus:border-transparent transition-all disabled:opacity-50"
            />
          </div>

          <button
            type="submit"
            [disabled]="store.isLoading() || !email.trim()"
            data-cy="newsletter-submit-btn"
            class="inline-flex items-center justify-center gap-2 px-5 py-2.5 text-sm font-semibold text-aux3 bg-primary hover:bg-primary/80 active:scale-98 rounded-2xl transition-all shadow-sm disabled:opacity-50 disabled:cursor-not-allowed cursor-pointer"
          >
            @if (store.isLoading()) {
              <span class="animate-spin inline-block w-4 h-4 border-2 border-aux3 border-t-transparent rounded-full"></span>
              <span>Przetwarzanie...</span>
            } @else {
              <ng-icon name="lucideSend" class="text-sm"></ng-icon>
              <span>{{ store.mode() === 'subscribe' ? 'Zapisz się' : 'Wypisz się' }}</span>
            }
          </button>
        </form>

        <!-- Status Message -->
        @if (store.status() === 'success') {
          <div
            data-cy="newsletter-success-msg"
            class="flex items-center justify-center gap-2 p-3 mt-3 bg-emerald-50 border border-emerald-200 text-emerald-800 text-xs sm:text-sm rounded-2xl animate-fade-in"
          >
            <ng-icon name="lucideCheckCircle" class="text-base text-emerald-600 shrink-0"></ng-icon>
            <span>{{ store.message() }}</span>
          </div>
        }

        @if (store.status() === 'error') {
          <div
            data-cy="newsletter-error-msg"
            class="flex items-center justify-center gap-2 p-3 mt-3 bg-rose-50 border border-rose-200 text-rose-800 text-xs sm:text-sm rounded-2xl animate-fade-in"
          >
            <ng-icon name="lucideAlertCircle" class="text-base text-rose-600 shrink-0"></ng-icon>
            <span>{{ store.message() }}</span>
          </div>
        }

        <!-- Mode Toggle Link -->
        <div class="pt-2">
          @if (store.mode() === 'subscribe') {
            <button
              type="button"
              (click)="toggleMode('unsubscribe')"
              data-cy="newsletter-mode-toggle"
              class="text-[11px] text-aux2 opacity-60 hover:opacity-100 hover:underline transition-opacity cursor-pointer"
            >
              Chcesz zrezygnować z subskrypcji? Kliknij tutaj, aby się wypisać.
            </button>
          } @else {
            <button
              type="button"
              (click)="toggleMode('subscribe')"
              data-cy="newsletter-mode-toggle"
              class="text-[11px] text-aux2 opacity-60 hover:opacity-100 hover:underline transition-opacity cursor-pointer"
            >
              Wróć do formularza zapisu.
            </button>
          }
        </div>
      </div>
    </section>
  `
})
export class NewsletterBoxComponent {
  readonly store = inject(NewsletterStore);
  email: string = '';

  onSubmit() {
    const trimmed = this.email.trim();
    if (!trimmed) return;

    if (this.store.mode() === 'subscribe') {
      this.store.subscribeEmail({ email: trimmed });
    } else {
      this.store.unsubscribeEmail({ email: trimmed });
    }
  }

  toggleMode(mode: 'subscribe' | 'unsubscribe') {
    this.store.setMode(mode);
  }
}
