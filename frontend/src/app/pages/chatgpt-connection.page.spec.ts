import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { describe, expect, it, vi } from 'vitest';
import { AuthService } from '../auth/auth.service';
import { ChatGptConnectionService, ExternalIdentityLink } from '../integrations/chatgpt-connection.service';
import { ChatGptConnectionPage } from './chatgpt-connection.page';

const households = [
  { householdId: 'h1', name: 'Zuhause', role: 'Owner' },
  { householdId: 'h2', name: 'Ferienhaus', role: 'Member' },
];

function link(overrides: Partial<ExternalIdentityLink> = {}): ExternalIdentityLink {
  return {
    id: 'l1',
    issuer: 'https://kuechenfuchs.example/auth/realms/kuechenfuchs',
    householdId: 'h1',
    householdName: 'Zuhause',
    createdAt: '2026-10-11T10:00:00Z',
    revokedAt: null,
    providerSessionsRevokedAt: null,
    lastBearerAccessAt: null,
    chatGpt: { state: 'not-authorized', sessionLastAccessAt: null },
    ...overrides,
  };
}

async function setup(
  connections: Partial<Record<keyof ChatGptConnectionService, unknown>>,
  query: Record<string, string> = {},
) {
  await TestBed.configureTestingModule({
    imports: [ChatGptConnectionPage],
    providers: [
      provideRouter([]),
      { provide: AuthService, useValue: { session: () => ({ households }) } },
      { provide: ChatGptConnectionService, useValue: connections },
      { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(query) } } },
    ],
  }).compileComponents();
  const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  const fixture = TestBed.createComponent(ChatGptConnectionPage);
  const redirect = vi.spyOn(fixture.componentInstance as unknown as { redirect: (url: string) => void }, 'redirect')
    .mockImplementation(() => undefined);
  fixture.detectChanges();
  await fixture.whenStable();
  await new Promise((resolve) => setTimeout(resolve));
  fixture.detectChanges();
  return { fixture, navigate, redirect, text: () => (fixture.nativeElement as HTMLElement).textContent ?? '' };
}

