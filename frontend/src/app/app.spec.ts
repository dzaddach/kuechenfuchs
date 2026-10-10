import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { App } from './app';

describe('App shell', () => {
  it('provides all main destinations in the responsive navigation', async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([])],
    }).compileComponents();

    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('.mobile-nav-link')).toHaveLength(5);
    expect(fixture.nativeElement.querySelector('.sidebar').textContent).toContain('Meine Rezepte');
    expect(fixture.nativeElement.textContent).toContain('Einstellungen');
    expect(fixture.nativeElement.textContent).toContain('Demnächst');

    (fixture.nativeElement.querySelector('.theme-toggle') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(document.documentElement.dataset['theme']).toBe('dark');
  });
});
