import { Component } from '@angular/core';

@Component({
  imports: [],
  selector: 'app-root',
  template: `
    <main>
      <h1>Küchenfuchs</h1>
      <p>Die lokale Entwicklungsumgebung ist bereit.</p>
      <a href="/api/health/ready">API- und Datenbankstatus prüfen</a>
    </main>
  `,
  styles: `
    :host {
      display: block;
      font-family: system-ui, sans-serif;
      margin: 3rem auto;
      max-width: 42rem;
      padding: 0 1.25rem;
    }
  `,
})
export class App {
}