describe('ChatGptConnectionPage', () => {
  it('explains that the integration is not configured without breaking the page', async () => {
    const { text } = await setup({ loadLinks: vi.fn(async () => null) });
    expect(text()).toContain('Nicht eingerichtet');
    expect(text()).not.toContain('Mit Keycloak verknüpfen');
  });

  it('requires a household and explicit confirmation before starting the Keycloak link', async () => {
    const startLink = vi.fn(async () => 'https://kuechenfuchs.example/auth/realms/kuechenfuchs/protocol/openid-connect/auth?x=1');
    const { fixture, redirect, text } = await setup({ loadLinks: vi.fn(async () => []), startLink });
    expect(text()).toContain('Zuhause');
    expect(text()).toContain('Ferienhaus');

    await fixture.componentInstance.connect();
    expect(startLink).not.toHaveBeenCalled();

    fixture.componentInstance.selectedHouseholdId = 'h2';
    await fixture.componentInstance.connect();
    expect(startLink).not.toHaveBeenCalled();

    fixture.componentInstance.confirmed = true;
    await fixture.componentInstance.connect();
    expect(startLink).toHaveBeenCalledWith('h2');
    expect(redirect).toHaveBeenCalledWith('https://kuechenfuchs.example/auth/realms/kuechenfuchs/protocol/openid-connect/auth?x=1');
  });

  it('shows a start error and does not navigate away', async () => {
    const { fixture, redirect, text } = await setup({
      loadLinks: vi.fn(async () => []),
      startLink: vi.fn(async () => { throw new Error('Keycloak ist gerade nicht erreichbar.'); }),
    });
    fixture.componentInstance.selectedHouseholdId = 'h1';
    fixture.componentInstance.confirmed = true;
    await fixture.componentInstance.connect();
    fixture.detectChanges();
    expect(redirect).not.toHaveBeenCalled();
    expect(text()).toContain('Keycloak ist gerade nicht erreichbar.');
  });

  it('shows the callback result once and removes it from the URL', async () => {
    const { navigate, text } = await setup({ loadLinks: vi.fn(async () => [link()]) }, { keycloakLink: 'linked' });
    expect(text()).toContain('ist jetzt mit Küchenfuchs verknüpft');
    expect(text()).toContain('ChatGPT ist damit noch nicht autorisiert');
    expect(navigate).toHaveBeenCalledWith([], expect.objectContaining({ queryParams: {}, replaceUrl: true }));
  });

  it('reports a failed link without claiming success', async () => {
    const { text } = await setup({ loadLinks: vi.fn(async () => []) }, { keycloakLink: 'conflict' });
    expect(text()).toContain('Es wurde nichts verknüpft');
    expect(text()).not.toContain('Keycloak-Konto verknüpft');
  });

  it('distinguishes a linked Keycloak account from an authorized ChatGPT connection', async () => {
    const { text } = await setup({ loadLinks: vi.fn(async () => [link()]) });
    expect(text()).toContain('Keycloak-Konto verknüpft');
    expect(text()).toContain('Haushalt „Zuhause“');
    expect(text()).toContain('ChatGPT noch nicht autorisiert');
    expect(text()).not.toContain('ChatGPT autorisiert');
  });

  it('does not show ChatGPT as authorized when the server cannot confirm it', async () => {
    const { text } = await setup({
      loadLinks: vi.fn(async () => [link({ chatGpt: { state: 'unknown', sessionLastAccessAt: null } })]),
    });
    expect(text()).toContain('nicht prüfbar');
    expect(text()).not.toContain('ChatGPT autorisiert');
  });

  it('shows ChatGPT as authorized only when the server reports a ChatGPT session', async () => {
    const { text } = await setup({
      loadLinks: vi.fn(async () => [link({
        chatGpt: { state: 'authorized', sessionLastAccessAt: '2026-10-11T11:00:00Z' },
        lastBearerAccessAt: '2026-10-11T11:01:00Z',
      })]),
    });
    expect(text()).toContain('ChatGPT autorisiert');
    expect(text()).toContain('Letzter Küchenfuchs-Zugriff über ChatGPT');
  });

  it('disconnects only after explicit confirmation and reloads the status', async () => {
    const loadLinks = vi.fn()
      .mockResolvedValueOnce([link()])
      .mockResolvedValueOnce([link({ revokedAt: '2026-10-11T12:00:00Z', providerSessionsRevokedAt: '2026-10-11T12:00:01Z', chatGpt: null })]);
    const revoke = vi.fn(async () => ({ revoked: true, providerSessionsEnded: true }));
    const { fixture, text } = await setup({ loadLinks, revoke });
    const disconnectButton = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')]
      .find((button) => button.textContent?.includes('Verbindung trennen'))!;
    disconnectButton.click();
    fixture.detectChanges();
    expect(revoke).not.toHaveBeenCalled();
    expect(text()).toContain('Endgültig trennen');

    await fixture.componentInstance.disconnect('l1');
    fixture.detectChanges();
    expect(revoke).toHaveBeenCalledWith('l1');
    expect(text()).toContain('Die Verbindung wurde getrennt');
    expect(text()).toContain('Mit Keycloak verknüpfen');
    expect(text()).toContain('Frühere Verbindungen');
  });

  it('warns and offers a retry when Keycloak sessions could not be ended', async () => {
    const revokedLink = link({ revokedAt: '2026-10-11T12:00:00Z', chatGpt: null });
    const loadLinks = vi.fn().mockResolvedValueOnce([link()]).mockResolvedValue([revokedLink]);
    const revoke = vi.fn(async () => ({ revoked: true, providerSessionsEnded: false }));
    const { fixture, text } = await setup({ loadLinks, revoke });
    await fixture.componentInstance.disconnect('l1');
    fixture.detectChanges();
    expect(text()).toContain('Keycloak-Sitzungen konnten nicht beendet werden');
    expect(text()).toContain('Erneut versuchen');
  });
});
