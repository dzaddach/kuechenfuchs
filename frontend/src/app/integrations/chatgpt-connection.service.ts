import { Injectable, inject } from '@angular/core';
import { AuthService } from '../auth/auth.service';

export type ChatGptAuthorizationState = 'authorized' | 'not-authorized' | 'unknown';

export interface ExternalIdentityLink {
  id: string;
  issuer: string;
  householdId: string;
  householdName: string;
  createdAt: string;
  revokedAt: string | null;
  providerSessionsRevokedAt: string | null;
  lastBearerAccessAt: string | null;
  chatGpt: { state: ChatGptAuthorizationState; sessionLastAccessAt: string | null } | null;
}

export interface RevokeResult {
  revoked: boolean;
  providerSessionsEnded: boolean;
}

const base = '/api/v1/integrations/keycloak';

/**
 * Verwaltung der Keycloak-Verknüpfung für ChatGPT. Arbeitet ausschließlich mit der Cookie-Sitzung und
 * CSRF-Token; Access- oder Refresh-Tokens erreichen den Browser nie und werden nirgends gespeichert.
 */
@Injectable({ providedIn: 'root' })
export class ChatGptConnectionService {
  private readonly auth = inject(AuthService);

  /** Verknüpfungen des Benutzers; null, wenn die Integration auf diesem Server nicht eingerichtet ist. */
  async loadLinks(): Promise<ExternalIdentityLink[] | null> {
    const response = await fetch(`${base}/links`, { credentials: 'same-origin' });
    if (response.status === 404) return null;
    if (!response.ok) throw new Error(`Verbindungsstatus nicht abrufbar (${response.status}).`);
    return await response.json() as ExternalIdentityLink[];
  }

  async startLink(householdId: string): Promise<string> {
    const response = await fetch(`${base}/link-requests`, {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json', 'X-KF-CSRF': await this.auth.getCsrfToken() },
      body: JSON.stringify({ householdId }),
    });
    if (response.status === 503) throw new Error('Keycloak ist gerade nicht erreichbar. Bitte versuche es später erneut.');
    if (!response.ok) throw new Error('Die Verknüpfung konnte nicht gestartet werden.');
    const result = await response.json() as { authorizationUrl: string };
    return result.authorizationUrl;
  }

  async revoke(linkId: string): Promise<RevokeResult> {
    const response = await fetch(`${base}/links/${encodeURIComponent(linkId)}`, {
      method: 'DELETE',
      credentials: 'same-origin',
      headers: { 'X-KF-CSRF': await this.auth.getCsrfToken() },
    });
    if (!response.ok) throw new Error('Die Verbindung konnte nicht getrennt werden.');
    return await response.json() as RevokeResult;
  }
}
