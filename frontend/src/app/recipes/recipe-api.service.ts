import { Injectable, inject } from '@angular/core';
import { AuthService } from '../auth/auth.service';

export interface RecipeIngredient {
  id?: string;
  position?: number;
  name: string;
  amount: number | null;
  unitCode: string;
  foodId: string | null;
  foodName?: string | null;
}

export interface RecipeStep {
  id?: string;
  position?: number;
  description: string;
}

export interface RecipeRecord {
  id: string;
  name: string;
  description: string;
  baseServings: number;
  preparationMinutes: number;
  category: string;
  tags: string[];
  isArchived: boolean;
  version: string;
  ingredients: RecipeIngredient[];
  steps: RecipeStep[];
}

export type RecipeSummary = Omit<RecipeRecord, 'ingredients' | 'steps'>;

export type RecipeWrite = Omit<RecipeRecord, 'id' | 'isArchived' | 'version' | 'ingredients' | 'steps'> & {
  ingredients: RecipeIngredient[];
  steps: RecipeStep[];
};

export interface RecipeUnit {
  code: string;
  name: string;
  dimension: string;
}

@Injectable({ providedIn: 'root' })
export class RecipeApiService {
  private readonly auth = inject(AuthService);

  async list(householdId: string, query = ''): Promise<RecipeSummary[]> {
    const suffix = query.trim() ? `?q=${encodeURIComponent(query.trim())}` : '';
    return this.request(`/api/v1/households/${householdId}/recipes${suffix}`);
  }

  async get(householdId: string, recipeId: string): Promise<RecipeRecord> {
    return this.request(`/api/v1/households/${householdId}/recipes/${recipeId}`);
  }

  async units(householdId: string): Promise<RecipeUnit[]> {
    return this.request(`/api/v1/households/${householdId}/units`);
  }

  async save(householdId: string, recipe: RecipeWrite, recipeId?: string, version?: string): Promise<RecipeRecord> {
    const csrf = await this.auth.getCsrfToken();
    const response = await fetch(
      `/api/v1/households/${householdId}/recipes${recipeId ? `/${recipeId}` : ''}`,
      {
        method: recipeId ? 'PUT' : 'POST',
        credentials: 'same-origin',
        headers: {
          'Content-Type': 'application/json',
          'X-KF-CSRF': csrf,
          ...(version ? { 'If-Match': `"${version}"` } : {}),
        },
        body: JSON.stringify(recipe),
      },
    );
    if (!response.ok) throw new Error(await this.errorMessage(response));
    return await response.json() as RecipeRecord;
  }

  async archive(householdId: string, recipe: RecipeRecord): Promise<void> {
    const csrf = await this.auth.getCsrfToken();
    const response = await fetch(`/api/v1/households/${householdId}/recipes/${recipe.id}`, {
      method: 'DELETE',
      credentials: 'same-origin',
      headers: { 'X-KF-CSRF': csrf, 'If-Match': `"${recipe.version}"` },
    });
    if (!response.ok) throw new Error(await this.errorMessage(response));
  }

  private async request<T>(url: string): Promise<T> {
    const response = await fetch(url, { credentials: 'same-origin' });
    if (!response.ok) throw new Error(await this.errorMessage(response));
    return await response.json() as T;
  }

  private async errorMessage(response: Response): Promise<string> {
    if (response.status === 412) return 'Das Rezept wurde zwischenzeitlich geändert. Bitte lade es erneut.';
    if (response.status === 428) return 'Die Rezeptversion fehlt. Bitte lade das Rezept erneut.';
    const body = await response.json().catch(() => null) as { title?: string } | null;
    return body?.title ?? `Anfrage fehlgeschlagen (${response.status}).`;
  }
}
