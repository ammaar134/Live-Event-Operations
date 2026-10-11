import { afterEach, expect, it, vi } from 'vitest';
import { api } from './api';
afterEach(() => {
  vi.unstubAllGlobals();
  sessionStorage.clear();
});
it('preserves the key for an unknown outcome, then rotates after a completed action', async () => {
  const keys: string[] = [];
  let fail = true;
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init?: RequestInit) => {
      if (url.endsWith('/csrf'))
        return new Response(JSON.stringify({ token: 'test-token' }));
      keys.push((init?.headers as Record<string, string>)['Idempotency-Key']);
      if (fail) {
        fail = false;
        throw new Error('Response lost');
      }
      return new Response(JSON.stringify({ id: 'same-outcome' }));
    }),
  );
  await expect(api('/api/holds', { asset: 'a' }, 'recover')).rejects.toThrow(
    'Connection interrupted',
  );
  await api('/api/holds', { asset: 'a' }, 'recover');
  await api('/api/holds', { asset: 'a' }, 'recover');
  expect(keys[0]).toBe(keys[1]);
  expect(keys[2]).not.toBe(keys[1]);
});
it('coalesces rapid duplicate clicks while a mutation is pending', async () => {
  let calls = 0;
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string) => {
      if (url.endsWith('/csrf'))
        return new Response(JSON.stringify({ token: 'test-token' }));
      calls++;
      await new Promise((resolve) => setTimeout(resolve, 10));
      return new Response(JSON.stringify({ id: 'one' }));
    }),
  );
  expect(
    await Promise.all([
      api('/api/holds', { asset: 'b' }, 'click'),
      api('/api/holds', { asset: 'b' }, 'click'),
    ]),
  ).toEqual([{ id: 'one' }, { id: 'one' }]);
  expect(calls).toBe(1);
});

it('retains an unknown key when headers arrive but the body is lost', async () => {
  const keys: string[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (_url: string, init?: RequestInit) => {
      keys.push((init?.headers as Record<string, string>)['Idempotency-Key']);
      if (keys.length === 1)
        return {
          ok: true,
          status: 200,
          json: async () => {
            throw new Error('body truncated');
          },
        };
      return new Response(JSON.stringify({ id: 'committed' }));
    }),
  );
  await expect(
    api('/api/desk/products', { name: 'new' }, 'body-loss'),
  ).rejects.toThrow('receiving the result');
  await api('/api/desk/products', { name: 'new' }, 'body-loss');
  expect(keys[0]).toBe(keys[1]);
});

it.each([401, 403, 429])(
  'keeps an unknown key through status %s during recovery',
  async (status) => {
    const keys: string[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn(async (_url: string, init?: RequestInit) => {
        keys.push((init?.headers as Record<string, string>)['Idempotency-Key']);
        if (keys.length === 1) throw new Error('Response lost');
        if (keys.length === 2) return new Response('', { status });
        return new Response(JSON.stringify({ id: 'committed' }));
      }),
    );
    await expect(
      api('/api/desk/products', { name: 'new' }, 'status-loss'),
    ).rejects.toThrow();
    await expect(
      api('/api/desk/products', { name: 'new' }, 'status-loss'),
    ).rejects.toThrow();
    await api('/api/desk/products', { name: 'new' }, 'status-loss');
    expect(new Set(keys).size).toBe(1);
  },
);
