export type Policy = { id: number; timeZone: string; openHour: number; closeHour: number; leadMinutes: number; maximumHours: number; horizonDays: number; bufferMinutes: number; cancellationMinutes: number; holdSeconds: number; holdLimit: number; version: number };
export type Session = { authenticated: boolean; id?: string; displayName?: string; email?: string; manager?: boolean };
export type Asset = { id: string; tag: string; condition: string; state: string; location: string; readyAt?: string; available?: boolean; atRisk?: boolean };
export type Product = { id: string; name: string; category: string; description: string; image: string; specifications: string; accessories: string; assets: Asset[]; schedule?: { assetId: string; start: string; end: string; blockedUntil: string; kind: string }[]; policy: Policy };
export type Booking = { id: string; assetId: string; reference?: string; start: string; end: string; state: string; expiresAt?: string; serverTime: string; asset: string; product: string; image: string; location: string; accessories: string; policy: Policy; atRisk: boolean; history: { action: string; reason: string; at: string }[]; custody?: { pickedUpAt: string; returnedAt?: string; condition?: string; accessoryCheck?: string } };
export class ApiError extends Error { constructor(public status: number, public code: string, message: string) { super(message); } }
let token = '';
const pending = new Map<string, Promise<unknown>>();
export async function csrf() { token = (await fetch('/api/session/csrf').then(r => r.json())).token; }
export async function api<T>(path: string, body?: unknown, scope?: string): Promise<T> {
  const operation = `${path}:${JSON.stringify(body)}`;
  if (scope && pending.has(operation)) return pending.get(operation) as Promise<T>;
  const promise = request<T>(path, body, scope);
  if (scope) pending.set(operation, promise);
  try { return await promise; } finally { pending.delete(operation); }
}
async function request<T>(path: string, body?: unknown, scope?: string): Promise<T> {
  const headers: Record<string, string> = {};
  if (body !== undefined) {
    if (!token) await csrf();
    headers['Content-Type'] = 'application/json'; headers['X-CSRF-TOKEN'] = token;
    if (scope) {
      const content = JSON.stringify(body); const slot = `fieldroom.retry.${scope}`;
      const old = JSON.parse(sessionStorage.getItem(slot) || 'null');
      const record = old?.content === content && !old.completed ? old : { content, key: crypto.randomUUID(), completed: false };
      sessionStorage.setItem(slot, JSON.stringify(record)); headers['Idempotency-Key'] = record.key;
    }
  }
  let response: Response;
  try { response = await fetch(path, { method: body === undefined ? 'GET' : 'POST', headers, body: body === undefined ? undefined : JSON.stringify(body) }); }
  catch { throw new ApiError(0, 'network', 'Connection interrupted. Retry this action; its request key is preserved.'); }
  // A received success/business response ends this user attempt. Network/5xx
  // responses leave the key available for recovery after an unknown commit.
  if (scope && response.status < 500) { const slot = `fieldroom.retry.${scope}`; const record = JSON.parse(sessionStorage.getItem(slot) || 'null'); if (record) sessionStorage.setItem(slot, JSON.stringify({ ...record, completed: true })); }
  if (!response.ok) { const e = await response.json().catch(() => ({})); if (e.code === 'csrf') await csrf(); throw new ApiError(response.status, e.code || 'request', e.message || (response.status === 401 ? 'Your session has expired. Sign in again.' : response.status === 403 ? 'This area is for lending-desk managers.' : 'Unable to load this page. Please retry.')); }
  return response.json();
}
export function dates(instant: string, options?: Intl.DateTimeFormatOptions) { return new Intl.DateTimeFormat('en-NZ', { timeZone: 'Pacific/Auckland', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit', ...options }).format(new Date(instant)); }
export function localDate(instant: Date) { const parts = new Intl.DateTimeFormat('en-CA', { timeZone: 'Pacific/Auckland', year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(instant); const p = (type: string) => parts.find(x => x.type === type)?.value; return `${p('year')}-${p('month')}-${p('day')}`; }
export function defaultTimes() { return { start: `${localDate(new Date(Date.now() + 86400000))}T10:00`, end: `${localDate(new Date(Date.now() + 172800000))}T16:00` }; }
