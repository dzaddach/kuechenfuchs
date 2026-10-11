import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../auth/auth.service';
import { ChatGptConnectionService, ExternalIdentityLink } from '../integrations/chatgpt-connection.service';
import { IconComponent } from '../shared/icon.component';

const CALLBACK_MESSAGES: Record<string, { tone: 'success' | 'error'; text: string }> = {
  linked: {
    tone: 'success',
    text: 'Dein Keycloak-Konto ist jetzt mit Küchenfuchs verknüpft. ChatGPT ist damit noch nicht autorisiert – das geschieht erst bei der Anmeldung aus deinem GPT.',
  },
  conflict: {
    tone: 'error',
    text: 'Dieses Keycloak-Konto ist bereits mit einem Küchenfuchs-Konto verknüpft oder du hast schon eine aktive Verbindung. Trenne die bestehende Verbindung zuerst. Es wurde nichts verknüpft.',
  },
  expired: { tone: 'error', text: 'Der Verknüpfungsvorgang ist abgelaufen. Bitte starte ihn erneut. Es wurde nichts verknüpft.' },
  cancelled: { tone: 'error', text: 'Die Anmeldung bei Keycloak wurde abgebrochen. Es wurde nichts verknüpft.' },
  unavailable: { tone: 'error', text: 'Keycloak war während der Verknüpfung nicht erreichbar. Es wurde nichts verknüpft.' },
  failed: { tone: 'error', text: 'Die Verknüpfung konnte nicht sicher abgeschlossen werden. Es wurde nichts verknüpft.' },
};

const dateFormat = new Intl.DateTimeFormat('de-DE', { dateStyle: 'medium', timeStyle: 'short' });

@Component({
  selector: 'app-chatgpt-connection-page',
  imports: [FormsModule, RouterLink, IconComponent],
  template: `
    <section class="connection-page">
      <a class="back-link" routerLink="/settings"><app-icon name="back" /> Zurück zu den Einstellungen</a>
      <header class="heading">
        <span class="heading-mark"><app-icon name="sparkles" /></span>
        <p class="eyebrow">VERBINDUNGEN</p>
        <h1>ChatGPT-Verbindung</h1>
        <p>Verknüpfe dein Keycloak-Konto mit Küchenfuchs und genau einem Haushalt. Erst danach kann ein GPT nach deiner Anmeldung auf diesen Haushalt zugreifen.</p>
      </header>

      @if (callbackMessage(); as message) {
        <p class="banner" [class.success]="message.tone === 'success'" [class.error]="message.tone === 'error'" role="status">{{ message.text }}</p>
      }
      @if (error()) { <p class="banner error" role="alert">{{ error() }}</p> }

      @if (loading()) {
        <p class="muted" role="status">Verbindungsstatus wird geladen …</p>
      } @else if (unavailable()) {
        <section class="card">
          <h2><app-icon name="info" /> Nicht eingerichtet</h2>
          <p class="muted">Auf diesem Küchenfuchs-Server ist die ChatGPT-Verbindung (Keycloak) nicht aktiviert. Die normale Anmeldung und alle anderen Funktionen sind davon nicht betroffen.</p>
        </section>
      } @else if (activeLink(); as link) {
        <section class="card" aria-labelledby="status-heading">
          <h2 id="status-heading"><app-icon name="shield" /> Status</h2>
          <ol class="status-steps">
            <li class="done" data-testid="keycloak-status">
              <app-icon name="check" />
              <div>
                <strong>Keycloak-Konto verknüpft</strong>
                <small>Haushalt „{{ link.householdName }}“ · seit {{ format(link.createdAt) }}</small>
              </div>
            </li>
            <li [class.done]="link.chatGpt?.state === 'authorized'" data-testid="chatgpt-status">
              <app-icon [name]="link.chatGpt?.state === 'authorized' ? 'check' : 'clock'" />
              <div>
                @switch (link.chatGpt?.state) {
                  @case ('authorized') {
                    <strong>ChatGPT autorisiert</strong>
                    <small>Keycloak meldet eine aktive ChatGPT-Sitzung
                      @if (link.chatGpt?.sessionLastAccessAt) { · zuletzt aktiv {{ format(link.chatGpt!.sessionLastAccessAt!) }} }</small>
                  }
                  @case ('unknown') {
                    <strong>ChatGPT-Status derzeit nicht prüfbar</strong>
                    <small>Keycloak ist nicht erreichbar. Eine Autorisierung wird erst angezeigt, wenn sie serverseitig bestätigt ist.</small>
                  }
                  @default {
                    <strong>ChatGPT noch nicht autorisiert</strong>
                    <small>Melde dich in deinem GPT über „Anmelden“ bei Keycloak an. Erst eine bestätigte ChatGPT-Sitzung wird hier als autorisiert angezeigt.</small>
                  }
                }
              </div>
            </li>
          </ol>
          @if (link.lastBearerAccessAt) {
            <p class="muted small">Letzter Küchenfuchs-Zugriff über ChatGPT: {{ format(link.lastBearerAccessAt) }}</p>
          }
        </section>

        <section class="card danger-card" aria-labelledby="disconnect-heading">
          <h2 id="disconnect-heading"><app-icon name="logout" /> Verbindung trennen</h2>
          <p class="muted">Trennen sperrt den Zugriff über ChatGPT sofort und beendet alle Keycloak-Sitzungen dieses Kontos. Danach ist eine neue, ausdrückliche Verknüpfung nötig.</p>
          @if (!confirmDisconnect()) {
            <button class="secondary-button" type="button" (click)="confirmDisconnect.set(true)">Verbindung trennen</button>
          } @else {
            <div class="confirm-row">
              <button class="danger-button" type="button" [disabled]="busy()" (click)="disconnect(link.id)">Endgültig trennen</button>
              <button class="secondary-button" type="button" [disabled]="busy()" (click)="confirmDisconnect.set(false)">Abbrechen</button>
            </div>
          }
        </section>
      } @else {
        @if (pendingProviderRevocation(); as pending) {
          <p class="banner error" role="alert">
            Der Zugriff über ChatGPT ist gesperrt, aber Keycloak-Sitzungen konnten nicht beendet werden.
            <button class="inline-button" type="button" [disabled]="busy()" (click)="disconnect(pending.id)">Erneut versuchen</button>
          </p>
        }
        <form class="card" (ngSubmit)="connect()" aria-labelledby="connect-heading">
          <h2 id="connect-heading"><app-icon name="users" /> Haushalt wählen</h2>
          <p class="muted">ChatGPT erhält ausschließlich Zugriff auf den hier gewählten Haushalt. Ein späterer Wechsel erfordert Trennen und neues Verknüpfen.</p>
          <fieldset class="household-options">
            <legend class="visually-hidden">Haushalt</legend>
            @for (household of households(); track household.householdId) {
              <label>
                <input type="radio" name="household" [value]="household.householdId" [(ngModel)]="selectedHouseholdId" />
                <span>{{ household.name }}</span>
              </label>
            }
          </fieldset>
          <label class="consent">
            <input type="checkbox" name="confirm" [(ngModel)]="confirmed" />
            <span>Ich möchte mein Keycloak-Konto mit Küchenfuchs und diesem Haushalt verknüpfen. Dafür melde ich mich gleich bei Keycloak an.</span>
          </label>
          <button class="primary-button" type="submit" [disabled]="busy() || !confirmed || !selectedHouseholdId">
            Mit Keycloak verknüpfen <app-icon name="arrow" />
          </button>
        </form>
      }

      @if (pastLinks().length) {
        <section class="history" aria-labelledby="history-heading">
          <h2 id="history-heading">Frühere Verbindungen</h2>
          <ul>
            @for (link of pastLinks(); track link.id) {
              <li>„{{ link.householdName }}“ · {{ format(link.createdAt) }} – getrennt {{ format(link.revokedAt!) }}</li>
            }
          </ul>
        </section>
      }
    </section>
  `,
  styleUrl: './chatgpt-connection.page.css',
})
export class ChatGptConnectionPage implements OnInit {
  private readonly connections = inject(ChatGptConnectionService);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly links = signal<ExternalIdentityLink[]>([]);
  readonly loading = signal(true);
  readonly unavailable = signal(false);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly confirmDisconnect = signal(false);
  readonly callbackMessage = signal<{ tone: 'success' | 'error'; text: string } | null>(null);
  readonly households = computed(() => this.auth.session()?.households ?? []);
  readonly activeLink = computed(() => this.links().find((link) => !link.revokedAt) ?? null);
  readonly pastLinks = computed(() => this.links().filter((link) => link.revokedAt));
  readonly pendingProviderRevocation = computed(() =>
    this.links().find((link) => link.revokedAt && !link.providerSessionsRevokedAt) ?? null);

