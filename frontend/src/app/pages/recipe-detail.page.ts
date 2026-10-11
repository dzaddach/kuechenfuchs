import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService } from '../auth/auth.service';
import { IconComponent } from '../shared/icon.component';
import { RecipeApiService, RecipeRecord, RecipeUnit } from '../recipes/recipe-api.service';

@Component({
  selector: 'app-recipe-detail-page',
  imports: [RouterLink, IconComponent],
  template: `
    <a class="back-link" routerLink="/recipes"><app-icon name="back" /> Zurück zu meinen Rezepten</a>
    @if (loading()) { <p class="loading" role="status">Rezept wird geladen …</p> }
    @if (error()) {
      <section class="empty-state" role="alert"><h1>Rezept nicht verfügbar</h1><p>{{ error() }}</p>
        <a class="secondary-button" routerLink="/recipes">Zur Rezeptliste</a></section>
    }
    @if (recipe(); as item) {
      <article class="detail">
        <header class="hero">
          <div class="hero-art" aria-hidden="true"><span>{{ item.category }}</span></div>
          <div class="hero-copy">
            <p class="eyebrow">{{ item.category }}<span> · </span>{{ item.preparationMinutes }} MIN.</p>
            <h1>{{ item.name }}</h1>
            @if (item.description) { <p class="description">{{ item.description }}</p> }
            @if (item.tags.length) {
              <ul class="tags" aria-label="Rezept-Tags">
                @for (tag of item.tags; track tag) { <li>{{ tag }}</li> }
              </ul>
            }
            <a class="edit-button" [routerLink]="['/recipes', item.id, 'edit']"><app-icon name="edit" /> Rezept bearbeiten</a>
          </div>
        </header>

        <section class="content-card ingredients">
          <div class="section-heading"><div><p class="eyebrow">GUT VORBEREITET</p><h2>Das brauchst du</h2></div>
            <label class="servings"><span><app-icon name="users" /> Portionen</span>
              <input type="number" min="1" max="50" [value]="targetServings()" (input)="updateServings($event)" aria-label="Portionenzahl für Anzeige"></label>
          </div>
          <p class="scale-note">Grundrezept für {{ item.baseServings }} Portionen. Angezeigte Mengen werden nur für diese Ansicht skaliert.</p>
          <ul class="ingredient-list">
            @for (ingredient of item.ingredients; track ingredient.id ?? $index; let index = $index) {
              <li><span class="ingredient-name">{{ ingredient.name }} @if (ingredient.foodName && ingredient.foodName !== ingredient.name) { <small>({{ ingredient.foodName }})</small> }</span>
                <span class="amount">{{ amountFor(ingredient.amount, item.baseServings, index) }}</span>
              </li>
            }
          </ul>
        </section>

        <section class="content-card preparation">
          <div class="section-heading"><div><p class="eyebrow">SCHRITT FÜR SCHRITT</p><h2>So wird's gemacht</h2></div>
            <span class="time"><app-icon name="clock" /> {{ item.preparationMinutes }} Min.</span></div>
          <ol class="steps">
            @for (step of item.steps; track step.id ?? $index; let index = $index) {
              <li><span class="step-number">{{ index + 1 }}</span><p>{{ step.description }}</p></li>
            }
          </ol>
        </section>
      </article>
    }
  `,
  styleUrl: './recipe-detail.page.css',
})
export class RecipeDetailPage {
  private readonly auth = inject(AuthService);
  private readonly api = inject(RecipeApiService);
  private readonly recipeId = inject(ActivatedRoute).snapshot.paramMap.get('id');
  readonly recipe = signal<RecipeRecord | null>(null);
  readonly loading = signal(true);
  readonly error = signal('');
  readonly targetServings = signal(1);
  readonly units = signal<RecipeUnit[]>([]);

  constructor() {
    void this.load();
  }

  updateServings(event: Event): void {
    if (event.target instanceof HTMLInputElement) {
      const value = Number(event.target.value);
      if (Number.isInteger(value) && value >= 1 && value <= 50) this.targetServings.set(value);
    }
  }

  amountFor(amount: number | null, baseServings: number, index: number): string {
    if (amount === null) return 'nach Geschmack';
    const scaled = amount * this.targetServings() / baseServings;
    const unit = this.recipe()?.ingredients[index]?.unitCode ?? '';
    const unitName = this.units().find((item) => item.code === unit)?.name ?? unit;
    return `${new Intl.NumberFormat('de-DE', { maximumFractionDigits: 3 }).format(scaled)} ${unitName}`;
  }

  private async load(): Promise<void> {
    const householdId = this.auth.session()?.households[0]?.householdId;
    if (!householdId || !this.recipeId) {
      this.error.set('Das Rezept konnte nicht gefunden werden.');
      this.loading.set(false);
      return;
    }
    try {
      const [recipe, units] = await Promise.all([
        this.api.get(householdId, this.recipeId),
        this.api.units(householdId),
      ]);
      this.recipe.set(recipe);
      this.units.set(units);
      this.targetServings.set(recipe.baseServings);
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'Das Rezept konnte nicht geladen werden.');
    } finally {
      this.loading.set(false);
    }
  }
}
