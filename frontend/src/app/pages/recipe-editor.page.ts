import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { IconComponent } from '../shared/icon.component';

@Component({
  selector: 'app-recipe-editor-page',
  imports: [ReactiveFormsModule, RouterLink, IconComponent],
  template: `
    <a class="back-link" routerLink="/recipes"><app-icon name="back" /> Zurück zu meinen Rezepten</a>
    <header class="editor-heading">
      <div><p class="eyebrow">DEINE SAMMLUNG</p><h1>Ein Rezept für dich</h1><p class="intro">Halte fest, was auf deinen Tisch kommt.</p></div>
      <span class="draft-chip"><span class="demo-dot"></span> Nur Vorschau</span>
    </header>

    <div class="demo-notice" role="note">
      <span><app-icon name="info" /></span>
      <p><strong>Demo-Modus</strong> · Der Editor ist schon bereit zum Ausprobieren. Rezepte werden in dieser Version nicht gespeichert.</p>
    </div>

    <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <section class="form-card">
        <div class="form-section-heading"><span class="step-number">1</span><div><h2>Das Grundrezept</h2><p>Ein guter Name macht Appetit.</p></div></div>
        <div class="field">
          <label for="recipe-name">Rezeptname <span>*</span></label>
          <input id="recipe-name" formControlName="name" placeholder="z. B. Omas Zitronenkuchen" autocomplete="off" required [attr.aria-invalid]="showError('name')">
          @if (showError('name')) { <small class="field-error">Bitte gib deinem Rezept einen Namen.</small> }
        </div>
        <div class="field">
          <label for="description">Ein paar Worte dazu</label>
          <textarea id="description" formControlName="description" rows="3" placeholder="Was macht dieses Gericht besonders?"></textarea>
        </div>
        <div class="field-row">
          <div class="field">
            <label for="servings">Portionen <span>*</span></label>
            <div class="number-field"><input id="servings" type="number" min="1" max="20" formControlName="servings" required [attr.aria-invalid]="showError('servings')"><span>Portionen</span></div>
            @if (showError('servings')) { <small class="field-error">Bitte wähle 1 bis 20 Portionen.</small> }
          </div>
          <div class="field">
            <label for="minutes">Zubereitungszeit <span>*</span></label>
            <div class="number-field"><input id="minutes" type="number" min="1" max="600" formControlName="minutes" required [attr.aria-invalid]="showError('minutes')"><span>Minuten</span></div>
            @if (showError('minutes')) { <small class="field-error">Die Zeit muss zwischen 1 und 600 Minuten liegen.</small> }
          </div>
        </div>
      </section>

      <section class="form-card">
        <div class="form-section-heading">
          <span class="step-number">2</span>
          <div><h2>Was kommt hinein?</h2><p>Menge, Einheit und Zutat – ganz einfach.</p></div>
          <span class="item-count">{{ ingredients.length }} {{ ingredients.length === 1 ? 'Zutat' : 'Zutaten' }}</span>
        </div>
        <div class="ingredient-labels"><span>MENGE</span><span>EINHEIT</span><span>ZUTAT</span><span></span></div>
        <div class="ingredient-list" formArrayName="ingredients">
          @for (ingredient of ingredients.controls; track $index; let index = $index) {
            <div class="ingredient-row" [formGroupName]="index">
              <input type="text" inputmode="decimal" formControlName="amount" [attr.aria-label]="'Menge Zutat ' + (index + 1)" placeholder="200" required [attr.aria-invalid]="submitted() && ingredient.controls.amount.invalid">
              <select formControlName="unit" [attr.aria-label]="'Einheit Zutat ' + (index + 1)">
                @for (unit of units; track unit) { <option [value]="unit">{{ unit }}</option> }
              </select>
              <input type="text" formControlName="name" [attr.aria-label]="'Zutat ' + (index + 1)" placeholder="z. B. Mehl" required [attr.aria-invalid]="submitted() && ingredient.controls.name.invalid">
              <button class="remove-button" type="button" [disabled]="ingredients.length <= 1" [attr.aria-label]="'Zutat ' + (index + 1) + ' entfernen'" (click)="removeIngredient(index)"><app-icon name="close" /></button>
              @if (submitted() && ingredient.invalid) { <small class="row-error">Menge und Zutat bitte ergänzen.</small> }
            </div>
          }
        </div>
        <button class="add-row" type="button" (click)="addIngredient()"><app-icon name="plus" /> Zutat hinzufügen</button>
      </section>

      <section class="form-card">
        <div class="form-section-heading">
          <span class="step-number">3</span>
          <div><h2>So wird's gemacht</h2><p>Schritt für Schritt zum Genuss.</p></div>
          <span class="item-count">{{ steps.length }} {{ steps.length === 1 ? 'Schritt' : 'Schritte' }}</span>
        </div>
        <div class="steps-list" formArrayName="steps">
          @for (step of steps.controls; track $index; let index = $index) {
            <div class="step-row">
              <span class="step-index">{{ index + 1 }}</span>
              <textarea [formControlName]="index" [attr.aria-label]="'Zubereitungsschritt ' + (index + 1)" rows="2" [placeholder]="'Beschreibe Schritt ' + (index + 1) + ' ...'" required [attr.aria-invalid]="submitted() && step.invalid"></textarea>
              <button class="remove-button" type="button" [disabled]="steps.length <= 1" [attr.aria-label]="'Schritt ' + (index + 1) + ' entfernen'" (click)="removeStep(index)"><app-icon name="close" /></button>
              @if (submitted() && step.invalid) { <small class="row-error">Bitte beschreibe diesen Schritt.</small> }
            </div>
          }
        </div>
        <button class="add-row" type="button" (click)="addStep()"><app-icon name="plus" /> Schritt hinzufügen</button>
      </section>

      <section class="form-card">
        <div class="form-section-heading"><span class="step-number">4</span><div><h2>Das passt dazu</h2><p>Ordne dein Rezept ein und finde es später wieder.</p></div></div>
        <div class="field-row">
          <div class="field">
            <label for="category">Kategorie</label>
            <select id="category" formControlName="category">
              <option>Abendessen</option><option>Frühstück</option><option>Mittagessen</option><option>Dessert</option><option>Snack</option>
            </select>
          </div>
          <div class="field">
            <label for="tags">Tags <span class="optional">optional</span></label>
            <input id="tags" formControlName="tags" placeholder="z. B. vegetarisch, schnell">
            <small>Trenne mehrere Tags mit einem Komma.</small>
          </div>
        </div>
      </section>

      @if (notice()) { <p class="demo-result" role="status"><app-icon name="info" /> {{ notice() }}</p> }
      @if (form.invalid && submitted()) { <p class="validation-summary" role="alert">Ein paar Pflichtfelder brauchen noch deine Aufmerksamkeit.</p> }
      <div class="form-actions">
        <a class="cancel-button" routerLink="/recipes">Abbrechen</a>
        <button class="save-button" type="submit"><app-icon name="check" /> Speichern (Demo)</button>
      </div>
    </form>
    <p class="required-note"><span>*</span> Pflichtfeld · Deine Angaben gehen beim Verlassen dieser Seite verloren.</p>
  `,
  styleUrl: './recipe-editor.page.css',
})
export class RecipeEditorPage {
  private readonly fb = inject(FormBuilder).nonNullable;
  readonly units = ['g', 'kg', 'ml', 'l', 'Stk.', 'EL', 'TL', 'Prise'];
  readonly notice = signal('');
  readonly submitted = signal(false);
  readonly form = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(90)]],
    description: ['', [Validators.maxLength(500)]],
    servings: [2, [Validators.required, Validators.min(1), Validators.max(20)]],
    minutes: [30, [Validators.required, Validators.min(1), Validators.max(600)]],
    ingredients: this.fb.array([this.newIngredient()]),
    steps: this.fb.array([this.fb.control('', Validators.required)]),
    category: ['Abendessen', Validators.required],
    tags: [''],
  });

  get ingredients() { return this.form.controls.ingredients; }
  get steps() { return this.form.controls.steps; }

  addIngredient(): void { this.ingredients.push(this.newIngredient()); }
  removeIngredient(index: number): void { if (this.ingredients.length > 1) this.ingredients.removeAt(index); }
  addStep(): void { this.steps.push(this.fb.control('')); }
  removeStep(index: number): void { if (this.steps.length > 1) this.steps.removeAt(index); }

  showError(controlName: 'name' | 'servings' | 'minutes'): boolean {
    const control = this.form.controls[controlName];
    return control.invalid && (control.touched || this.submitted());
  }

  submit(): void {
    this.submitted.set(true);
    this.form.markAllAsTouched();
    this.notice.set(this.form.valid
      ? 'Demo-Modus: Es wurde nichts gespeichert. Deine Eingaben bleiben nur bis zum Verlassen dieser Seite erhalten.'
      : '');
  }

  private newIngredient() {
    return this.fb.group({
      amount: ['', [Validators.required, Validators.pattern(/^\d+(?:[.,]\d+)?$/)]],
      unit: ['g', Validators.required],
      name: ['', Validators.required],
    });
  }
}
