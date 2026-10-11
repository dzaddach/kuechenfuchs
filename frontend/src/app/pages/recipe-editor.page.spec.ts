import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { describe, expect, it, vi } from 'vitest';
import { AuthService } from '../auth/auth.service';
import { RecipeApiService, RecipeWrite } from '../recipes/recipe-api.service';
import { RecipeEditorPage } from './recipe-editor.page';

function setup() {
  const save = vi.fn(async (_household: string, payload: unknown) => ({ id: 'saved', ...payload as object }));
  const auth = { session: signal({ households: [{ householdId: 'h1' }] }) };
  TestBed.configureTestingModule({
    imports: [RecipeEditorPage],
    providers: [
      provideRouter([]),
      { provide: AuthService, useValue: auth },
      { provide: RecipeApiService, useValue: { units: vi.fn(async () => [
        { code: 'g', name: 'Gramm', dimension: 'mass' },
        { code: 'piece', name: 'Stück', dimension: 'count' },
      ]), save } },
    ],
  });
  return { save };
}

describe('RecipeEditorPage', () => {
  it('validates required recipe details and supports dynamic rows', async () => {
    setup();
    await TestBed.compileComponents();
    const fixture = TestBed.createComponent(RecipeEditorPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.save-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('Pflichtfelder');
    (fixture.nativeElement.querySelectorAll('.add-row')[0] as HTMLButtonElement).click();
    (fixture.nativeElement.querySelectorAll('.add-row')[1] as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.ingredient-row')).toHaveLength(2);
    expect(fixture.nativeElement.querySelectorAll('.step-row')).toHaveLength(2);
  });

  it('does not save when a dynamically added step is empty', async () => {
    const { save } = setup();
    await TestBed.compileComponents();
    const fixture = TestBed.createComponent(RecipeEditorPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    (fixture.nativeElement.querySelectorAll('.add-row')[1] as HTMLButtonElement).click();
    const name = fixture.nativeElement.querySelector('#recipe-name') as HTMLInputElement;
    name.value = 'Mein Rezept';
    name.dispatchEvent(new Event('input'));
    const ingredient = fixture.nativeElement.querySelector('[aria-label="Zutat 1"]') as HTMLInputElement;
    ingredient.value = 'Mehl';
    ingredient.dispatchEvent(new Event('input'));
    const step = fixture.nativeElement.querySelector('[aria-label="Zubereitungsschritt 1"]') as HTMLTextAreaElement;
    step.value = 'Alles verrühren.';
    step.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.save-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.step-row textarea')[1].getAttribute('aria-invalid')).toBe('true');
    expect(save).not.toHaveBeenCalled();
  });

  it('scales displayed quantities with display-only rounding', async () => {
    setup();
    await TestBed.compileComponents();
    const fixture = TestBed.createComponent(RecipeEditorPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const amount = fixture.nativeElement.querySelector('[aria-label="Menge Zutat 1"]') as HTMLInputElement;
    amount.value = '0.333';
    amount.dispatchEvent(new Event('input'));
    const target = fixture.nativeElement.querySelector('#target-servings') as HTMLInputElement;
    target.value = '3';
    target.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Für 3 Portionen: 0,5 g');
    expect(amount.value).toBe('0.333');
  });

  it('saves base quantities unchanged when displaying another serving count', async () => {
    const { save } = setup();
    await TestBed.compileComponents();
    const fixture = TestBed.createComponent(RecipeEditorPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const fields: Array<[string, string]> = [
      ['#recipe-name', 'Pasta'],
      ['[aria-label="Menge Zutat 1"]', '250'],
      ['[aria-label="Zutat 1"]', 'Nudeln'],
      ['[aria-label="Zubereitungsschritt 1"]', 'Kochen.'],
    ];
    for (const [selector, value] of fields) {
      const field = fixture.nativeElement.querySelector(selector) as HTMLInputElement | HTMLTextAreaElement;
      field.value = value;
      field.dispatchEvent(new Event('input'));
    }
    const target = fixture.nativeElement.querySelector('#target-servings') as HTMLInputElement;
    target.value = '4';
    target.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    const payload = save.mock.calls[0][1] as RecipeWrite;
    expect(payload.baseServings).toBe(2);
    expect(payload.ingredients[0].amount).toBe(250);
  });

  it('loads an AI draft as an editable preview and only saves it on explicit takeover', async () => {
    const { save } = setup();
    const originalState = history.state;
    history.replaceState({
      aiRecipeDraft: {
        name: 'Kartoffelpfanne',
        description: 'Schnelles Abendessen.',
        baseServings: 3,
        preparationMinutes: 25,
        category: 'Abendessen',
        tags: ['schnell'],
        ingredients: [{ name: 'Kartoffeln', amount: 500, unitCode: 'g', foodId: null }],
        steps: [{ description: 'Kartoffeln braten.' }],
      },
      aiWarnings: ['Bitte Mengen prüfen.'],
    }, '');
    await TestBed.compileComponents();
    const router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
    const fixture = TestBed.createComponent(RecipeEditorPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#recipe-name').value).toBe('Kartoffelpfanne');
    expect(fixture.nativeElement.textContent).toContain('Bitte Mengen prüfen.');
    expect(fixture.nativeElement.querySelector('.save-button').textContent).toContain('Rezept übernehmen');
    expect(save).not.toHaveBeenCalled();

    const name = fixture.nativeElement.querySelector('#recipe-name') as HTMLInputElement;
    name.value = 'Meine angepasste Kartoffelpfanne';
    name.dispatchEvent(new Event('input'));
    const form = fixture.nativeElement.querySelector('form') as HTMLFormElement;
    form.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    expect(save).toHaveBeenCalledOnce();
    expect(save.mock.calls[0][1]).toMatchObject({ name: 'Meine angepasste Kartoffelpfanne', baseServings: 3 });
    expect(router.navigate).toHaveBeenCalledWith(['/recipes', 'saved']);
    history.replaceState(originalState, '');
  });
});
