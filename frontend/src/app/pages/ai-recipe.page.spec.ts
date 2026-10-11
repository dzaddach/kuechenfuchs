import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { describe, expect, it, vi } from 'vitest';
import { AuthService } from '../auth/auth.service';
import { RecipeApiService } from '../recipes/recipe-api.service';
import { AiRecipePage } from './ai-recipe.page';

const draft = {
  recipe: {
    name: 'Kartoffelpfanne',
    description: 'Schnelles Abendessen.',
    baseServings: 3,
    preparationMinutes: 25,
    category: 'Abendessen',
    tags: ['schnell'],
    ingredients: [{ name: 'Kartoffeln', amount: 500, unitCode: 'g', foodId: null }],
    steps: [{ description: 'Kartoffeln braten.' }],
  },
  warnings: [],
};

describe('AiRecipePage', () => {
  it('requires external-processing consent and hands a validated draft to the editable recipe editor', async () => {
    const createAiDraft = vi.fn(async () => draft);
    await TestBed.configureTestingModule({
      imports: [AiRecipePage],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { session: () => ({ households: [{ householdId: 'h1' }] }) } },
        { provide: RecipeApiService, useValue: { createAiDraft } },
      ],
    }).compileComponents();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    const fixture = TestBed.createComponent(AiRecipePage);
    fixture.componentInstance.prompt = 'Ich möchte ein schnelles Abendessen.';
    await fixture.componentInstance.generate();
    expect(createAiDraft).not.toHaveBeenCalled();

    fixture.componentInstance.externalProcessingConfirmed = true;
    fixture.componentInstance.ingredientsText = 'Kartoffeln, Paprika';
    fixture.componentInstance.servings = 3;
    fixture.componentInstance.quick = true;
    await fixture.componentInstance.generate();

    expect(createAiDraft).toHaveBeenCalledWith('h1', expect.objectContaining({
      prompt: 'Ich möchte ein schnelles Abendessen.',
      desiredServings: 3,
      availableIngredients: ['Kartoffeln', 'Paprika'],
      preferences: ['schnell'],
      externalProcessingConfirmed: true,
    }));
    expect(navigate).toHaveBeenCalledWith(['/recipes/new'], {
      state: { aiRecipeDraft: draft.recipe, aiWarnings: [] },
    });
  });

  it('shows provider errors without navigating or pretending a draft was created', async () => {
    const createAiDraft = vi.fn(async () => { throw new Error('Monthly AI budget reached'); });
    await TestBed.configureTestingModule({
      imports: [AiRecipePage],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { session: () => ({ households: [{ householdId: 'h1' }] }) } },
        { provide: RecipeApiService, useValue: { createAiDraft } },
      ],
    }).compileComponents();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    const fixture = TestBed.createComponent(AiRecipePage);
    fixture.componentInstance.prompt = 'Eine einfache Gemüsesuppe bitte.';
    fixture.componentInstance.externalProcessingConfirmed = true;
    await fixture.componentInstance.generate();

    expect(fixture.componentInstance.error()).toContain('Monthly AI budget reached');
    expect(navigate).not.toHaveBeenCalled();
  });
});
