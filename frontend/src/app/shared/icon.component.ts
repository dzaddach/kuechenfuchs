import { Component, input } from '@angular/core';

export type IconName =
  | 'leaf' | 'calendar' | 'book' | 'checklist' | 'tag' | 'settings' | 'sparkles'
  | 'moon' | 'sun' | 'search' | 'plus' | 'heart' | 'clock' | 'users' | 'arrow'
  | 'filter' | 'close' | 'check' | 'chevron' | 'back' | 'pot' | 'shopping'
  | 'download' | 'info' | 'globe' | 'shield' | 'edit' | 'send';

@Component({
  selector: 'app-icon',
  template: `
    <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor"
         stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
      @switch (name()) {
        @case ('leaf') { <path d="M20 4c-8 0-14 3.5-14 10a6 6 0 0 0 6 6c6.5 0 8-7 8-16Z"/><path d="M4 21c2-5 6-8 11-11"/> }
        @case ('calendar') { <rect x="3" y="5" width="18" height="16" rx="3"/><path d="M16 3v4M8 3v4M3 10h18"/> }
        @case ('book') { <path d="M4 5.5A2.5 2.5 0 0 1 6.5 3H20v17H6.5A2.5 2.5 0 0 1 4 17.5z"/><path d="M4 17a2.5 2.5 0 0 1 2.5-2.5H20M8 7h7"/> }
        @case ('checklist') { <rect x="4" y="3" width="16" height="18" rx="3"/><path d="m7 9 1.5 1.5L11 8M13 9h4m-10 6 1.5 1.5L11 14m2 1h4"/> }
        @case ('tag') { <path d="M20.6 13.4 13 21l-10-10V3h8z"/><circle cx="7.5" cy="7.5" r="1"/> }
        @case ('settings') { <circle cx="12" cy="12" r="3"/><path d="m19.4 15 .1.1 1.4 1.1-1.4 2.4-1.7-.7a8 8 0 0 1-1.7 1l-.3 1.8h-2.8l-.3-1.8a8 8 0 0 1-1.7-1l-1.7.7-1.4-2.4 1.4-1.1A8 8 0 0 1 7 13l-1.8-.4v-2.8L7 9.4a8 8 0 0 1 1-1.7l-.7-1.7 2.4-1.4 1.1 1.4a8 8 0 0 1 2-.1l.4-1.8H16l.4 1.8a8 8 0 0 1 1.7 1l1.7-.7 1.4 2.4-1.4 1.1a8 8 0 0 1 .1 2l1.8.4v2.8z" transform="translate(-1 -1) scale(.95)"/> }
        @case ('sparkles') { <path d="m12 3 1.6 5.4L19 10l-5.4 1.6L12 17l-1.6-5.4L5 10l5.4-1.6zM19 16l.8 2.2L22 19l-2.2.8L19 22l-.8-2.2L16 19l2.2-.8z"/> }
        @case ('moon') { <path d="M20.5 15.5A8.5 8.5 0 0 1 8.5 3.5 8.5 8.5 0 1 0 20.5 15.5Z"/> }
        @case ('sun') { <circle cx="12" cy="12" r="4"/><path d="M12 2v2m0 16v2M4.9 4.9l1.4 1.4m11.4 11.4 1.4 1.4M2 12h2m16 0h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/> }
        @case ('search') { <circle cx="10.8" cy="10.8" r="6.8"/><path d="m16 16 4.5 4.5"/> }
        @case ('plus') { <path d="M12 5v14M5 12h14"/> }
        @case ('heart') { <path d="M20.8 8.8c0 5-8.8 10-8.8 10s-8.8-5-8.8-10A4.8 4.8 0 0 1 12 6.5a4.8 4.8 0 0 1 8.8 2.3Z"/> }
        @case ('clock') { <circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/> }
        @case ('users') { <path d="M16 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2m6-10a4 4 0 1 0 0-8 4 4 0 0 0 0 8Zm6-7.8a4 4 0 0 1 0 7.6M20 21v-2a4 4 0 0 0-3-3.9"/> }
        @case ('arrow') { <path d="M5 12h14m-6-6 6 6-6 6"/> }
        @case ('filter') { <path d="M4 7h16M7 12h10m-7 5h4"/><circle cx="8" cy="7" r="1.5" fill="currentColor"/><circle cx="15" cy="12" r="1.5" fill="currentColor"/> }
        @case ('close') { <path d="m18 6-12 12M6 6l12 12"/> }
        @case ('check') { <path d="m5 12 4 4L19 6"/> }
        @case ('chevron') { <path d="m9 18 6-6-6-6"/> }
        @case ('back') { <path d="m15 18-6-6 6-6M9 12h11"/> }
        @case ('pot') { <path d="M4 10h16v5a6 6 0 0 1-6 6h-4a6 6 0 0 1-6-6zM2 10h20M8 6V4h8v2m-4-2V2"/> }
        @case ('shopping') { <path d="M5 8h14l-1 13H6L5 8Zm4 0V6a3 3 0 0 1 6 0v2"/> }
        @case ('download') { <path d="M12 3v12m-5-5 5 5 5-5M4 20h16"/> }
        @case ('info') { <circle cx="12" cy="12" r="9"/><path d="M12 11v5m0-8h.01"/> }
        @case ('globe') { <circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3a14 14 0 0 1 0 18 14 14 0 0 1 0-18Z"/> }
        @case ('shield') { <path d="M12 22s8-4 8-11V5l-8-3-8 3v6c0 7 8 11 8 11Z"/><path d="m9 12 2 2 4-4"/> }
        @case ('edit') { <path d="m15 5 4 4M4 20l4-.8L19 8a2.8 2.8 0 0 0-4-4L4 15z"/> }
        @case ('send') { <path d="m22 2-7 20-4-9-9-4 20-7ZM22 2 11 13"/> }
      }
    </svg>
  `,
  styles: `:host{display:inline-flex;width:1.25rem;height:1.25rem;flex:none}svg{width:100%;height:100%}`,
})
export class IconComponent {
  readonly name = input.required<IconName>();
}
