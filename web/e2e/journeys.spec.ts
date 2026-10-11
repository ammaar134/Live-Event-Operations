import { test, expect } from '@playwright/test';
import type { Page, APIRequestContext } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const password = `Journey!${crypto.randomUUID()}a`;
async function register(page: Page) {
  const email = `member-${crypto.randomUUID()}@example.test`;
  await page.goto('/account'); await page.getByRole('button', { name: 'New here? Join the library' }).click();
  await page.getByLabel('Your name').fill('Alex Field'); await page.getByLabel('Email', { exact: true }).fill(email); await page.getByLabel('Password', { exact: true }).fill(password); await page.getByRole('button', { name: 'Create my account' }).click(); await expect(page).toHaveURL(/\/bookings$/); return email;
}
async function csrf(request: APIRequestContext) { return (await (await request.get('/api/session/csrf')).json()).token; }
function future(day: number) { const date = new Date(Date.now() + day * 86400000); const parts = new Intl.DateTimeFormat('en-CA', { timeZone: 'Pacific/Auckland', year: 'numeric', month: '2-digit', day: '2-digit' }).format(date); return { start: `${parts}T10:00`, end: `${parts}T16:00` }; }

test('member books, refreshes, navigates back, reschedules and cancels', async ({ page }) => {
  await register(page); await page.goto('/'); await page.getByRole('link', { name: /The everyday mirrorless/ }).last().click();
  const times = future(15); await page.getByLabel('Pick up', { exact: true }).fill(times.start); await page.getByLabel('Return', { exact: true }).fill(times.end);
  await page.getByRole('button', { name: 'Hold this equipment' }).click(); await expect(page.getByRole('heading', { name: 'One last look.' })).toBeVisible();
  await page.reload(); await expect(page.getByRole('timer')).toBeVisible();
  await page.getByLabel('I accept this interval, this copy and the lending policy.').check(); await page.getByRole('button', { name: 'Confirm my booking' }).click();
  await expect(page.getByRole('heading', { name: 'It’s on the calendar.' })).toBeVisible(); const bookingUrl = page.url();
  await page.goto('/bookings'); await page.goBack(); await expect(page).toHaveURL(bookingUrl); await expect(page.getByText('Confirmed', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Change dates' }).click(); const replacement = future(16); await page.getByLabel('Pick up', { exact: true }).fill(replacement.start); await page.getByLabel('Return', { exact: true }).fill(replacement.end); await page.getByLabel('Reason / desk note').fill('Project moved one day'); await page.getByRole('button', { name: 'Confirm reschedule' }).click(); await expect(page.getByText('Project moved one day')).toBeVisible();
  await page.getByRole('button', { name: 'Cancel booking', exact: true }).click(); await page.getByLabel('Reason / desk note').fill('Project completed early'); await page.getByRole('button', { name: 'Confirm cancel' }).click(); await expect(page.getByText('Cancelled', { exact: true })).toBeVisible(); await page.reload(); await expect(page.getByText('Cancelled', { exact: true })).toBeVisible();
});

test('two browser sessions race for one physical copy', async ({ browser }) => {
  const a = await browser.newContext(); const b = await browser.newContext(); const p = await a.newPage(); const q = await b.newPage(); await register(p); await register(q);
  const catalog = await (await a.request.get('/api/catalog')).json(); const asset = catalog.products.find((x: { name: string }) => x.name === 'Carbon travel tripod').assets[0].id;
  const body = { assetId: asset, ...future(20) }; const ta = await csrf(a.request); const tb = await csrf(b.request);
  const results = await Promise.all([a.request.post('/api/holds', { data: body, headers: { 'X-CSRF-TOKEN': ta, 'Idempotency-Key': crypto.randomUUID() } }), b.request.post('/api/holds', { data: body, headers: { 'X-CSRF-TOKEN': tb, 'Idempotency-Key': crypto.randomUUID() } })]);
  expect(results.map(r => r.status()).sort()).toEqual([200, 409]);
  const winner = results[0].ok() ? 0 : 1; const hold = await results[winner].json(); await (winner ? b : a).request.post(`/api/bookings/${hold.id}/release`, { data: { reason: 'Browser race test complete' }, headers: { 'X-CSRF-TOKEN': winner ? tb : ta, 'Idempotency-Key': crypto.randomUUID() } }); await a.close(); await b.close();
});

test('responsive screenshots, keyboard access and automated accessibility', async ({ page }) => {
  await page.goto('/'); await expect(page.getByRole('heading', { name: 'Your next idea. Already equipped.' })).toBeVisible(); await page.keyboard.press('Tab'); await expect(page.getByRole('link', { name: 'Skip to content' })).toBeFocused();
  await page.setViewportSize({ width: 1440, height: 1050 }); await page.screenshot({ path: '../docs/screenshots/catalog-desktop.png', fullPage: true });
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
  await page.setViewportSize({ width: 390, height: 844 }); await page.screenshot({ path: '../docs/screenshots/catalog-mobile.png', fullPage: true }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.goto('/products/10000000-0000-0000-0000-000000000001'); await expect(page.getByRole('heading', { name: 'The everyday mirrorless' })).toBeVisible(); await page.screenshot({ path: '../docs/screenshots/product-mobile.png', fullPage: true }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true); expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
});
