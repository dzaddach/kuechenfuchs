import { Component, ElementRef, HostListener, ViewChild, computed, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IconComponent } from '../shared/icon.component';
import { DEMO_RECIPES, Recipe } from '../shared/recipes';

@Component({
  selector: 'app-recipes-page',
  imports: [RouterLink, IconComponent],
  template: `
    <section class="page-heading">
      <div>
        <p class="eyebrow">DEINE SAMMLUNG</p>
        <h1>Meine Rezepte<span class="count">{{ recipes().length }}</span></h1>
        <p class="intro">Alle deine Lieblingsgerichte an einem guten Ort.</p>
      </div>
      <a class="primary-button" routerLink="/recipes/new"><app-icon name="plus" /> Neues Rezept</a>
    </section>

    <div class="demo-banner"><span class="demo-icon"><app-icon name="info" /></span>
      <span><strong>Demo-Sammlung</strong> · Die Beispiele sind frei erfunden und werden nicht gespeichert.</span>
    </div>

    <div class="toolbar">
      <label class="search-field">
        <app-icon name="search" />
        <input type="search" [value]="query()" (input)="updateSearch($event)"
               placeholder="Rezept oder Zutat suchen" aria-label="Rezepte suchen">
        @if (query()) {
          <button class="clear-search" type="button" aria-label="Suche leeren" (click)="query.set('')"><app-icon name="close" /></button>
        }
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

    <p class="result-count">{{ filteredRecipes().length }} REZEPTE <span>·</span> EIN GUTER ANFANG</p>
    @if (filteredRecipes().length) {
      <div class="recipe-grid">
        @for (recipe of filteredRecipes(); track recipe.id) {
          <article class="recipe-card">
            <button type="button" class="recipe-image" [style.background-image]="'url(' + recipe.image + ')'"
                    [attr.aria-label]="'Details zu ' + recipe.name + ' anzeigen'" (click)="openRecipe(recipe, $event)">
              <span class="demo-tag">DEMO</span>
            </button>
            <button class="favorite-button" type="button" [class.is-favorite]="favorites().has(recipe.id)"
                    [attr.aria-label]="favorites().has(recipe.id) ? recipe.name + ' aus Favoriten entfernen' : recipe.name + ' als Favorit markieren'"
                    [attr.aria-pressed]="favorites().has(recipe.id)" (click)="toggleFavorite(recipe.id)">
              <app-icon name="heart" />
            </button>
            <div class="recipe-copy">
              <div class="recipe-meta-top"><span>{{ recipe.category }}</span><span>·</span><span>{{ recipe.rating }}</span></div>
              <button type="button" class="recipe-title" (click)="openRecipe(recipe, $event)">{{ recipe.name }}</button>
              <p>{{ recipe.description }}</p>
              <div class="recipe-footer">
                <span><app-icon name="clock" /> {{ recipe.minutes }} Min.</span>
                <span><app-icon name="users" /> {{ recipe.servings }} Portionen</span>
                <button type="button" class="details-link" (click)="openRecipe(recipe, $event)">Ansehen</button>
              </div>
            </div>
          </article>
        }
      </div>
    } @else {
      <div class="empty-state">
        <span class="empty-icon"><app-icon name="search" /></span>
        <h2>Hier ist noch nichts zu sehen</h2>
        <p>Ändere deine Suche oder wähle eine andere Kategorie.</p>
        <button class="secondary-button" type="button" (click)="resetFilters()">Filter zurücksetzen</button>
      </div>
    }

    @if (selectedRecipe(); as recipe) {
      <div class="dialog-backdrop" (click)="closeDialog()">
        <section class="recipe-dialog" role="dialog" aria-modal="true" [attr.aria-label]="recipe.name"
                 #recipeDialog (click)="$event.stopPropagation()" tabindex="-1">
          <button type="button" class="dialog-close" aria-label="Schließen" (click)="closeDialog()"><app-icon name="close" /></button>
          <div class="dialog-photo" [style.background-image]="'url(' + recipe.image + ')'"></div>
          <div class="dialog-content">
            <p class="eyebrow">{{ recipe.category }} · DEMO-REZEPT</p>
            <h2>{{ recipe.name }}</h2>
            <p class="dialog-description">{{ recipe.description }}</p>
            <div class="dialog-facts"><span><app-icon name="clock" /> {{ recipe.minutes }} Min.</span><span><app-icon name="users" /> {{ recipe.servings }} Portionen</span></div>
            <h3>Das brauchst du</h3>
            <ul>@for (ingredient of recipe.ingredients; track ingredient) { <li>{{ ingredient }}</li> }</ul>
            <h3>So geht's</h3>
            <ol>@for (step of recipe.steps; track step) { <li>{{ step }}</li> }</ol>
            <p class="not-saved">Demo-Ansicht · Dieses Rezept ist nur ein Beispiel.</p>
          </div>
        </section>
      </div>
    }
  `,
  styleUrl: './recipes.page.css',
})
export class RecipesPage {
  readonly categories = ['Alle', 'Vegetarisch', 'Vegan', 'Fisch'];
  readonly query = signal('');
  readonly activeCategory = signal('Alle');
  readonly showFavoritesOnly = signal(false);
  readonly favorites = signal(new Set<string>(['lemon-pasta']));
  readonly selectedRecipe = signal<Recipe | null>(null);
  readonly recipes = signal(DEMO_RECIPES);
  private lastFocusedElement: HTMLElement | null = null;
  private dialogElement?: ElementRef<HTMLElement>;

