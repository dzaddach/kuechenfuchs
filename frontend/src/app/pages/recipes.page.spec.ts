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
    const list = vi.fn(async (_household: string, _query = '') => [recipe]);
    await TestBed.configureTestingModule({
      imports: [RecipesPage],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { session: () => ({ households: [{ householdId: 'h1' }] }) } },
        { provide: RecipeApiService, useValue: { list, favorites: vi.fn(async () => []), setFavorite: vi.fn() } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(RecipesPage);
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
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
        { provide: RecipeApiService, useValue: { list: vi.fn(async () => []), favorites: vi.fn(async () => []), setFavorite: vi.fn() } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(RecipesPage);
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.empty-state')).not.toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Deine Sammlung beginnt hier');
  });

  it('persists household favorites and reloads them from the API', async () => {
    const vegetarianRecipe = { ...recipe, category: 'Vegetarisch' };
    const favoriteIds = new Set<string>();
    const favorites = vi.fn(async () => [...favoriteIds]);
    const setFavorite = vi.fn(async (_householdId: string, recipeId: string, isFavorite: boolean) => {
      if (isFavorite) favoriteIds.add(recipeId);
      else favoriteIds.delete(recipeId);
    });
    await TestBed.configureTestingModule({
      imports: [RecipesPage],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { session: () => ({ households: [{ householdId: 'h1' }] }) } },
        { provide: RecipeApiService, useValue: { list: vi.fn(async () => [vegetarianRecipe]), favorites, setFavorite } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(RecipesPage);
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
    const favorite = fixture.nativeElement.querySelector('.favorite-button') as HTMLButtonElement;
    favorite.click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(setFavorite).toHaveBeenCalledWith('h1', 'r1', true);
    expect(favorite.getAttribute('aria-pressed')).toBe('true');

    fixture.destroy();
    const reloadedFixture = TestBed.createComponent(RecipesPage);
    reloadedFixture.detectChanges();
    await reloadedFixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    reloadedFixture.detectChanges();
    const reloadedFavorite = reloadedFixture.nativeElement.querySelector('.favorite-button') as HTMLButtonElement | null;
    expect(reloadedFavorite, reloadedFixture.nativeElement.textContent).not.toBeNull();
    expect(reloadedFavorite!.getAttribute('aria-pressed')).toBe('true');

    (reloadedFixture.nativeElement.querySelector('.filter-button') as HTMLButtonElement).click();
    reloadedFixture.detectChanges();
    expect(reloadedFixture.nativeElement.querySelectorAll('.recipe-card')).toHaveLength(1);
    const vegetarianTab = [...reloadedFixture.nativeElement.querySelectorAll('.category-tab')]
      .find((button: HTMLButtonElement) => button.textContent.trim() === 'Vegetarisch') as HTMLButtonElement;
    vegetarianTab.click();
    reloadedFixture.detectChanges();
    expect(reloadedFixture.nativeElement.querySelectorAll('.recipe-card')).toHaveLength(1);
  });

  it('does not let an older search response overwrite the latest results', async () => {
    const deferred = <T>() => {
      let resolve!: (value: T) => void;
      const promise = new Promise<T>((complete) => { resolve = complete; });
      return { promise, resolve };
    };
    const oldResults = deferred<RecipeSummary[]>();
    const newResults = deferred<RecipeSummary[]>();
    const list = vi.fn((_household: string, query = '') => {
      if (query === 'older') return oldResults.promise;
      if (query === 'newer') return newResults.promise;
      return Promise.resolve([recipe]);
    });
    await TestBed.configureTestingModule({
      imports: [RecipesPage],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { session: () => ({ households: [{ householdId: 'h1' }] }) } },
        { provide: RecipeApiService, useValue: { list, favorites: vi.fn(async () => []), setFavorite: vi.fn() } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(RecipesPage);
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
    const search = fixture.nativeElement.querySelector('input[type="search"]') as HTMLInputElement;

    search.value = 'older';
    search.dispatchEvent(new Event('input'));
    await new Promise((resolve) => setTimeout(resolve, 300));
    search.value = 'newer';
    search.dispatchEvent(new Event('input'));
    await new Promise((resolve) => setTimeout(resolve, 300));

    const latest = { ...recipe, id: 'new', name: 'Newer search result' };
    newResults.resolve([latest]);
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
    oldResults.resolve([{ ...recipe, id: 'old', name: 'Veraltete Suche' }]);
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Newer search result');
    expect(fixture.nativeElement.textContent).not.toContain('Veraltete Suche');
  });
});
