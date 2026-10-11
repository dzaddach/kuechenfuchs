import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../auth/auth.service';
import { IconComponent } from '../shared/icon.component';
import { RecipeApiService, RecipeSummary } from '../recipes/recipe-api.service';

@Component({
  selector: 'app-recipes-page',
  imports: [RouterLink, IconComponent],
  template: `
    <section class="page-heading">
      <div><p class="eyebrow">DEINE SAMMLUNG</p><h1>Meine Rezepte<span class="count">{{ recipes().length }}</span></h1>
        <p class="intro">Alle deine Lieblingsgerichte an einem guten Ort.</p></div>
        <div class="heading-actions">
          <a class="ai-button" routerLink="/ai/recipe"><app-icon name="sparkles" /> Mit KI entwerfen</a>
          <a class="primary-button" routerLink="/recipes/new"><app-icon name="plus" /> Neues Rezept</a>
        </div>
    </section>
    <div class="toolbar">
      <label class="search-field"><app-icon name="search" />
        <input type="search" [value]="query()" (input)="updateSearch($event)"
               placeholder="Rezept oder Zutat suchen" aria-label="Rezepte suchen">
      </label>
      <button class="filter-button" type="button" (click)="showFavoritesOnly.set(!showFavoritesOnly())"
              [class.selected]="showFavoritesOnly()" [attr.aria-pressed]="showFavoritesOnly()">
        <app-icon name="heart" /> <span>Favoriten</span>
      </button>
    </div>
    <div class="category-tabs" role="group" aria-label="Nach Kategorie filtern">
      @for (category of categories; track category) {
        <button type="button" class="category-tab" [class.active]="activeCategory() === category"
                [attr.aria-pressed]="activeCategory() === category" (click)="activeCategory.set(category)">
          {{ category }}
        </button>
      }
    </div>
    @if (loading()) { <p class="result-count" role="status">Rezepte werden geladen …</p> }
    @if (error()) { <p class="validation-summary" role="alert">{{ error() }}</p> }
    @if (!loading() && !error() && filteredRecipes().length === 0) {
      <div class="empty-state"><span class="empty-icon"><app-icon name="search" /></span>
        <h2>{{ recipes().length === 0 && !query() ? 'Deine Sammlung beginnt hier' : 'Keine Rezepte gefunden' }}</h2>
        <p>{{ recipes().length === 0 && !query() ? 'Lege dein erstes persönliches Rezept an.' : 'Passe Suche oder Filter an.' }}</p>
        @if (recipes().length === 0 && !query()) {
          <a class="secondary-button" routerLink="/recipes/new">Rezept erstellen</a>
        } @else {
          <button class="secondary-button" type="button" (click)="resetFilters()">Filter zurücksetzen</button>
        }
      </div>
    }
    @if (filteredRecipes().length > 0) {
      <p class="result-count">{{ filteredRecipes().length }} REZEPTE</p>
      <div class="recipe-grid">
        @for (recipe of filteredRecipes(); track recipe.id) {
          <article class="recipe-card">
            <div class="recipe-image recipe-placeholder" aria-hidden="true"><span>{{ recipe.category }}</span></div>
            <button class="favorite-button" type="button" [disabled]="pendingFavorites().has(recipe.id)" [class.is-favorite]="favorites().has(recipe.id)"
                    [attr.aria-label]="favorites().has(recipe.id) ? recipe.name + ' aus Favoriten entfernen' : recipe.name + ' als Favorit markieren'"
                    [attr.aria-pressed]="favorites().has(recipe.id)" (click)="toggleFavorite(recipe.id)">
              <app-icon name="heart" />
            </button>
            <div class="recipe-copy">
              <div class="recipe-meta-top"><span>{{ recipe.category }}</span></div>
              <a class="recipe-title" [routerLink]="['/recipes', recipe.id]">{{ recipe.name }}</a>
              <p>{{ recipe.description || 'Ein Rezept aus deinem Haushalt.' }}</p>
              <div class="recipe-footer">
                <span><app-icon name="clock" /> {{ recipe.preparationMinutes }} Min.</span>
                <span><app-icon name="users" /> {{ recipe.baseServings }} Portionen</span>
                <a class="details-link" [routerLink]="['/recipes', recipe.id]">Ansehen</a>
              </div>
            </div>
          </article>
        }
      </div>
    }
  `,
  styleUrl: './recipes.page.css',
})
export class RecipesPage {
  private readonly auth = inject(AuthService);
  private readonly api = inject(RecipeApiService);
  private readonly destroyRef = inject(DestroyRef);
  readonly categories = ['Alle', 'Vegetarisch', 'Vegan', 'Fisch'];
  readonly query = signal('');
  readonly activeCategory = signal('Alle');
  readonly showFavoritesOnly = signal(false);
  readonly favorites = signal(new Set<string>());
  readonly pendingFavorites = signal(new Set<string>());
  readonly loading = signal(true);
  readonly error = signal('');
  readonly recipes = signal<RecipeSummary[]>([]);
  private searchTimer?: ReturnType<typeof setTimeout>;
  private latestSearchRequest = 0;

