import { inject } from '@angular/core';
import { CanActivateChildFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

export const requireSession: CanActivateChildFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return await auth.ensureSession() ? true : router.createUrlTree(['/login']);
};
