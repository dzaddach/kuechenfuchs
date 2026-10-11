import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { describe, expect, it, vi } from 'vitest';
import { AuthService } from '../auth/auth.service';
import { RecipeApiService, RecipeRecord } from '../recipes/recipe-api.service';
import { RecipeDetailPage } from './recipe-detail.page';

const recipe: RecipeRecord = {
  id: 'recipe-1',
  name: 'Pasta mit Zitrone',
  description: 'Frisch und einfach.',
  baseServings: 2,
  preparationMinutes: 25,
  category: 'Abendessen',
  tags: ['vegetarisch'],
  isArchived: false,
  version: 'version-1',
  ingredients: [
    { id: 'ingredient-1', name: 'Nudeln', amount: 250, unitCode: 'g', foodId: null },
    { id: 'ingredient-2', name: 'Zitrone', amount: null, unitCode: 'piece', foodId: null },
  ],
  steps: [
    { id: 'step-1', description: 'Nudeln in Salzwasser kochen.' },
    { id: 'step-2', description: 'Zitrone darüber reiben.' },
  ],
};

describe('RecipeDetailPage', () => {
  it('shows ingredients and steps separately from edit mode and scales display amounts', async () => {
    await TestBed.configureTestingModule({
      imports: [RecipeDetailPage],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 'recipe-1' } } } },
        { provide: AuthService, useValue: { session: () => ({ households: [{ householdId: 'household-1' }] }) } },
        { provide: RecipeApiService, useValue: {
          get: vi.fn(async () => recipe),
          units: vi.fn(async () => [
            { code: 'g', name: 'Gramm', dimension: 'mass' },
            { code: 'piece', name: 'Stück', dimension: 'count' },
          ]),
        } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(RecipeDetailPage);
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('h1').textContent).toContain(recipe.name);
    expect(fixture.nativeElement.textContent).toContain('Nudeln in Salzwasser kochen.');
    expect(fixture.nativeElement.textContent).toContain('Zitrone darüber reiben.');
    expect(fixture.nativeElement.textContent).toContain('250 Gramm');
    expect(fixture.nativeElement.querySelector('textarea')).toBeNull();
    expect(fixture.nativeElement.querySelector('.edit-button').getAttribute('href')).toContain('/edit');

    const servings = fixture.nativeElement.querySelector('[aria-label="Portionenzahl für Anzeige"]') as HTMLInputElement;
    servings.value = '4';
    servings.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('500 Gramm');
    expect(fixture.nativeElement.textContent).toContain('nach Geschmack');
  });
});
