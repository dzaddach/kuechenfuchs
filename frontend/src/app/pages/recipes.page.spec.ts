import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { RecipesPage } from './recipes.page';

describe('RecipesPage', () => {
  it('searches demo recipes by ingredient', async () => {
    await TestBed.configureTestingModule({
      imports: [RecipesPage],
      providers: [provideRouter([])],
    }).compileComponents();

    const fixture = TestBed.createComponent(RecipesPage);
    fixture.detectChanges();
    const search = fixture.nativeElement.querySelector('input[type="search"]') as HTMLInputElement;
    search.value = 'zitrone';
    search.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('.recipe-card')).toHaveLength(1);
    expect(fixture.nativeElement.textContent).toContain('Cremige Zitronenpasta');
  });

  it('filters favorites and supports empty-state recovery', async () => {
    await TestBed.configureTestingModule({
      imports: [RecipesPage],
      providers: [provideRouter([])],
    }).compileComponents();

    const fixture = TestBed.createComponent(RecipesPage);
    fixture.detectChanges();
    const favorites = fixture.nativeElement.querySelector('.filter-button') as HTMLButtonElement;
    favorites.click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.recipe-card')).toHaveLength(1);

    const search = fixture.nativeElement.querySelector('input[type="search"]') as HTMLInputElement;
    search.value = 'does not exist';
    search.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.empty-state')).not.toBeNull();
    (fixture.nativeElement.querySelector('.secondary-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.recipe-card')).toHaveLength(4);
  });

  it('opens an accessible recipe dialog and closes it with Escape', async () => {
    await TestBed.configureTestingModule({
      imports: [RecipesPage],
      providers: [provideRouter([])],
    }).compileComponents();

    const fixture = TestBed.createComponent(RecipesPage);
    fixture.detectChanges();
    const trigger = fixture.nativeElement.querySelector('.recipe-title') as HTMLButtonElement;
    trigger.click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="dialog"]')).not.toBeNull();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    await Promise.resolve();
    expect(fixture.nativeElement.querySelector('[role="dialog"]')).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });
});
