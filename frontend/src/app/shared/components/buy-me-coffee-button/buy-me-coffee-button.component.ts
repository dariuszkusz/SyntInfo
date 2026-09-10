import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { NgIconComponent, provideIcons } from '@ng-icons/core';
import { lucideCoffee } from '@ng-icons/lucide';

@Component({
  selector: 'app-buy-me-coffee-button',
  standalone: true,
  imports: [CommonModule, NgIconComponent],
  providers: [provideIcons({ lucideCoffee })],
  template: `
    <a
      [href]="profileUrl"
      target="_blank"
      rel="noopener noreferrer"
      data-cy="buy-me-coffee-btn"
      class="inline-flex items-center gap-2 px-3.5 py-1.5 text-xs font-bold text-amber-950 bg-amber-400 hover:bg-amber-300 rounded-full shadow-sm hover:shadow transition-all duration-200 border border-amber-500/30 focus:outline-none focus:ring-2 focus:ring-amber-500/50"
      title="Postaw mi kawę na Buy Me a Coffee"
    >
      <ng-icon name="lucideCoffee" class="text-base text-amber-950"></ng-icon>
      <span>Postaw kawę</span>
    </a>
  `
})
export class BuyMeCoffeeButtonComponent {
  @Input() profileUrl: string = 'https://buymeacoffee.com/dariuszkusz';
}
