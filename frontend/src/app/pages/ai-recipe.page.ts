import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../auth/auth.service';
import { IconComponent } from '../shared/icon.component';
import { RecipeApiService, RecipeWrite } from '../recipes/recipe-api.service';

@Component({
  selector: 'app-ai-recipe-page',
  imports: [FormsModule, RouterLink, IconComponent],
  template: `
    <a class="back-link" routerLink="/recipes"><app-icon name="back" /> Zurück zu meinen Rezepten</a>
    <header class="assistant-heading">
      <span class="assistant-mark"><app-icon name="sparkles" /></span>
      <p class="eyebrow">DEINE KÜCHE, NEUE IDEEN</p>
      <h1>Was möchtest du kochen?</h1>
      <p>Beschreibe deine Idee oder verrate, was noch im Kühlschrank liegt. Du prüfst und bearbeitest den Entwurf, bevor etwas gespeichert wird.</p>
    </header>

    <form class="assistant-form" (ngSubmit)="generate()">
      <section class="assistant-card">
        <label for="recipe-wish">Dein Rezeptwunsch <span>*</span></label>
        <textarea id="recipe-wish" name="prompt" [(ngModel)]="prompt" rows="4" maxlength="2000"
                  placeholder="Ich habe Kartoffeln, Paprika und Hähnchen und möchte für drei Personen ein schnelles Abendessen kochen."
                  required></textarea>
        <div class="field-row">
          <div class="field">
            <label for="available-ingredients">Vorhandene Zutaten <span class="optional">optional</span></label>
            <textarea id="available-ingredients" name="availableIngredients" [(ngModel)]="ingredientsText" rows="3"
                      placeholder="Kartoffeln, Paprika, Hähnchen"></textarea>
            <small>Trenne Zutaten mit Kommas oder Zeilenumbrüchen.</small>
          </div>
          <div class="field">
            <label for="desired-servings">Portionen <span class="optional">optional</span></label>
            <input id="desired-servings" name="servings" type="number" min="1" max="50"
                   [(ngModel)]="servings" placeholder="z. B. 3">
            <small>Wenn leer, wählt die KI eine passende Anzahl.</small>
          </div>
        </div>
      </section>

      <section class="assistant-card">
        <h2>Was ist dir wichtig?</h2>
        <div class="preference-options">
          <label><input type="checkbox" name="vegetarian" [(ngModel)]="vegetarian"> Vegetarisch</label>
          <label><input type="checkbox" name="quick" [(ngModel)]="quick"> Schnell</label>
          <label><input type="checkbox" name="budgetFriendly" [(ngModel)]="budgetFriendly"> Preisbewusst</label>
        </div>
        <div class="field">
          <label for="other-preferences">Weitere Wünsche <span class="optional">optional</span></label>
          <input id="other-preferences" name="preferences" [(ngModel)]="preferencesText" maxlength="960"
                 placeholder="z. B. wenig Abwasch, aus dem Ofen">
        </div>
        <div class="field">
          <label for="avoid-ingredients">Allergien oder Zutaten, die du vermeiden möchtest <span class="optional">optional</span></label>
          <input id="avoid-ingredients" name="avoidIngredients" [(ngModel)]="avoidText" maxlength="2400"
                 placeholder="z. B. Erdnüsse, Sellerie">
          <small>Die Hinweise sind keine Garantie für Allergenfreiheit. Prüfe Zutaten und Verpackungen immer selbst.</small>
        </div>
      </section>

      <section class="privacy-card" aria-labelledby="privacy-heading">
        <h2 id="privacy-heading"><app-icon name="lock" /> Externe KI-Verarbeitung</h2>
        <p>Bei deiner Anfrage werden nur dein Rezeptwunsch, die angegebenen Zutaten, Portionen und Wünsche an OpenAI übertragen. Küchenfuchs sendet keine Konto-, Haushalts- oder Rezeptverlaufsdaten. OpenAI kann API-Eingaben gemäß den jeweils geltenden Datenverarbeitungs- und Aufbewahrungsregeln verarbeiten. Bitte gib keine privaten oder sensiblen Informationen ein.</p>
        <label class="consent">
          <input type="checkbox" name="externalProcessingConfirmed" [(ngModel)]="externalProcessingConfirmed">
          <span>Ich habe die Information gelesen und möchte diese Angaben zur Erstellung eines Rezeptentwurfs an OpenAI senden.</span>
        </label>
      </section>

      @if (error()) { <p class="error-message" role="alert">{{ error() }}</p> }
      <button class="generate-button" type="submit" [disabled]="loading() || !externalProcessingConfirmed || prompt.trim().length < 10">
        <app-icon name="sparkles" />
        {{ loading() ? 'Deine Idee wird zubereitet …' : 'Rezeptentwurf erstellen' }}
      </button>
      @if (loading()) { <p class="loading-message" role="status">Die KI erstellt einen Entwurf. Das kann einen Moment dauern.</p> }
      <p class="no-save-note">Es wird noch nichts gespeichert. Du kannst den Entwurf anschließend vollständig bearbeiten und ausdrücklich übernehmen.</p>
    </form>
  `,
  styleUrl: './ai-recipe.page.css',
})
export class AiRecipePage {
  private readonly auth = inject(AuthService);
  private readonly api = inject(RecipeApiService);
  private readonly router = inject(Router);

  prompt = '';
  ingredientsText = '';
  servings: number | null = null;
  preferencesText = '';
  avoidText = '';
  vegetarian = false;
  quick = false;
  budgetFriendly = false;
  externalProcessingConfirmed = false;
  readonly loading = signal(false);
  readonly error = signal('');

  async generate(): Promise<void> {
    const householdId = this.auth.session()?.households[0]?.householdId;
    if (!householdId || !this.externalProcessingConfirmed || this.prompt.trim().length < 10) return;
    if (this.servings !== null && (!Number.isInteger(Number(this.servings)) || Number(this.servings) < 1 || Number(this.servings) > 50)) {
      this.error.set('Die Portionszahl muss zwischen 1 und 50 liegen.');
      return;
    }

    this.loading.set(true);
    this.error.set('');
    try {
      const preferences = [
        ...(this.vegetarian ? ['vegetarisch'] : []),
        ...(this.quick ? ['schnell'] : []),
        ...(this.budgetFriendly ? ['preisbewusst'] : []),
        ...splitValues(this.preferencesText),
      ];
      const result = await this.api.createAiDraft(householdId, {
        prompt: this.prompt.trim(),
        desiredServings: this.servings === null ? null : Number(this.servings),
        availableIngredients: splitValues(this.ingredientsText),
        preferences,
        avoidIngredients: splitValues(this.avoidText),
        externalProcessingConfirmed: true,
      });
      const navigated = await this.router.navigate(['/recipes/new'], {
        state: { aiRecipeDraft: result.recipe, aiWarnings: result.warnings },
      });
      if (!navigated) this.error.set('Der geprüfte Entwurf konnte nicht geöffnet werden. Bitte versuche es erneut.');
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'Der Rezeptentwurf konnte nicht erstellt werden.');
    } finally {
      this.loading.set(false);
    }
  }
}

function splitValues(value: string): string[] {
  return value.split(/[,\n]/).map((item) => item.trim()).filter(Boolean);
}

export type AiRecipeDraftForEditor = RecipeWrite;
