import { Component, inject, signal } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { IconComponent } from '../shared/icon.component';
import { AuthService } from '../auth/auth.service';

@Component({
  selector: 'app-login-page',
  imports: [ReactiveFormsModule, IconComponent],
  template: `
    <main class="login-page">
      <section class="login-card" aria-labelledby="login-title">
        <a class="brand" href="/" aria-label="Küchenfuchs Startseite">
          <span class="brand-mark"><app-icon name="leaf" /></span>
          <span>Küchenfuchs</span>
        </a>
        <p class="eyebrow">SCHÖN, DASS DU DA BIST</p>
        <h1 id="login-title">Willkommen zurück</h1>
        <p class="intro">Melde dich an und wir sind gleich in deiner Küche.</p>

        <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <div class="field">
            <label for="email">E-Mail-Adresse</label>
            <input id="email" type="email" formControlName="email" autocomplete="username"
                   inputmode="email" placeholder="name&#64;beispiel.de" required>
            @if (submitted() && form.controls.email.invalid) {
              <span class="field-error">Gib bitte eine gültige E-Mail-Adresse ein.</span>
            }
          </div>
          <div class="field">
            <label for="password">Passwort</label>
            <input id="password" type="password" formControlName="password" autocomplete="current-password"
                   placeholder="Dein Passwort" required>
            @if (submitted() && form.controls.password.invalid) {
              <span class="field-error">Bitte gib dein Passwort ein.</span>
            }
          </div>
          @if (auth.error()) {
            <p class="error-message" role="alert"><app-icon name="info" /> {{ auth.error() }}</p>
          }
          <button class="submit-button" type="submit" [disabled]="busy()">
            @if (busy()) { <span class="spinner" aria-hidden="true"></span> Anmeldung läuft … }
            @else { Anmelden <app-icon name="arrow" /> }
          </button>
        </form>
        <p class="privacy-note"><app-icon name="shield" /> Die Anmeldung verwendet HttpOnly-Sitzungscookies. Eine verschlüsselte Übertragung besteht nur über HTTPS.</p>
      </section>
      <p class="login-footer">Küchenfuchs · Clever kochen. Günstiger einkaufen.</p>
    </main>
  `,
  styleUrl: './login.page.css',
})
export class LoginPage {
  private readonly fb = inject(FormBuilder).nonNullable;
  private readonly router = inject(Router);
  readonly auth = inject(AuthService);
  readonly busy = signal(false);
  readonly submitted = signal(false);
  readonly form = this.fb.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(254)]],
    password: ['', [Validators.required, Validators.maxLength(256)]],
  });

  constructor() {
    void this.auth.ensureSession().then((authenticated) => {
      if (authenticated) void this.router.navigateByUrl('/today');
    });
  }

  async submit(): Promise<void> {
    this.submitted.set(true);
    this.auth.error.set('');
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    const { email, password } = this.form.getRawValue();
    try {
      if (await this.auth.login(email, password)) {
        await this.router.navigateByUrl('/today');
      }
    } finally {
      this.busy.set(false);
    }
  }
}