  @ViewChild('recipeDialog')
  set recipeDialog(element: ElementRef<HTMLElement> | undefined) {
    this.dialogElement = element;
    element?.nativeElement.focus();
  }

  readonly filteredRecipes = computed(() => {
    const normalizedQuery = this.query().trim().toLocaleLowerCase('de');
    return this.recipes().filter((recipe) => {
      const matchesCategory = this.activeCategory() === 'Alle' || recipe.category === this.activeCategory();
      const matchesFavorite = !this.showFavoritesOnly() || this.favorites().has(recipe.id);
      const matchesSearch = !normalizedQuery || [recipe.name, recipe.description, ...recipe.ingredients, ...recipe.tags]
        .some((value) => value.toLocaleLowerCase('de').includes(normalizedQuery));
      return matchesCategory && matchesFavorite && matchesSearch;
    });
  });

  toggleFavorite(id: string): void {
    const next = new Set(this.favorites());
    if (next.has(id)) next.delete(id);
    else next.add(id);
    this.favorites.set(next);
  }

  updateSearch(event: Event): void {
    if (event.target instanceof HTMLInputElement) this.query.set(event.target.value);
  }

  openRecipe(recipe: Recipe, event: MouseEvent): void {
    this.lastFocusedElement = event.currentTarget instanceof HTMLElement ? event.currentTarget : null;
    this.selectedRecipe.set(recipe);
  }

  closeDialog(): void {
    this.selectedRecipe.set(null);
    queueMicrotask(() => this.lastFocusedElement?.focus());
  }

  @HostListener('document:keydown', ['$event'])
  handleDialogKeyboard(event: KeyboardEvent): void {
    const dialog = this.dialogElement?.nativeElement;
    if (!dialog) return;
    if (event.key === 'Escape') {
      event.preventDefault();
      this.closeDialog();
      return;
    }
    if (event.key !== 'Tab') return;

    const focusable = dialog.querySelectorAll<HTMLElement>(
      'button, a[href], input, select, textarea, [tabindex]:not([tabindex="-1"])',
    );
    const first = focusable.item(0);
    const last = focusable.item(focusable.length - 1);
    if (!dialog.contains(document.activeElement)) {
      event.preventDefault();
      (event.shiftKey ? last : first)?.focus();
    } else if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last?.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first?.focus();
    }
  }

  resetFilters(): void {
    this.query.set('');
    this.activeCategory.set('Alle');
    this.showFavoritesOnly.set(false);
  }
}
