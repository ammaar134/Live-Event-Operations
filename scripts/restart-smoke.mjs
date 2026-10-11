import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';

// Uses only public endpoints. Process control stays in the isolated test harness.
export async function restartSmoke(request, baseURL, restart) {
  const client = await request.newContext({ baseURL });
  async function json(response) {
    assert.equal(response.status(), 200, `Restart probe HTTP ${response.status()}`);
    return response.json();
  }
  try {
    let token = (await json(await client.get('/api/session/csrf'))).token;
    await json(await client.post('/api/session/register', {
      headers: { 'X-CSRF-TOKEN': token },
      data: { name: 'Restart Probe', email: `restart-${randomUUID()}@example.test`, password: `Fr!${randomUUID()}a` },
    }));
    token = (await json(await client.get('/api/session/csrf'))).token;
    const product = await json(await client.get('/api/products/10000000-0000-0000-0000-000000000015'));
    const day = new Intl.DateTimeFormat('en-CA', { timeZone: 'Pacific/Auckland', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date(Date.now() + 45 * 86400000));
    const headers = { 'X-CSRF-TOKEN': token, 'Idempotency-Key': randomUUID() };
    const hold = await json(await client.post('/api/holds', { headers, data: { assetId: product.assets[0].id, start: `${day}T10:00`, end: `${day}T12:00` } }));
    const confirmation = { headers: { ...headers, 'Idempotency-Key': randomUUID() }, data: { reason: 'Accept interval and policy' } };
    const before = await json(await client.post(`/api/bookings/${hold.id}/confirm`, confirmation));
    assert.equal(before.state, 'Confirmed');
    await restart();
    const session = await json(await client.get('/api/session'));
    assert.equal(session.authenticated, true, 'Session cookie must survive API restart');
    const replay = await json(await client.post(`/api/bookings/${hold.id}/confirm`, confirmation));
    assert.deepEqual(replay, before, 'Persisted idempotency outcome must survive API restart');
    const booking = await json(await client.get(`/api/bookings/${hold.id}`));
    assert.equal(booking.reference, before.reference);
    assert.equal(booking.history.filter(x => x.action === 'confirm').length, 1);
    await json(await client.post(`/api/bookings/${hold.id}/cancel`, { headers: { ...headers, 'Idempotency-Key': randomUUID() }, data: { reason: 'Restart probe complete' } }));
    console.log('API restart probe passed: session, confirmed booking, same-key recovery and one audit event.');
  } finally {
    await client.dispose();
  }
}
