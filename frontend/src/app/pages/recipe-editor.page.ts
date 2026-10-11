import { Component, inject, signal } from '@angular/core';
import { AbstractControl, FormArray, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../auth/auth.service';
import { IconComponent } from '../shared/icon.component';
import { RecipeApiService, RecipeRecord, RecipeUnit, RecipeWrite } from '../recipes/recipe-api.service';

@Component({
  selector: 'app-recipe-editor-page',
  imports: [ReactiveFormsModule, RouterLink, IconComponent],
  template: `
    <a class="back-link" routerLink="/recipes"><app-icon name="back" /> Zurück zu meinen Rezepten</a>
    <header class="editor-heading">
      <div><p class="eyebrow">{{ fromAi() ? 'KI-ENTWURF' : 'DEINE SAMMLUNG' }}</p>
        <h1>{{ recipeId ? 'Rezept bearbeiten' : fromAi() ? 'Entwurf prüfen und anpassen' : 'Ein Rezept für dich' }}</h1>
        <p class="intro">{{ fromAi() ? 'Prüfe alle Angaben und bearbeite den Entwurf nach deinem Geschmack.' : 'Halte fest, was auf deinen Tisch kommt.' }}</p></div>
    </header>
    @if (loading()) { <p role="status">Rezept wird geladen …</p> }
    @if (error()) { <p class="validation-summary" role="alert">{{ error() }}</p> }
    @if (!loading()) {
      @if (fromAi() && aiWarnings().length) {
        <section class="validation-summary" aria-label="Hinweise zum KI-Entwurf">
          <strong>Bitte beachte diese Hinweise:</strong>
          <ul>@for (warning of aiWarnings(); track $index) { <li>{{ warning }}</li> }</ul>
          <p>KI-Vorschläge können Fehler enthalten. Prüfe Zutaten, Mengen und mögliche Allergene.</p>
        </section>
      }
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <section class="form-card">
          <div class="form-section-heading"><span class="step-number">1</span><div><h2>Das Grundrezept</h2><p>Ein guter Name macht Appetit.</p></div></div>
          <div class="field"><label for="recipe-name">Rezeptname <span>*</span></label>
            <input id="recipe-name" formControlName="name" maxlength="90" required [attr.aria-invalid]="showError('name')">
            @if (showError('name')) { <small class="field-error">Bitte gib deinem Rezept einen Namen.</small> }</div>
          <div class="field"><label for="description">Ein paar Worte dazu</label><textarea id="description" formControlName="description" rows="3" maxlength="500"></textarea></div>
          <div class="field-row">
            <div class="field"><label for="servings">Portionen <span>*</span></label>
              <div class="number-field"><input id="servings" type="number" min="1" max="50" formControlName="servings" required><span>Portionen</span></div>
              @if (showError('servings')) { <small class="field-error">Bitte wähle 1 bis 50 Portionen.</small> }</div>
            <div class="field"><label for="minutes">Zubereitungszeit <span>*</span></label>
              <div class="number-field"><input id="minutes" type="number" min="1" max="600" formControlName="minutes" required><span>Minuten</span></div>
              @if (showError('minutes')) { <small class="field-error">Die Zeit muss zwischen 1 und 600 Minuten liegen.</small> }</div>
          </div>
        </section>
        <section class="form-card">
          <div class="form-section-heading"><span class="step-number">2</span><div><h2>Was kommt hinein?</h2><p>Die Menge darf leer bleiben, wenn du sie nicht kennst.</p></div>
            <span class="item-count">{{ ingredients.length }} Zutaten</span></div>
          <div class="field target-servings"><label for="target-servings">Mengen anzeigen für</label>
            <div class="number-field"><input id="target-servings" type="number" min="1" max="50" [value]="targetServings()" (input)="updateTargetServings($event)"><span>Portionen</span></div>
          </div>
          <div class="ingredient-labels"><span>MENGE</span><span>EINHEIT</span><span>ZUTAT</span><span></span></div>
          <div class="ingredient-list" formArrayName="ingredients">
            @for (ingredient of ingredients.controls; track $index; let index = $index) {
              <div class="ingredient-row" [formGroupName]="index">
                <input type="text" inputmode="decimal" formControlName="amount" [attr.aria-label]="'Menge Zutat ' + (index + 1)" placeholder="optional"
                       [attr.aria-invalid]="submitted() && ingredient.controls.amount.invalid">
                <select formControlName="unit" [attr.aria-label]="'Einheit Zutat ' + (index + 1)">
                  @for (unit of units(); track unit.code) { <option [value]="unit.code">{{ unit.name }}</option> }
                </select>
                <input type="text" formControlName="name" [attr.aria-label]="'Zutat ' + (index + 1)" placeholder="z. B. Mehl" required>
                <button class="remove-button" type="button" [disabled]="ingredients.length <= 1" [attr.aria-label]="'Zutat ' + (index + 1) + ' entfernen'" (click)="removeIngredient(index)"><app-icon name="close" /></button>
                @if (ingredient.controls.amount.value && targetServings() !== baseServings()) {
                  <small class="row-error">Für {{ targetServings() }} Portionen: {{ scaledAmount(index) }} {{ ingredient.controls.unit.value }}</small>
                }
                @if (submitted() && ingredient.invalid) { <small class="row-error">Bitte prüfe Zutat und Menge.</small> }
              </div>
            }
          </div>
          <button class="add-row" type="button" (click)="addIngredient()"><app-icon name="plus" /> Zutat hinzufügen</button>
        </section>
        <section class="form-card">
          <div class="form-section-heading"><span class="step-number">3</span><div><h2>So wird's gemacht</h2><p>Schritt für Schritt zum Genuss.</p></div>
            <span class="item-count">{{ steps.length }} Schritte</span></div>
          <div class="steps-list" formArrayName="steps">
            @for (step of steps.controls; track $index; let index = $index) {
              <div class="step-row"><span class="step-index">{{ index + 1 }}</span>
                <textarea [formControlName]="index" [attr.aria-label]="'Zubereitungsschritt ' + (index + 1)" rows="2" required [attr.aria-invalid]="submitted() && step.invalid"></textarea>
                <button class="remove-button" type="button" [disabled]="steps.length <= 1" (click)="removeStep(index)" [attr.aria-label]="'Schritt ' + (index + 1) + ' entfernen'"><app-icon name="close" /></button>
                @if (submitted() && step.invalid) { <small class="row-error">Bitte beschreibe diesen Schritt.</small> }
              </div>
            }
          </div>
          <button class="add-row" type="button" (click)="addStep()"><app-icon name="plus" /> Schritt hinzufügen</button>
        </section>
        <section class="form-card">
          <div class="form-section-heading"><span class="step-number">4</span><div><h2>Das passt dazu</h2><p>Ordne dein Rezept ein und finde es später wieder.</p></div></div>
          <div class="field-row"><div class="field"><label for="category">Kategorie</label>
            <select id="category" formControlName="category"><option>Abendessen</option><option>Frühstück</option><option>Mittagessen</option><option>Dessert</option><option>Snack</option><option>Vegetarisch</option><option>Vegan</option><option>Fisch</option></select></div>
            <div class="field"><label for="tags">Tags <span class="optional">optional</span></label><input id="tags" formControlName="tags" placeholder="z. B. vegetarisch, schnell"><small>Trenne mehrere Tags mit einem Komma.</small></div>
          </div>
        </section>
        @if (notice()) { <p class="demo-result" role="status">{{ notice() }}</p> }
        @if (form.invalid && submitted()) { <p class="validation-summary" role="alert">Ein paar Pflichtfelder brauchen noch deine Aufmerksamkeit.</p> }
        <div class="form-actions"><a class="cancel-button" routerLink="/recipes">Abbrechen</a>
          <button class="save-button" type="submit" [disabled]="saving()"><app-icon name="check" />
            {{ saving() ? 'Speichern …' : fromAi() ? 'Rezept übernehmen' : 'Rezept speichern' }}</button></div>
      </form>
      @if (recipeId && recipe()) {
        <section class="form-actions archive-action"><button class="cancel-button" type="button" (click)="archive()">Rezept archivieren</button></section>
      }
    }
    <p class="required-note"><span>*</span> Pflichtfeld · Änderungen werden in deinem Haushalt gespeichert.</p>
  `,
  styleUrl: './recipe-editor.page.css',
})
export class RecipeEditorPage {
  private readonly fb = inject(FormBuilder).nonNullable;
  private readonly auth = inject(AuthService);
  private readonly api = inject(RecipeApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly navigationState = this.router.getCurrentNavigation()?.extras.state ?? history.state;
  readonly recipeId = this.route.snapshot.paramMap.get('id');
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly submitted = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly fromAi = signal(false);
  readonly aiWarnings = signal<string[]>([]);
  readonly units = signal<RecipeUnit[]>([]);
  readonly recipe = signal<RecipeRecord | null>(null);
  readonly targetServings = signal(2);
  readonly form = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(90)]],
    description: ['', Validators.maxLength(500)],
    servings: [2, [Validators.required, Validators.min(1), Validators.max(50)]],
    minutes: [30, [Validators.required, Validators.min(1), Validators.max(600)]],
    ingredients: this.fb.array([this.newIngredient()]),
    steps: this.fb.array([this.fb.control('', Validators.required)]),
    category: ['Abendessen', Validators.required],
    tags: [''],
  });
  constructor() { void this.load(); }

  get ingredients(): FormArray<ReturnType<RecipeEditorPage['newIngredient']>> { return this.form.controls.ingredients; }
  get steps(): FormArray<ReturnType<RecipeEditorPage['newStep']>> { return this.form.controls.steps; }

  addIngredient(): void { this.ingredients.push(this.newIngredient()); }
  removeIngredient(index: number): void { if (this.ingredients.length > 1) this.ingredients.removeAt(index); }
  addStep(): void { this.steps.push(this.newStep()); }
  removeStep(index: number): void { if (this.steps.length > 1) this.steps.removeAt(index); }
  updateTargetServings(event: Event): void {
    if (event.target instanceof HTMLInputElement) this.targetServings.set(Number(event.target.value));
  }
  baseServings(): number { return Number(this.form.controls.servings.value); }
  scaledAmount(index: number): string {
    const amount = Number(this.ingredients.at(index).controls.amount.value.trim().replace(',', '.'));
    const scaled = amount * this.targetServings() / this.baseServings();
    return new Intl.NumberFormat('de-DE', { maximumFractionDigits: 3 }).format(scaled);
  }

  showError(controlName: 'name' | 'servings' | 'minutes'): boolean {
    const control = this.form.controls[controlName];
    return control.invalid && (control.touched || this.submitted());
  }

  async submit(): Promise<void> {
    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const householdId = this.householdId();
    if (!householdId) return;
    const value = this.form.getRawValue();
    const payload: RecipeWrite = {
      name: value.name.trim(),
      description: value.description.trim(),
      baseServings: Number(value.servings),
      preparationMinutes: Number(value.minutes),
      category: value.category,
      tags: value.tags.split(',').map((tag) => tag.trim()).filter(Boolean),
      ingredients: value.ingredients.map((ingredient) => ({
        name: ingredient.name.trim(),
        amount: ingredient.amount.trim() ? Number(ingredient.amount.trim().replace(',', '.')) : null,
        unitCode: ingredient.unit,
        foodId: ingredient.foodId || null,
      })),
      steps: value.steps.map((description) => ({ description: description.trim() })),
    };
    this.saving.set(true);
    this.error.set('');
    try {
      const saved = await this.api.save(householdId, payload, this.recipeId ?? undefined, this.recipe()?.version);
      if (this.recipeId) {
        this.recipe.set(saved);
        this.notice.set('Rezept gespeichert.');
      } else if (this.fromAi()) {
        await this.router.navigate(['/recipes', saved.id]);
      } else {
        await this.router.navigate(['/recipes']);
      }
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'Das Rezept konnte nicht gespeichert werden.');
    } finally {
      this.saving.set(false);
    }
  }

  async archive(): Promise<void> {
    const householdId = this.householdId();
    const recipe = this.recipe();
    if (!householdId || !recipe) return;
    if (!globalThis.confirm('Möchtest du dieses Rezept archivieren?')) return;
    try {
      await this.api.archive(householdId, recipe);
      await this.router.navigate(['/recipes']);
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'Das Rezept konnte nicht archiviert werden.');
    }
  }

  private async load(): Promise<void> {
    const householdId = this.householdId();
    if (!householdId) {
      this.error.set('Deinem Benutzer ist kein Haushalt zugeordnet.');
      this.loading.set(false);
      return;
    }
    try {
      this.units.set(await this.api.units(householdId));
      if (!this.units().length) throw new Error('Es sind keine Einheiten verfügbar.');
      if (this.recipeId) {
        const recipe = await this.api.get(householdId, this.recipeId);
        this.recipe.set(recipe);
        this.form.patchValue({
          name: recipe.name,
          description: recipe.description,
          servings: recipe.baseServings,
          minutes: recipe.preparationMinutes,
          category: recipe.category,
          tags: recipe.tags.join(', '),
        });
        this.targetServings.set(recipe.baseServings);
        this.form.setControl('ingredients', this.fb.array(recipe.ingredients.map((ingredient) =>
          this.newIngredient(ingredient.name, ingredient.amount?.toString() ?? '', ingredient.unitCode, ingredient.foodId ?? ''))));
        this.form.setControl('steps', this.fb.array(recipe.steps.map((step) => this.newStep(step.description))));
      } else {
        const state = isRecord(this.navigationState) ? this.navigationState : {};
        const draft = state['aiRecipeDraft'];
        if (isRecipeWrite(draft)) {
          this.fromAi.set(true);
          this.aiWarnings.set(Array.isArray(state['aiWarnings'])
            ? state['aiWarnings'].filter((warning): warning is string => typeof warning === 'string')
            : []);
          this.form.patchValue({
            name: draft.name,
            description: draft.description,
            servings: draft.baseServings,
            minutes: draft.preparationMinutes,
            category: draft.category,
            tags: draft.tags.join(', '),
          });
          this.targetServings.set(draft.baseServings);
          this.form.setControl('ingredients', this.fb.array(draft.ingredients.map((ingredient) =>
            this.newIngredient(ingredient.name, ingredient.amount?.toString() ?? '', ingredient.unitCode, ingredient.foodId ?? ''))));
          this.form.setControl('steps', this.fb.array(draft.steps.map((step) => this.newStep(step.description))));
        }
      }
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'Das Rezept konnte nicht geladen werden.');
    } finally {
      this.loading.set(false);
    }
  }

  private householdId(): string | undefined {
    return this.auth.session()?.households[0]?.householdId;
  }

  private newIngredient(name = '', amount = '', unit = 'g', foodId = '') {
    const initialUnit = this.units().some((item) => item.code === unit) ? unit : this.units()[0]?.code ?? unit;
    return this.fb.group({
      name: [name, [Validators.required, Validators.maxLength(120)]],
      amount: [amount, [positiveOptionalAmount]],
      unit: [initialUnit, Validators.required],
      foodId: [foodId],
    });
  }

  private newStep(description = '') {
    return this.fb.control(description, [Validators.required, Validators.maxLength(2_000)]);
  }
}

