import { test, expect } from '@playwright/test';

test('flujo completo carga UI principal', async ({ page }) => {
  await page.goto('/');
  await expect(page).toHaveTitle(/LocalMind|Vite|React/i);
});
