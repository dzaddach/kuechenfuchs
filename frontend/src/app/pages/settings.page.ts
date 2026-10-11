import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IconComponent } from '../shared/icon.component';

@Component({
  selector: 'app-settings-page',
  imports: [RouterLink, IconComponent],
  template: `
    <section class="settings-page">
      <p class="eyebrow">DEINE KÜCHE, DEINE REGELN</p>
      <h1>Einstellungen</h1>
      <p class="description">Verbindungen und Konto-Einstellungen für Küchenfuchs.</p>
      <a class="settings-entry" routerLink="/settings/chatgpt">
        <span class="entry-icon"><app-icon name="sparkles" /></span>
        <span class="entry-text">
          <strong>ChatGPT-Verbindung</strong>
          <small>Keycloak-Konto verknüpfen, Status prüfen und Verbindung trennen</small>
        </span>
        <app-icon name="chevron" />
      </a>
    </section>
  `,
  styleUrl: './settings.page.css',
})
export class SettingsPage {}
