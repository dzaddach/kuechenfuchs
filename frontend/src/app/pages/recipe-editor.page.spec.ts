import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { RecipeEditorPage } from './recipe-editor.page';

describe('RecipeEditorPage', () => {
  it('validates required recipe details and dynamic rows', async () => {
    await TestBed.configureTestingModule({
      imports: [RecipeEditorPage],
      providers: [provideRouter([])],
    }).compileComponents();

    const fixture = TestBed.createComponent(RecipeEditorPage);
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.save-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('Pflichtfelder');

    (fixture.nativeElement.querySelectorAll('.add-row')[0] as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.ingredient-row')).toHaveLength(2);
    (fixture.nativeElement.querySelectorAll('.add-row')[1] as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.step-row')).toHaveLength(2);
  });

  it('never pretends a valid demo recipe was persisted', async () => {
    await TestBed.configureTestingModule({
      imports: [RecipeEditorPage],
      providers: [provideRouter([])],
    }).compileComponents();

    const fixture = TestBed.createComponent(RecipeEditorPage);
    fixture.detectChanges();
    const name = fixture.nativeElement.querySelector('#recipe-name') as HTMLInputElement;
    name.value = 'Mein Demo-Rezept';
    name.dispatchEvent(new Event('input'));
    const amount = fixture.nativeElement.querySelector('[aria-label="Menge Zutat 1"]') as HTMLInputElement;
    amount.value = '200';
    amount.dispatchEvent(new Event('input'));
    const ingredient = fixture.nativeElement.querySelector('[aria-label="Zutat 1"]') as HTMLInputElement;
    ingredient.value = 'Mehl';
    ingredient.dispatchEvent(new Event('input'));
    const step = fixture.nativeElement.querySelector('[aria-label="Zubereitungsschritt 1"]') as HTMLTextAreaElement;
    step.value = 'Alles verrühren.';
    step.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('.save-button') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="status"]').textContent).toContain('Es wurde nichts gespeichert');
  });
});