function positiveOptionalAmount(control: AbstractControl<string>): ValidationErrors | null {
  const raw = control.value.trim().replace(',', '.');
  if (!raw) return null;
  const amount = Number(raw);
  return Number.isFinite(amount) && amount > 0 && amount <= 1_000_000 && /^\d+(?:\.\d{1,3})?$/.test(raw)
    ? null
    : { amount: true };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isRecipeWrite(value: unknown): value is RecipeWrite {
  if (!isRecord(value)) return false;
  return typeof value['name'] === 'string'
    && typeof value['description'] === 'string'
    && Number.isInteger(value['baseServings'])
    && Number.isInteger(value['preparationMinutes'])
    && typeof value['category'] === 'string'
    && Array.isArray(value['tags']) && value['tags'].every((tag) => typeof tag === 'string')
    && Array.isArray(value['ingredients']) && value['ingredients'].every((ingredient) =>
      isRecord(ingredient)
      && typeof ingredient['name'] === 'string'
      && (ingredient['amount'] === null || typeof ingredient['amount'] === 'number')
      && typeof ingredient['unitCode'] === 'string'
      && (ingredient['foodId'] === null || typeof ingredient['foodId'] === 'string'))
    && Array.isArray(value['steps']) && value['steps'].every((step) =>
      isRecord(step) && typeof step['description'] === 'string');
}