  readonly filteredRecipes = computed(() => {
    const q = this.query().trim().toLocaleLowerCase('de');
    return this.recipes().filter((recipe) => {
      const matchesSearch = !q || [recipe.name, recipe.description, recipe.category, ...recipe.tags]
        .some((value) => value.toLocaleLowerCase('de').includes(q));
      const matchesCategory = this.activeCategory() === 'Alle' || recipe.category === this.activeCategory();
      const matchesFavorite = !this.showFavoritesOnly() || this.favorites().has(recipe.id);
      return matchesSearch && matchesCategory && matchesFavorite;
    });
  });

  constructor() {
    this.destroyRef.onDestroy(() => clearTimeout(this.searchTimer));
    void this.load(++this.latestSearchRequest, true);
  }

  updateSearch(event: Event): void {
    if (!(event.target instanceof HTMLInputElement)) return;
    this.query.set(event.target.value);
    const searchRequest = ++this.latestSearchRequest;
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => void this.load(searchRequest), 250);
  }

  async toggleFavorite(id: string): Promise<void> {
    if (this.pendingFavorites().has(id)) return;
    const householdId = this.householdId();
    if (!householdId) return;
    const isFavorite = !this.favorites().has(id);
    const pending = new Set(this.pendingFavorites());
    pending.add(id);
    this.pendingFavorites.set(pending);
    this.error.set('');
    try {
      await this.api.setFavorite(householdId, id, isFavorite);
      const next = new Set(this.favorites());
      if (isFavorite) next.add(id);
      else next.delete(id);
      this.favorites.set(next);
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'Favorit konnte nicht gespeichert werden.');
    } finally {
      const remaining = new Set(this.pendingFavorites());
      remaining.delete(id);
      this.pendingFavorites.set(remaining);
    }
  }

  resetFilters(): void {
    this.query.set('');
    this.activeCategory.set('Alle');
    this.showFavoritesOnly.set(false);
    clearTimeout(this.searchTimer);
    void this.load(++this.latestSearchRequest);
  }

  private async load(searchRequest: number, includeFavorites = false): Promise<void> {
    const householdId = this.householdId();
    if (!householdId) {
      if (searchRequest === this.latestSearchRequest) {
        this.loading.set(false);
        this.error.set('Deinem Benutzer ist kein Haushalt zugeordnet.');
      }
      return;
    }
    this.loading.set(true);
    if (searchRequest === this.latestSearchRequest) this.error.set('');
    try {
      const [recipes, favorites] = await Promise.all([
        this.api.list(householdId, this.query()),
        includeFavorites ? this.api.favorites(householdId) : Promise.resolve(null),
      ]);
      if (searchRequest !== this.latestSearchRequest) return;
      this.recipes.set(recipes);
      if (favorites) this.favorites.set(new Set(favorites));
    } catch (error) {
      if (searchRequest === this.latestSearchRequest) {
        this.error.set(error instanceof Error ? error.message : 'Rezepte konnten nicht geladen werden.');
      }
    } finally {
      if (searchRequest === this.latestSearchRequest) this.loading.set(false);
    }
  }

  private householdId(): string | undefined {
    return this.auth.session()?.households[0]?.householdId;
  }
}
