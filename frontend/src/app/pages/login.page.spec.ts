import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { describe, expect, it, vi } from 'vitest';
import { AuthService } from '../auth/auth.service';
import { LoginPage } from './login.page';

describe('LoginPage', () => {
  it('blocks an empty login form', async () => {
    const login = vi.fn().mockResolvedValue(true);
    await TestBed.configureTestingModule({
      imports: [LoginPage],
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            error: signal(''),
            ensureSession: vi.fn().mockResolvedValue(false),
            login,
          },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(LoginPage);
    fixture.detectChanges();

    await fixture.componentInstance.submit();

    expect(fixture.componentInstance.form.invalid).toBe(true);
    expect(login).not.toHaveBeenCalled();
  });

  it('does not claim that local HTTP traffic is encrypted', async () => {
    await TestBed.configureTestingModule({
      imports: [LoginPage],
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            error: signal(''),
            ensureSession: vi.fn().mockResolvedValue(false),
            login: vi.fn(),
          },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(LoginPage);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Eine verschlüsselte Übertragung besteht nur über HTTPS.');
    expect(fixture.nativeElement.textContent).not.toContain('sicher und verschlüsselt behandelt');
  });

  it('logs in and navigates to the app after a valid submission', async () => {
    const login = vi.fn().mockResolvedValue(true);
    await TestBed.configureTestingModule({
      imports: [LoginPage],
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            error: signal(''),
            ensureSession: vi.fn().mockResolvedValue(false),
            login,
          },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(LoginPage);
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    fixture.componentInstance.form.setValue({
      email: 'owner@example.test',
      password: 'valid-password',
    });

    await fixture.componentInstance.submit();

    expect(login).toHaveBeenCalledWith('owner@example.test', 'valid-password');
    expect(navigate).toHaveBeenCalledWith('/today');
  });
});
