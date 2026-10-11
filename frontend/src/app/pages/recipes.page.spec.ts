import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { describe, expect, it, vi } from 'vitest';
import { AuthService } from '../auth/auth.service';
import { RecipeApiService, RecipeSummary } from '../recipes/recipe-api.service';
import { RecipesPage } from './recipes.page';

const recipe: RecipeSummary = {
  id: 'r1',
  name: 'Cremige Zitronenpasta',
  description: 'Frisch und schnell.',
  baseServings: 2,
  preparationMinutes: 20,
  category: 'Abendessen',
  tags: ['vegetarisch'],
  isArchived: false,
  version: 'v1',
};

describe('RecipesPage', () => {
  it('loads household recipes and searches by ingredient', async () => {
    const list = vi.fn(async (_household: string, query = '') => query ? [recipe] : [recipe]);
    await TestBed.configureTestingModule({
      imports: [RecipesPage],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { session: () => ({ households: [{ householdId: 'h1' }] }) } },
        { provide: RecipeApiService, useValue: { list } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(RecipesPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.recipe-card')).toHaveLength(1);

    const search = fixture.nativeElement.querySelector('input[type="search"]') as HTMLInputElement;
    search.value = 'zitrone';
    search.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Cremige Zitronenpasta');
    await new Promise((resolve) => setTimeout(resolve, 300));
    expect(list).toHaveBeenCalledWith('h1', 'zitrone');
  });

  it('shows an explicit empty state when the API has no recipes', async () => {
    await TestBed.configureTestingModule({
      imports: [RecipesPage],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { session: () => ({ households: [{ householdId: 'h1' }] }) } },
        { provide: RecipeApiService, useValue: { list: vi.fn(async () => []) } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(RecipesPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.empty-state')).not.toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Deine Sammlung beginnt hier');
  });

  it('keeps client favorites and category filtering available', async () => {
    const vegetarianRecipe = { ...recipe, category: 'Vegetarisch' };
    await TestBed.configureTestingModule({
      imports: [RecipesPage],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { session: () => ({ households: [{ householdId: 'h1' }] }) } },
        { provide: RecipeApiService, useValue: { list: vi.fn(async () => [vegetarianRecipe]) } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(RecipesPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.favorite-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.filter-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.recipe-card')).toHaveLength(1);
    const vegetarianTab = [...fixture.nativeElement.querySelectorAll('.category-tab')]
      .find((button: HTMLButtonElement) => button.textContent.trim() === 'Vegetarisch') as HTMLButtonElement;
    vegetarianTab.click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.recipe-card')).toHaveLength(1);
  });
});
