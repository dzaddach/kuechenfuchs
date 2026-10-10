import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { IconComponent, IconName } from '../shared/icon.component';

interface PageContent {
  eyebrow: string;
  title: string;
  description: string;
  icon: IconName;
  phase: string;
  feature: string;
  link?: string;
  linkLabel?: string;
}

const PAGES: Record<string, PageContent> = {
  shopping: {
    eyebrow: 'GUT VORBEREITET',
    title: 'Einkaufsliste',
    description: 'Alles für deinen nächsten Einkauf – bald an einem einzigen, praktischen Ort.',
    icon: 'checklist',
    phase: 'P4',
    feature: 'Zutaten aus Rezepten übernehmen, Mengen abhaken und unterwegs auf dem iPhone dabei haben.',
    link: '/recipes',
    linkLabel: 'Rezepte ansehen',
  },
  offers: {
    eyebrow: 'CLEVER EINKAUFEN',
    title: 'Angebote',
    description: 'Finde bald passende Angebote für die Zutaten, die du wirklich brauchst.',
    icon: 'tag',
    phase: 'P7',
    feature: 'Zunächst mit klar gekennzeichneten Demo-Angeboten. Eine reale Quelle folgt nur nach positivem Machbarkeitsnachweis und separater Freigabe.',
  },
  settings: {
    eyebrow: 'DEINE KÜCHE, DEINE REGELN',
    title: 'Einstellungen',
    description: 'Passe Küchenfuchs an deine Gewohnheiten an.',
    icon: 'settings',
    phase: 'P2',
    feature: 'Zugangsschutz und sichere Kontoeinstellungen werden vor dem persönlichen Rezeptimport eingerichtet.',
  },
};

@Component({
  selector: 'app-placeholder-page',
  imports: [RouterLink, IconComponent],
  template: `
    <section class="placeholder-page">
      <p class="eyebrow">{{ content().eyebrow }}</p>
      <span class="page-icon"><app-icon [name]="content().icon" /></span>
      <h1>{{ content().title }}</h1>
      <p class="description">{{ content().description }}</p>
      <div class="coming-card">
        <span class="phase-label">{{ content().phase }} · GEPLANT</span>
        <h2>Das kommt in deine Küche</h2>
        <p>{{ content().feature }}</p>
        @if (content().link) {
          <a class="text-link" [routerLink]="content().link">{{ content().linkLabel }} <app-icon name="arrow" /></a>
        }
      </div>
      <p class="not-ready"><span class="demo-dot"></span> Diese Funktion ist noch nicht aktiv. Es werden keine Daten vorgetäuscht.</p>
    </section>
  `,
  styleUrl: './placeholder.page.css',
})
export class PlaceholderPage {
  private readonly route = inject(ActivatedRoute);
  private readonly routeData = toSignal(this.route.data, { initialValue: this.route.snapshot.data });
  readonly content = computed(() => PAGES[this.routeData()['page'] as string] ?? PAGES['shopping']);
}
