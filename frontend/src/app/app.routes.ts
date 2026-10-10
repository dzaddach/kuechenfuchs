import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'today' },
  { path: 'today', loadComponent: () => import('./pages/today.page').then((m) => m.TodayPage) },
  { path: 'recipes', loadComponent: () => import('./pages/recipes.page').then((m) => m.RecipesPage) },
  { path: 'recipes/new', loadComponent: () => import('./pages/recipe-editor.page').then((m) => m.RecipeEditorPage) },
  { path: 'shopping', loadComponent: () => import('./pages/placeholder.page').then((m) => m.PlaceholderPage), data: { page: 'shopping' } },
  { path: 'offers', loadComponent: () => import('./pages/placeholder.page').then((m) => m.PlaceholderPage), data: { page: 'offers' } },
  { path: 'settings', loadComponent: () => import('./pages/placeholder.page').then((m) => m.PlaceholderPage), data: { page: 'settings' } },
  { path: '**', redirectTo: 'today' },
];
