import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IconComponent } from '../shared/icon.component';

@Component({
  selector: 'app-today-page',
  imports: [RouterLink, IconComponent],
  template: `
    <section class="welcome">
      <div>
        <p class="eyebrow">{{ today }}</p>
        <h1>Was kommt heute<br class="mobile-break"> auf den Tisch?</h1>
        <p class="subheading">Ein guter Tag für etwas Leckeres.</p>
      </div>
      <div class="welcome-art" aria-hidden="true">
        <span class="sun"></span><span class="bowl"></span>
        <span class="herb herb-one"></span><span class="herb herb-two"></span>
      </div>
    </section>

    <section class="section-block">
      <div class="section-heading">
        <div><p class="eyebrow">HEUTE IN DEINER KÜCHE</p><h2>Dein Küchenmoment</h2></div>
        <span class="date-chip"><app-icon name="calendar" /> Heute</span>
      </div>
      <article class="plan-card">
        <div class="plan-illustration" aria-hidden="true">
          <div class="illustration-plate"><span class="pasta"></span><span class="basil"></span><span class="tomato"></span></div>
        </div>
        <div class="plan-copy">
          <span class="plan-label"><span class="plan-dot"></span> HEUTE ABEND</span>
          <h3>Noch nichts geplant</h3>
          <p>Such dir etwas aus deiner Sammlung aus – der Wochenplan ist bald für dich da.</p>
          <a class="text-link" routerLink="/recipes">Rezeptideen entdecken <app-icon name="arrow" /></a>
        </div>
        <span class="planned-badge"><app-icon name="sparkles" /> Platz für Genuss</span>
      </article>
    </section>

    <section class="section-block inspiration">
      <div class="section-heading">
        <div><p class="eyebrow">KLEINE INSPIRATION</p><h2>Lust auf etwas Neues?</h2></div>
        <a class="subtle-link" routerLink="/recipes">Alle Rezepte <app-icon name="arrow" /></a>
      </div>
      <div class="inspiration-grid">
        <a class="inspiration-card inspiration-green" routerLink="/recipes">
          <span class="inspiration-icon"><app-icon name="book" /></span>
          <strong>Deine Rezeptsammlung</strong>
          <span>4 liebevoll ausgewählte Demo-Rezepte</span>
          <app-icon class="card-arrow" name="arrow" />
        </a>
        <a class="inspiration-card inspiration-peach" routerLink="/shopping">
          <span class="inspiration-icon"><app-icon name="shopping" /></span>
          <strong>Gut vorbereitet</strong>
          <span>Deine Einkaufsliste wächst bald mit</span>
          <app-icon class="card-arrow" name="arrow" />
        </a>
      </div>
    </section>
    <p class="demo-disclaimer"><span class="demo-dot"></span> Du siehst eine Vorschau. Wochenplan und Einkaufsliste werden in späteren Versionen verfügbar.</p>
  `,
  styleUrl: './today.page.css',
})
export class TodayPage {
  readonly today = new Intl.DateTimeFormat('de-DE', {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
  }).format(new Date()).toLocaleUpperCase('de-DE');
}
