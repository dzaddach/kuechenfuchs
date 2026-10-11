import { Injectable, signal } from '@angular/core';

export interface HouseholdSession {
  householdId: string;
  name: string;
  role: string;
}

export interface UserSession {
  userId: string;
  email: string;
  households: HouseholdSession[];
}

interface CsrfResponse {
  requestToken: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  readonly session = signal<UserSession | null>(null);
  readonly error = signal('');
  private pendingSessionCheck: Promise<boolean> | null = null;

  ensureSession(): Promise<boolean> {
    if (this.session()) return Promise.resolve(true);
    if (this.pendingSessionCheck) return this.pendingSessionCheck;

    this.pendingSessionCheck = this.readSession().finally(() => {
      this.pendingSessionCheck = null;
    });
    return this.pendingSessionCheck;
  }

  async login(email: string, password: string): Promise<boolean> {
    this.error.set('');
    try {
      const csrf = await this.getCsrfToken();
      const response = await fetch('/api/auth/login', {
        method: 'POST',
        credentials: 'same-origin',
        headers: {
          'Content-Type': 'application/json',
          'X-KF-CSRF': csrf,
        },
        body: JSON.stringify({ email, password }),
      });
      if (response.status === 401) {
        this.error.set('E-Mail-Adresse oder Passwort ist nicht korrekt.');
        return false;
      }
      if (response.status === 429) {
        this.error.set('Zu viele Anmeldeversuche. Bitte warte kurz und versuche es erneut.');
        return false;
      }
      if (!response.ok) {
        this.error.set('Die Anmeldung konnte nicht abgeschlossen werden. Bitte versuche es erneut.');
        return false;
      }

      this.session.set(await response.json() as UserSession);
      return true;
    } catch {
      this.error.set('Küchenfuchs ist gerade nicht erreichbar. Prüfe deine Verbindung und versuche es erneut.');
      return false;
    }
  }

  async logout(): Promise<boolean> {
    this.error.set('');
    try {
      const csrf = await this.getCsrfToken();
      const response = await fetch('/api/auth/logout', {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'X-KF-CSRF': csrf },
      });
      if (!response.ok) {
        this.error.set('Du konntest nicht sicher abgemeldet werden. Bitte versuche es erneut.');
        return false;
      }

      this.session.set(null);
      return true;
    } catch {
      this.error.set('Küchenfuchs ist gerade nicht erreichbar. Prüfe deine Verbindung und versuche es erneut.');
      return false;
    }
  }

  private async readSession(): Promise<boolean> {
    this.error.set('');
    try {
      const response = await fetch('/api/auth/session', { credentials: 'same-origin' });
      if (response.status === 401) {
        this.session.set(null);
        return false;
      }
      if (!response.ok) {
        this.error.set('Deine Sitzung konnte nicht geprüft werden. Bitte versuche es erneut.');
        return false;
      }

      this.session.set(await response.json() as UserSession);
      return true;
    } catch {
      this.error.set('Küchenfuchs ist gerade nicht erreichbar. Prüfe deine Verbindung und versuche es erneut.');
      return false;
    }
  }

  private async getCsrfToken(): Promise<string> {
    const response = await fetch('/api/auth/csrf', { credentials: 'same-origin' });
    if (!response.ok) throw new Error('Unable to obtain an anti-forgery token.');
    const result = await response.json() as CsrfResponse;
    if (!result.requestToken) throw new Error('The anti-forgery token was empty.');
    return result.requestToken;
  }
}
