import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { Client } from 'pg';

test('manager picks up, receives damaged equipment, inspects and edits inventory', async ({
  page,
}) => {
  const connectionString = process.env.E2E_DATABASE_URL;
  if (
    !connectionString ||
    !new URL(connectionString).pathname.startsWith('/fieldroom_e2e_')
  )
    throw new Error(
      'Run through scripts/browser-tests.mjs; isolated fixture database is required.',
    );
  const db = new Client({ connectionString });
  await db.connect();
  try {
    // Test setup uses direct SQL only on this isolated database. There is no test endpoint.
    const member = (
      await db.query('SELECT "Id" FROM "AspNetUsers" ORDER BY "Id" LIMIT 1')
    ).rows[0].Id;
    const asset = (
      await db.query('SELECT "Id" FROM "Assets" WHERE "Tag"=$1', ['FR-005-01'])
    ).rows[0].Id;
    const policy = (
      await db.query('SELECT row_to_json(p) AS policy FROM "Policies" p')
    ).rows[0].policy;
    const id = crypto.randomUUID();
    await db.query(
      `WITH t AS MATERIALIZED (SELECT clock_timestamp() AS n) INSERT INTO "Allocations" ("Id","AssetId","MemberId","State","Reference","Start","End","Occupied","PolicyJson","CreatedAt") SELECT $1,$2,$3,'Confirmed','FR-DESK-TEST',n-interval '10 minutes',n+interval '1 hour',tstzrange(n-interval '10 minutes',n+interval '2 hours','[)'),$4,n FROM t`,
      [id, asset, member, JSON.stringify(policy)],
    );
    await page.goto('/account');
    await page
      .getByLabel('Email', { exact: true })
      .fill('desk@fieldroom.example');
    await page
      .getByLabel('Password', { exact: true })
      .fill(process.env.E2E_DESK_PASSWORD!);
    await page.getByRole('button', { name: 'Sign in', exact: false }).click();
    await expect(page).toHaveURL('/bookings');
    await page.goto(`/bookings/${id}`);
    await page.getByRole('button', { name: 'Record pickup' }).click();
    await page
      .getByLabel('Reason / desk note')
      .fill('ID and asset label checked');
    await page.getByRole('button', { name: 'Confirm pickup' }).click();
    await expect(page.getByText('CheckedOut', { exact: true })).toBeVisible();
    await page.reload();
    await page.getByRole('button', { name: 'Record return' }).click();
    await page.getByLabel('Return condition').selectOption('Damaged');
    await page
      .getByLabel('Accessory check')
      .fill('Microphone, shock mount, windscreen, cables and case all present');
    await page
      .getByLabel('Reason / desk note')
      .fill('Housing cracked; inspection required');
    await page.getByRole('button', { name: 'Confirm return' }).click();
    await expect(page.getByText('Returned', { exact: true })).toBeVisible();
    await page.goto('/desk');
    await page.getByRole('tab', { name: 'Inventory' }).click();
    const row = page.locator('.asset-entry').filter({ hasText: 'FR-005-01' });
    await expect(row.getByText('Inspection', { exact: true })).toBeVisible();
    await row.getByRole('button', { name: 'Inspect & ready' }).click();
    await row
      .getByLabel('Reason / inspection record')
      .fill('Housing repaired and function checked');
    await row.getByRole('button', { name: 'Confirm update' }).click();
    await expect(row.getByText('Ready', { exact: true })).toBeVisible();
    const product = page
      .locator('.inventory-product')
      .filter({ hasText: 'Directional microphone' });
    await product.getByRole('button', { name: 'Edit product' }).click();
    await product
      .getByRole('textbox', { name: 'Description', exact: true })
      .fill(
        'A carefully inspected directional microphone for clear conversations and field recordings.',
      );
    await product
      .getByLabel('Reason', { exact: true })
      .fill('Clarify equipment description');
    await product.getByRole('button', { name: 'Save product' }).click();
    await expect(
      product.getByRole('button', { name: 'Save product' }),
    ).toHaveCount(0);
    await page.getByRole('tab', { name: 'Loans' }).click();
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.screenshot({
      path: '../docs/screenshots/lending-desk.png',
      fullPage: true,
    });
    expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
    const record = (
      await db.query('SELECT "State" FROM "Allocations" WHERE "Id"=$1', [id])
    ).rows[0];
    expect(record.State).toBe('Returned');
    expect(
      (
        await db.query(
          'SELECT count(*)::int AS n FROM "Checkouts" WHERE "AllocationId"=$1 AND "ReturnedAt" IS NOT NULL',
          [id],
        )
      ).rows[0].n,
    ).toBe(1);
  } finally {
    await db.end();
  }
});
