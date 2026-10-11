import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './auth/auth.service';
import { IconComponent } from './shared/icon.component';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterLinkActive, RouterOutlet, IconComponent],
  template: `
    @if (auth.session()) {
      <div class="app-shell">
        <aside class="sidebar" aria-label="Hauptnavigation">
          <a class="brand" routerLink="/today" aria-label="Küchenfuchs Startseite">
            <span class="brand-mark" aria-hidden="true"><app-icon name="leaf" /></span>
            <span>Küchenfuchs</span>
          </a>
          <p class="nav-caption">DEINE KÜCHE</p>
          <nav class="nav-list">
            @for (item of navigation; track item.path) {
              <a class="nav-link" [routerLink]="item.path" routerLinkActive="active"
                 [routerLinkActiveOptions]="{ exact: item.path === '/today' }">
                <app-icon [name]="item.icon" />
                <span>{{ item.label }}</span>
              </a>
            }
          </nav>
          <div class="sidebar-note">
            <span class="note-mark"><app-icon name="sparkles" /></span>
            <strong>Eine Prise Inspiration?</strong>
            <p>Aus deinen Zutaten wird ein persönlicher Rezeptentwurf.</p>
            <a class="coming-soon" routerLink="/ai/recipe">KI-Rezeptassistent öffnen</a>
          </div>
          <div class="sidebar-footer"><span class="demo-dot"></span> DEMO-INHALTE</div>
        </aside>

        <div class="main-column">
          <header class="topbar">
            <a class="mobile-brand" routerLink="/today">
              <span class="brand-mark" aria-hidden="true"><app-icon name="leaf" /></span>
              <span>Küchenfuchs</span>
            </a>
            <div class="topbar-spacer"></div>
            <span class="demo-pill"><span class="demo-dot"></span> Demo-Inhalte</span>
            <button class="theme-toggle" type="button" (click)="toggleTheme()"
                    [attr.aria-label]="darkMode() ? 'Helles Design aktivieren' : 'Dunkles Design aktivieren'">
              <app-icon [name]="darkMode() ? 'sun' : 'moon'" />
            </button>
            <button class="account-button" type="button" (click)="logout()" aria-label="Abmelden">
              <span class="account-email">{{ auth.session()?.email }}</span>
              <app-icon name="logout" />
            </button>
          </header>
          @if (auth.error()) {
            <p class="session-error" role="alert">{{ auth.error() }}</p>
          }
          <main id="main-content" class="page-content">
            <router-outlet />
          </main>
        </div>

        <nav class="mobile-nav" aria-label="Hauptnavigation">
          @for (item of navigation; track item.path) {
            <a class="mobile-nav-link" [routerLink]="item.path" routerLinkActive="active"
               [routerLinkActiveOptions]="{ exact: item.path === '/today' }">
              <app-icon [name]="item.icon" />
              <span>{{ item.mobileLabel }}</span>
            </a>
          }
        </nav>
      </div>
    } @else {
      <router-outlet />
    }
  `,
  styleUrl: './app.css',
})
export class App {
  readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  readonly darkMode = signal(document.documentElement.dataset['theme'] === 'dark');
  readonly navigation = [
    { path: '/today', label: 'Heute & Wochenplan', mobileLabel: 'Heute', icon: 'calendar' },
    { path: '/recipes', label: 'Meine Rezepte', mobileLabel: 'Rezepte', icon: 'book' },
    { path: '/shopping', label: 'Einkaufsliste', mobileLabel: 'Einkauf', icon: 'checklist' },
    { path: '/offers', label: 'Angebote', mobileLabel: 'Angebote', icon: 'tag' },
    { path: '/settings', label: 'Einstellungen', mobileLabel: 'Mehr', icon: 'settings' },
  ] as const;

  constructor() {
    const savedTheme = localStorage.getItem('kuechenfuchs-theme');
    const useDarkTheme = savedTheme
      ? savedTheme === 'dark'
      : typeof window.matchMedia === 'function'
        && window.matchMedia('(prefers-color-scheme: dark)').matches;
    this.applyTheme(useDarkTheme);
  }

  toggleTheme(): void {
    this.applyTheme(!this.darkMode());
  }

  async logout(): Promise<void> {
    if (await this.auth.logout()) await this.router.navigateByUrl('/login');
  }

  private applyTheme(dark: boolean): void {
    this.darkMode.set(dark);
    document.documentElement.dataset['theme'] = dark ? 'dark' : 'light';
    localStorage.setItem('kuechenfuchs-theme', dark ? 'dark' : 'light');
  }
}
