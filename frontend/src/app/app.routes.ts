import { Routes } from '@angular/router';
import { requireSession } from './auth/auth.guard';

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./pages/login.page').then((m) => m.LoginPage) },
  {
    path: '',
    canActivateChild: [requireSession],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'today' },
      { path: 'today', loadComponent: () => import('./pages/today.page').then((m) => m.TodayPage) },
      { path: 'recipes', loadComponent: () => import('./pages/recipes.page').then((m) => m.RecipesPage) },
      { path: 'ai/recipe', loadComponent: () => import('./pages/ai-recipe.page').then((m) => m.AiRecipePage) },
      { path: 'recipes/new', loadComponent: () => import('./pages/recipe-editor.page').then((m) => m.RecipeEditorPage) },
      { path: 'recipes/:id', loadComponent: () => import('./pages/recipe-detail.page').then((m) => m.RecipeDetailPage) },
      { path: 'recipes/:id/edit', loadComponent: () => import('./pages/recipe-editor.page').then((m) => m.RecipeEditorPage) },
      { path: 'shopping', loadComponent: () => import('./pages/placeholder.page').then((m) => m.PlaceholderPage), data: { page: 'shopping' } },
      { path: 'offers', loadComponent: () => import('./pages/placeholder.page').then((m) => m.PlaceholderPage), data: { page: 'offers' } },
      { path: 'settings', loadComponent: () => import('./pages/settings.page').then((m) => m.SettingsPage) },
      { path: 'settings/chatgpt', loadComponent: () => import('./pages/chatgpt-connection.page').then((m) => m.ChatGptConnectionPage) },
      { path: '**', redirectTo: 'today' },
    ],
  },
];