  selectedHouseholdId = '';
  confirmed = false;

  async ngOnInit(): Promise<void> {
    const status = this.route.snapshot.queryParamMap.get('keycloakLink');
    if (status) {
      this.callbackMessage.set(CALLBACK_MESSAGES[status] ?? CALLBACK_MESSAGES['failed']);
      await this.router.navigate([], { relativeTo: this.route, queryParams: {}, replaceUrl: true });
    }

    const households = this.households();
    if (households.length === 1) this.selectedHouseholdId = households[0].householdId;
    await this.reload();
  }

  async connect(): Promise<void> {
    if (!this.confirmed || !this.selectedHouseholdId) return;
    this.busy.set(true);
    this.error.set('');
    try {
      this.redirect(await this.connections.startLink(this.selectedHouseholdId));
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'Die Verknüpfung konnte nicht gestartet werden.');
      this.busy.set(false);
    }
  }

  async disconnect(linkId: string): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      const result = await this.connections.revoke(linkId);
      this.callbackMessage.set(result.providerSessionsEnded
        ? { tone: 'success', text: 'Die Verbindung wurde getrennt. ChatGPT hat keinen Zugriff mehr auf Küchenfuchs.' }
        : { tone: 'error', text: 'Der Zugriff über ChatGPT ist gesperrt. Keycloak-Sitzungen konnten nicht beendet werden – bitte erneut versuchen.' });
      this.confirmDisconnect.set(false);
      await this.reload();
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'Die Verbindung konnte nicht getrennt werden.');
    } finally {
      this.busy.set(false);
    }
  }

  format(value: string): string {
    return dateFormat.format(new Date(value));
  }

  /** Top-level navigation to Keycloak; kept separate so tests can observe it. */
  protected redirect(url: string): void {
    window.location.assign(url);
  }

  private async reload(): Promise<void> {
    this.loading.set(true);
    try {
      const links = await this.connections.loadLinks();
      this.unavailable.set(links === null);
      this.links.set(links ?? []);
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'Der Verbindungsstatus konnte nicht geladen werden.');
    } finally {
      this.loading.set(false);
    }
  }
}
