import React, { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { api, csrf, dates, defaultTimes, safeNext, ApiError } from './api';
import type { Session, Product, Booking, Policy } from './api';
import './style.css';

function useLoad<T>(path: string) {
  const [data, setData] = useState<T>();
  const [error, setError] = useState('');
  const [version, setVersion] = useState(0);
  useEffect(() => {
    let active = true;
    setData(undefined);
    setError('');
    api<T>(path)
      .then((d) => {
        if (active) setData(d);
      })
      .catch((e) => {
        if (active) setError(e.message);
      });
    return () => {
      active = false;
    };
  }, [path, version]);
  return { data, error, refresh: () => setVersion((v) => v + 1) };
}
function ErrorBox({ message }: { message: string }) {
  return message ? (
    <div className="notice error" role="alert">
      {message.includes('session') ? (
        <>
          {message}{' '}
          <a href={`/account?next=${encodeURIComponent(location.pathname)}`}>
            Sign in
          </a>
        </>
      ) : (
        message
      )}
    </div>
  ) : null;
}
function Loading({ error, retry }: { error: string; retry: () => void }) {
  return error ? (
    <div className="page">
      <ErrorBox message={error} />
      <button onClick={retry}>Try again</button>
    </div>
  ) : (
    <div className="page loading" role="status">
      Opening the library…
    </div>
  );
}
function Label({ children }: { children: React.ReactNode }) {
  return <span className="eyebrow">{children}</span>;
}
function Image({
  name,
  alt,
  eager = false,
}: {
  name: string;
  alt: string;
  eager?: boolean;
}) {
  return (
    <img
      className="gear-image"
      src={`/images/${name}.svg`}
      alt={alt}
      loading={eager ? 'eager' : 'lazy'}
      width="800"
      height="600"
    />
  );
}
function TimeFields({
  value,
  change,
}: {
  value: { start: string; end: string };
  change: (v: { start: string; end: string }) => void;
}) {
  return (
    <div className="time-fields">
      <label>
        Pick up
        <input
          type="datetime-local"
          required
          value={value.start}
          onChange={(e) => change({ ...value, start: e.target.value })}
        />
      </label>
      <span aria-hidden="true">→</span>
      <label>
        Return
        <input
          type="datetime-local"
          required
          value={value.end}
          onChange={(e) => change({ ...value, end: e.target.value })}
        />
      </label>
    </div>
  );
}
function PolicyNote({ p }: { p: Policy }) {
  return (
    <div className="policy-note">
      <strong>{p.timeZone}</strong>
      <p>
        Open daily, {p.openHour}:00–{p.closeHour}:00. {p.leadMinutes} minutes’
        notice · up to {p.maximumHours / 24} days · {p.horizonDays}-day booking
        window.
      </p>
      <p>
        Changes and cancellations close {p.cancellationMinutes / 60} hours
        before pickup. Allow {p.bufferMinutes} minutes for turnaround. Bring
        photo ID and return every accessory.
      </p>
    </div>
  );
}

function App() {
  const session = useLoad<Session>('/api/session');
  const path = location.pathname;
  const route = path.split('/').filter(Boolean);
  return (
    <>
      <a href="#main" className="skip">
        Skip to content
      </a>
      <header className="header">
        <a className="brand" href="/" aria-label="Fieldroom home">
          <span className="brand-mark" aria-hidden="true">
            <svg viewBox="0 0 32 32" fill="none">
              <path d="M6 11h20l-5-5M26 21H6l5 5" />
            </svg>
          </span>
          fieldroom
          <span className="brand-caption">An equipment library</span>
        </a>
        <nav aria-label="Main navigation">
          <a href="/" aria-current={path === '/' ? 'page' : undefined}>
            The collection
          </a>
          <a
            href="/bookings"
            aria-current={route[0] === 'bookings' ? 'page' : undefined}
          >
            My loans
          </a>
          {session.data?.manager && <a href="/desk">Lending desk</a>}
          <a className="account-link" href="/account">
            {session.data?.authenticated
              ? session.data.displayName?.split(' ')[0]
              : 'Sign in'}{' '}
            <span aria-hidden="true">↗</span>
          </a>
        </nav>
      </header>
      <main id="main">
        {path === '/' ? (
          <Catalog />
        ) : route[0] === 'products' && route[1] ? (
          <Detail id={route[1]} session={session.data} />
        ) : route[0] === 'account' ? (
          <Account session={session.data} refresh={session.refresh} />
        ) : route[0] === 'bookings' && route[1] ? (
          <BookingPage id={route[1]} manager={!!session.data?.manager} />
        ) : path === '/bookings' ? (
          <MyLoans />
        ) : route[0] === 'desk' ? (
          <Desk session={session.data} />
        ) : (
          <div className="page">
            <h1>That shelf is empty.</h1>
            <p>We couldn’t find this page.</p>
            <a href="/">Return to the collection →</a>
          </div>
        )}
      </main>
      <footer>
        <a className="brand" href="/">
          fieldroom.
        </a>
        <p>Good things deserve to be used.</p>
        <span>
          Fictional community library · Local portfolio demo
          <br />
          Pacific/Auckland · No payments
        </span>
      </footer>
    </>
  );
}

function Catalog() {
  const [search, setSearch] = useState('');
  const [category, setCategory] = useState('');
  const [sort, setSort] = useState('name');
  const [times, setTimes] = useState(defaultTimes);
  const [filterTimes, setFilterTimes] = useState(false);
  const [submitted, setSubmitted] = useState('');
  const params = new URLSearchParams({
    q: submitted,
    category,
    sort,
    ...(filterTimes ? times : {}),
  });
  const load = useLoad<{ products: Product[]; total: number; policy: Policy }>(
    `/api/catalog?${params}`,
  );
  return (
    <div className="page catalog">
      <section className="hero">
        <div className="hero-heading">
          <Label>Less to own. More to do.</Label>
          <h1>
            Good gear.
            <br />
            Yours for a while.
          </h1>
        </div>
        <div className="hero-intro">
          <p>
            A weekend outside. A project at home. Borrow the equipment for
            whatever you have in mind.
          </p>
          <a className="text-link" href="#collection">
            Explore the collection <span aria-hidden="true">↓</span>
          </a>
        </div>
        <div className="equipment-shelf" aria-label="Browse by activity">
          {[
            {
              category: 'Cameras',
              image: 'camera',
              note: 'See things differently',
              number: '01',
            },
            {
              category: 'Tools',
              image: 'drill',
              note: 'Make a little progress',
              number: '02',
            },
            {
              category: 'Outdoors',
              image: 'tent',
              note: 'Take the long way home',
              number: '03',
            },
          ].map((item) => (
            <a
              key={item.category}
              className={`shelf-item shelf-${item.image}`}
              aria-label={`Browse ${item.category.toLowerCase()}`}
              href="#collection"
              onClick={() => setCategory(item.category)}
            >
              <span className="shelf-top">
                <span>{item.category}</span>
                <span className="mono">{item.number}</span>
              </span>
              <Image
                name={item.image}
                alt={`Original ${item.image} illustration`}
                eager
              />
              <span className="shelf-bottom">
                {item.note}
                <span aria-hidden="true">↗</span>
              </span>
            </a>
          ))}
        </div>
        <div className="library-note">
          <span>
            <span className="small-mark" aria-hidden="true">
              ↔
            </span>{' '}
            Borrow. Use. Bring back.
          </span>
          <span>One shared collection, plenty of possibilities.</span>
        </div>
      </section>
      <section id="collection" className="collection">
        <div className="section-heading">
          <div>
            <h2>
              The equipment room<span className="heading-dot">.</span>
            </h2>
          </div>
          <p>Find what you need. Leave the rest with us.</p>
        </div>
        <div className="catalog-browser">
          <aside className="catalog-sidebar" aria-label="Browse equipment">
            <Label>Browse the shelves</Label>
            <div className="categories" aria-label="Equipment categories">
              {['', 'Cameras', 'Tools', 'Outdoors'].map((c) => (
                <button
                  key={c}
                  className={category === c ? 'selected' : 'quiet'}
                  aria-pressed={category === c}
                  onClick={() => setCategory(c)}
                >
                  {c || 'Everything'}
                  <span aria-hidden="true">↗</span>
                </button>
              ))}
            </div>
            <div className="shelf-note">
              <span className="small-mark" aria-hidden="true">
                ↔
              </span>
              <p>Choose your dates to see which copies are available.</p>
              <span>
                Inspected between loans.
                <br />
                Ready for the next person.
              </span>
            </div>
          </aside>
          <div className="catalog-results">
            <form
              className="search-row"
              onSubmit={(e) => {
                e.preventDefault();
                setSubmitted(search);
              }}
            >
              <label className="search-label">
                <span className="sr-only">Search equipment</span>
                <input
                  placeholder="Search the equipment room"
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                />
              </label>
              <button type="submit">
                Search <span aria-hidden="true">↗</span>
              </button>
              <label>
                Sort by
                <select value={sort} onChange={(e) => setSort(e.target.value)}>
                  <option value="name">Name A–Z</option>
                  <option value="category">Category</option>
                </select>
              </label>
            </form>
            <div className="filter-row">
              <span className="filter-hint">
                {category || 'The whole collection'}
              </span>
              <label className="check">
                <input
                  type="checkbox"
                  checked={filterTimes}
                  onChange={(e) => setFilterTimes(e.target.checked)}
                />{' '}
                Check my dates
              </label>
            </div>
            {filterTimes && (
              <div className="date-filter">
                <TimeFields value={times} change={setTimes} />
                <p className="muted">
                  All times Pacific/Auckland. Availability is checked again when
                  you place a hold.
                </p>
              </div>
            )}
            {!load.data ? (
              <Loading error={load.error} retry={load.refresh} />
            ) : (
              <>
                <div className="results-label">
                  <span>
                    {load.data.total} {load.data.total === 1 ? 'item' : 'items'}{' '}
                    to borrow
                  </span>
                  <span>
                    {filterTimes
                      ? 'Showing availability for your dates'
                      : 'Select an item to find your copy'}
                  </span>
                </div>
                {load.data.products.length === 0 ? (
                  <div className="empty">
                    <h3>Nothing on this shelf yet.</h3>
                    <p>Try a broader search or a different category.</p>
                  </div>
                ) : (
                  <div className="product-grid">
                    {load.data.products.map((p) => {
                      const available = p.assets.filter(
                        (a) => a.available,
                      ).length;
                      return (
                        <a
                          key={p.id}
                          className="product-card"
                          href={`/products/${p.id}`}
                        >
                          <div className="product-art">
                            <span className="inventory-number">
                              {p.category}
                            </span>
                            <Image
                              name={p.image}
                              alt={`Original illustration of ${p.name.toLowerCase()}`}
                            />
                            <span className="round-arrow" aria-hidden="true">
                              ↗
                            </span>
                          </div>
                          <h3>{p.name}</h3>
                          <div className="product-meta">
                            <span
                              className={`availability ${available ? '' : 'unavailable'}`}
                            >
                              {available
                                ? `${available} ${filterTimes ? 'available for your dates' : `${available === 1 ? 'copy' : 'copies'} · choose dates`}`
                                : 'Currently unavailable'}
                            </span>
                          </div>
                          <p>{p.description.split('.')[0]}.</p>
                        </a>
                      );
                    })}
                  </div>
                )}
              </>
            )}
          </div>
        </div>
      </section>
      <section className="closing" aria-labelledby="how-it-works">
        <div>
          <Label>A little give and take</Label>
          <h2 id="how-it-works">
            Borrowing is
            <br />
            pretty simple.
          </h2>
        </div>
        <ol className="borrow-steps">
          <li>
            <span className="mono">01</span>
            <div>
              <h3>Find your thing</h3>
              <p>Choose equipment and a specific copy for your dates.</p>
            </div>
          </li>
          <li>
            <span className="mono">02</span>
            <div>
              <h3>Make it yours for a while</h3>
              <p>Place a short hold, review your booking, then confirm.</p>
            </div>
          </li>
          <li>
            <span className="mono">03</span>
            <div>
              <h3>Bring it back, pass it on</h3>
              <p>
                Collect it from the library and return it with every accessory.
              </p>
            </div>
          </li>
        </ol>
      </section>
    </div>
  );
}

function Detail({ id, session }: { id: string; session?: Session }) {
  const load = useLoad<Product>(`/api/products/${id}`);
  const [times, setTimes] = useState(defaultTimes);
  const [assetId, setAssetId] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  if (!load.data) return <Loading error={load.error} retry={load.refresh} />;
  const p = load.data;
  const selected =
    assetId ||
    p.assets.find((a) => a.state === 'Ready' && !a.atRisk)?.id ||
    p.assets[0]?.id;
  async function hold(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError('');
    try {
      const h = await api<Booking>(
        '/api/holds',
        { assetId: selected, ...times },
        `hold-${selected}`,
      );
      location.href = `/bookings/${h.id}`;
    } catch (e) {
      setError((e as Error).message);
      setBusy(false);
    }
  }
  return (
    <div className="page">
      <a className="back" href="/">
        ← The collection
      </a>
      <div className="detail-grid">
        <div className="equipment-detail">
          <div className="detail-art">
            <Label>{p.category} / Fieldroom collection</Label>
            <Image
              name={p.image}
              alt={`Original illustration of ${p.name}`}
              eager
            />
          </div>
          <div className="details-copy">
            <h2>Get to know your gear.</h2>
            <p>{p.description}</p>
            <dl>
              <dt>In the details</dt>
              <dd>{p.specifications}</dd>
              <dt>Comes with</dt>
              <dd>{p.accessories}</dd>
              <dt>Pickup</dt>
              <dd>{p.assets[0]?.location}</dd>
            </dl>
          </div>
        </div>
        <div className="booking-panel">
          <Label>{p.category} / Borrow from the library</Label>
          <h1>{p.name}</h1>
          <p className="muted">
            A specific copy, reserved just for your dates.
          </p>
          <form onSubmit={hold}>
            <label>
              Your equipment copy
              <select
                value={selected}
                onChange={(e) => setAssetId(e.target.value)}
              >
                {p.assets.map((a) => (
                  <option
                    key={a.id}
                    value={a.id}
                    disabled={a.state !== 'Ready' || a.atRisk}
                  >
                    {a.tag} ·{' '}
                    {a.atRisk
                      ? 'Awaiting overdue return'
                      : a.state !== 'Ready'
                        ? a.state
                        : a.condition}
                  </option>
                ))}
              </select>
            </label>
            <TimeFields value={times} change={setTimes} />
            <p className="timezone">◷ {p.policy.timeZone}</p>
            <ErrorBox message={error} />
            {session?.authenticated ? (
              <button
                className="primary full"
                disabled={
                  busy ||
                  !p.assets.some(
                    (a) =>
                      a.id === selected && a.state === 'Ready' && !a.atRisk,
                  )
                }
              >
                {busy ? 'Securing your copy…' : 'Hold this equipment'}{' '}
                <span aria-hidden="true">→</span>
              </button>
            ) : (
              <a
                className="button primary full"
                href={`/account?next=${encodeURIComponent(location.pathname)}`}
              >
                Sign in to borrow →
              </a>
            )}
            <p className="micro">
              A {p.policy.holdSeconds / 60}-minute hold gives you time to
              review. No payment required.
            </p>
          </form>
          <PolicyNote p={p.policy} />
        </div>
      </div>
      <section className="schedule">
        <Label>Plan your pickup</Label>
        <h2>On the calendar.</h2>
        <p>
          Reserved intervals include turnaround until the time shown. Gaps are
          advisory; a hold secures your copy.
        </p>
        <div
          className="table-wrap"
          tabIndex={0}
          role="region"
          aria-label="Scrollable records"
        >
          <table>
            <caption className="sr-only">
              Upcoming equipment occupancy in Pacific/Auckland
            </caption>
            <thead>
              <tr>
                <th>Copy</th>
                <th>From</th>
                <th>Unavailable until</th>
                <th>Purpose</th>
              </tr>
            </thead>
            <tbody>
              {p.schedule?.length ? (
                p.schedule.map((s, i) => (
                  <tr key={i}>
                    <td className="mono">
                      {p.assets.find((a) => a.id === s.assetId)?.tag}
                    </td>
                    <td>{dates(s.start)}</td>
                    <td>{dates(s.blockedUntil)}</td>
                    <td>{s.kind}</td>
                  </tr>
                ))
              ) : (
                <tr>
                  <td colSpan={4}>
                    No reserved intervals in the next 90 days. Choose dates
                    above to secure a hold.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  );
}

function Account({
  session,
  refresh,
}: {
  session?: Session;
  refresh: () => void;
}) {
  const [register, setRegister] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  async function submit(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    const values = Object.fromEntries(new FormData(e.currentTarget));
    setBusy(true);
    setError('');
    try {
      await api(`/api/session/${register ? 'register' : 'login'}`, values);
      await csrf();
      const next = new URLSearchParams(location.search).get('next');
      location.href = safeNext(next);
    } catch (e) {
      setError((e as Error).message);
      setBusy(false);
    }
  }
  if (session?.authenticated)
    return (
      <div className="page account-page">
        <Label>Your membership</Label>
        <h1>Hello, {session.displayName}.</h1>
        <p>{session.email}</p>
        <p>
          {session.manager
            ? 'Manager · inventory and lending desk access'
            : 'Member · borrow from the collection'}
        </p>
        <a className="button primary" href="/bookings">
          See your loans →
        </a>
        <button
          className="quiet"
          onClick={async () => {
            try {
              await api('/api/session/logout', {});
              await csrf();
              refresh();
            } catch (e) {
              setError((e as Error).message);
            }
          }}
        >
          Sign out
        </button>
        <ErrorBox message={error} />
      </div>
    );
  return (
    <div className="page auth-grid">
      <div>
        <Label>Your local equipment library</Label>
        <h1>
          Welcome to
          <br />
          the equipment room.
        </h1>
        <p>
          One account for the whole collection.
          <br />
          Book your equipment, collect it, and get started.
        </p>
        <Image name="drill" alt="Original illustration of a cordless drill" />
      </div>
      <section className="auth-form">
        <Label>{register ? 'Join the library' : 'Welcome back'}</Label>
        <h2>
          {register ? 'Make yourself at home.' : 'Pick up where you left off.'}
        </h2>
        <form onSubmit={submit}>
          {register && (
            <label>
              Your name
              <input name="name" required maxLength={80} autoComplete="name" />
            </label>
          )}
          <label>
            Email
            <input
              type="email"
              name="email"
              required
              autoComplete="email"
              maxLength={254}
            />
          </label>
          <label>
            Password
            <input
              type="password"
              name="password"
              required
              minLength={register ? 12 : undefined}
              maxLength={128}
              autoComplete={register ? 'new-password' : 'current-password'}
            />
          </label>
          {register && (
            <p className="micro">
              At least 12 characters, with uppercase, lowercase, a number and a
              symbol.
            </p>
          )}
          <ErrorBox message={error} />
          <button className="primary full" disabled={busy}>
            {busy ? 'One moment…' : register ? 'Create my account' : 'Sign in'}{' '}
            →
          </button>
        </form>
        <button
          className="text-link"
          onClick={() => {
            setRegister(!register);
            setError('');
          }}
        >
          {register
            ? 'Already a member? Sign in'
            : 'New here? Join the library'}
        </button>
      </section>
    </div>
  );
}

function MyLoans() {
  const load = useLoad<Booking[]>('/api/bookings');
  if (!load.data) return <Loading error={load.error} retry={load.refresh} />;
  const active = load.data.filter((b) =>
    ['Hold', 'Confirmed', 'CheckedOut'].includes(b.state),
  );
  const history = load.data.filter((b) => !active.includes(b));
  return (
    <div className="page">
      <Label>Your borrowing history</Label>
      <h1>Your loans.</h1>
      {[
        ['Upcoming & in your hands', active],
        ['The archive', history],
      ].map(([title, rows]) => (
        <section className="loan-section" key={title as string}>
          <h2>{title as string}</h2>
          {(rows as Booking[]).length ? (
            <div className="loans">
              {(rows as Booking[]).map((b) => (
                <a className="loan-row" href={`/bookings/${b.id}`} key={b.id}>
                  <Image name={b.image} alt="" />
                  <div>
                    <Label>{b.reference || b.asset}</Label>
                    <h3>{b.product}</h3>
                    <p>
                      {dates(b.start)} → {dates(b.end)}
                    </p>
                  </div>
                  <span className="status">
                    {b.atRisk ? 'At risk · contact desk' : b.state}
                  </span>
                  <span aria-hidden="true">↗</span>
                </a>
              ))}
            </div>
          ) : (
            <div className="empty">
              <p>
                {active === rows
                  ? 'Your next project is waiting on the shelf.'
                  : 'Completed and cancelled loans will appear here.'}
              </p>
              <a href="/">Explore the collection →</a>
            </div>
          )}
        </section>
      ))}
    </div>
  );
}

function BookingPage({ id, manager }: { id: string; manager: boolean }) {
  const load = useLoad<Booking>(`/api/bookings/${id}`);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [accepted, setAccepted] = useState(false);
  const [action, setAction] = useState('');
  const [times, setTimes] = useState(defaultTimes);
  const [now, setNow] = useState(Date.now());
  const [offset, setOffset] = useState(0);
  useEffect(() => {
    if (load.data)
      setOffset(new Date(load.data.serverTime).getTime() - Date.now());
  }, [load.data]);
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, []);
  if (!load.data) return <Loading error={load.error} retry={load.refresh} />;
  const b = load.data;
  const left = Math.max(
    0,
    Math.ceil((new Date(b.expiresAt || 0).getTime() - now - offset) / 1000),
  );
  async function change(verb: string, body: unknown) {
    setBusy(true);
    setError('');
    try {
      await api(`/api/bookings/${id}/${verb}`, body, `${id}-${verb}`);
      setAction('');
      load.refresh();
    } catch (e) {
      setError((e as Error).message);
      if (e instanceof ApiError && e.code === 'hold_expired') load.refresh();
    } finally {
      setBusy(false);
    }
  }
  return (
    <div className="page">
      <a className="back" href={manager ? '/desk' : '/bookings'}>
        ← {manager ? 'Lending desk' : 'My loans'}
      </a>
      <div className="booking-detail">
        <div>
          <Label>{b.reference || 'Your equipment hold'}</Label>
          <h1>
            {b.state === 'Hold'
              ? 'One last look.'
              : b.state === 'Confirmed'
                ? 'It’s on the calendar.'
                : 'Your equipment loan.'}
          </h1>
          <span className="status">{b.state}</span>
          {b.atRisk && (
            <div className="notice" role="status">
              This booking is at risk because the equipment is unavailable.
              Contact the lending desk; your booking has been preserved.
            </div>
          )}
          <div className="loan-object">
            <Image name={b.image} alt={b.product} />
            <div>
              <Label>{b.asset}</Label>
              <h2>{b.product}</h2>
              <p>{b.accessories}</p>
            </div>
          </div>
          <div className="interval-summary">
            <div>
              <Label>Pick up</Label>
              <strong>{dates(b.start)}</strong>
            </div>
            <span aria-hidden="true">→</span>
            <div>
              <Label>Return</Label>
              <strong>{dates(b.end)}</strong>
            </div>
          </div>
          <p className="timezone">
            {b.policy.timeZone} · {b.location}
          </p>
          <PolicyNote p={b.policy} />
          <ErrorBox message={error} />
          {['Expired', 'Released'].includes(b.state) && (
            <div className="notice">
              This hold is no longer active.{' '}
              <a href="/">Choose equipment and dates again →</a>
            </div>
          )}
          {b.state === 'Hold' && (
            <div className="confirm-box">
              <p role="timer" aria-label="Hold time remaining">
                Your copy is held for{' '}
                <strong>
                  {Math.floor(left / 60)}:{String(left % 60).padStart(2, '0')}
                </strong>
              </p>
              {left > 0 ? (
                <>
                  <label className="check">
                    <input
                      type="checkbox"
                      checked={accepted}
                      onChange={(e) => setAccepted(e.target.checked)}
                    />{' '}
                    I accept this interval, this copy and the lending policy.
                  </label>
                  <button
                    className="primary full"
                    disabled={busy || !accepted}
                    onClick={() =>
                      change('confirm', { reason: 'Policy accepted' })
                    }
                  >
                    {busy ? 'Confirming…' : 'Confirm my booking →'}
                  </button>
                  <button
                    className="quiet"
                    disabled={busy}
                    onClick={() =>
                      change('release', { reason: 'Member released hold' })
                    }
                  >
                    Release this hold
                  </button>
                </>
              ) : (
                <p>
                  This hold has elapsed.{' '}
                  <a href="/">Choose equipment and dates again →</a>
                </p>
              )}
            </div>
          )}
          {b.state === 'Confirmed' && (
            <div className="actions">
              <button onClick={() => setAction('reschedule')}>
                Change dates
              </button>
              <button className="quiet" onClick={() => setAction('cancel')}>
                Cancel booking
              </button>
              {manager && (
                <>
                  <button onClick={() => setAction('pickup')}>
                    Record pickup
                  </button>
                  <button
                    className="quiet"
                    onClick={() => setAction('no-show')}
                  >
                    Record no-show
                  </button>
                </>
              )}
            </div>
          )}
          {manager && b.state === 'CheckedOut' && (
            <button className="primary" onClick={() => setAction('return')}>
              Record return →
            </button>
          )}
          {manager && b.state === 'Maintenance' && (
            <button onClick={() => setAction('end-maintenance')}>
              End maintenance
            </button>
          )}
          {action && (
            <form
              className="action-form"
              onSubmit={(e) => {
                e.preventDefault();
                const fields = Object.fromEntries(
                  new FormData(e.currentTarget),
                );
                change(action, {
                  ...fields,
                  ...(action === 'reschedule' ? times : {}),
                });
              }}
            >
              <h2>
                {
                  (
                    {
                      cancel: 'Cancel this booking?',
                      reschedule: 'Choose a new interval',
                      pickup: 'Hand over this copy?',
                      return: 'Receive the equipment',
                      'no-show': 'Record a missed loan?',
                      'end-maintenance': 'Release this maintenance block?',
                    } as Record<string, string>
                  )[action]
                }
              </h2>
              <p>
                {action === 'reschedule'
                  ? 'The original reservation stays in place if the new dates are unavailable.'
                  : action === 'cancel'
                    ? 'This releases your reservation. You will need a new booking to borrow again.'
                    : `Check the asset label: ${b.asset}. This action is recorded in the loan history.`}
              </p>
              {action === 'reschedule' && (
                <TimeFields value={times} change={setTimes} />
              )}
              {action === 'return' && (
                <>
                  <label>
                    Return condition
                    <select name="condition">
                      <option>Good</option>
                      <option>Damaged</option>
                    </select>
                  </label>
                  <label>
                    Accessory check
                    <textarea
                      required
                      name="accessories"
                      maxLength={500}
                      placeholder="Check each included accessory; note anything missing."
                    />
                  </label>
                </>
              )}
              <label>
                Reason / desk note
                <textarea autoFocus required name="reason" maxLength={500} />
              </label>
              <button
                className={action === 'cancel' ? 'danger' : 'primary'}
                disabled={busy}
              >
                Confirm {action}
              </button>
              <button
                type="button"
                className="quiet"
                onClick={() => setAction('')}
              >
                Keep as is
              </button>
            </form>
          )}
          {b.custody && (
            <section>
              <h2>Physical custody</h2>
              <p>
                Picked up {dates(b.custody.pickedUpAt)}
                {b.custody.returnedAt
                  ? ` · Returned ${dates(b.custody.returnedAt)}`
                  : ' · Still in member’s hands'}
              </p>
              <p>
                {b.custody.condition} {b.custody.accessoryCheck}
              </p>
            </section>
          )}
        </div>
        <aside className="history">
          <Label>The record</Label>
          <h2>Loan history</h2>
          <ol>
            {b.history.map((h, i) => (
              <li key={i}>
                <span className="mono">{dates(h.at)}</span>
                <h3>{h.action.replace(/([a-z])([A-Z])/g, '$1 $2')}</h3>
                <p>{h.reason}</p>
              </li>
            ))}
          </ol>
          <p className="micro">
            A durable record of this loan. If a request is interrupted, retry it
            safely.
          </p>
        </aside>
      </div>
    </div>
  );
}

function Desk({ session }: { session?: Session }) {
  const [tab, setTab] = useState('loans');
  if (!session)
    return (
      <div className="page" role="status">
        Checking desk access…
      </div>
    );
  if (!session.manager)
    return (
      <div className="page">
        <h1>The lending desk.</h1>
        <p>Manager access is required.</p>
        <a href="/account">Sign in with a manager account →</a>
      </div>
    );
  return (
    <div className="page desk">
      <Label>Fieldroom / Operations</Label>
      <h1>The lending desk.</h1>
      <p className="muted">
        Pickups, returns and equipment that needs attention.
      </p>
      <div className="tabs" role="tablist" aria-label="Desk views">
        {['loans', 'inventory', 'policy', 'audit'].map((t) => (
          <button
            role="tab"
            aria-selected={tab === t}
            key={t}
            className={tab === t ? 'selected' : 'quiet'}
            onClick={() => setTab(t)}
          >
            {t[0].toUpperCase() + t.slice(1)}
          </button>
        ))}
      </div>
      <div role="tabpanel">
        {tab === 'loans' ? (
          <DeskLoans />
        ) : tab === 'inventory' ? (
          <Inventory />
        ) : tab === 'policy' ? (
          <PolicyEditor />
        ) : (
          <Audit />
        )}
      </div>
    </div>
  );
}
function DeskLoans() {
  const [state, setState] = useState('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const load = useLoad<{
    total: number;
    bookings: (Booking & { member: string; overdue: boolean })[];
  }>(
    `/api/desk/bookings?${new URLSearchParams({ state, q: search, page: String(page) })}`,
  );
  return (
    <>
      <div className="search-row">
        <label>
          Find a loan
          <input
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setPage(1);
            }}
            placeholder="Reference, member or asset label"
          />
        </label>
        <label>
          Show
          <select
            value={state}
            onChange={(e) => {
              setState(e.target.value);
              setPage(1);
            }}
          >
            <option value="">All loans</option>
            <option value="Confirmed">Upcoming pickups</option>
            <option value="CheckedOut">Awaiting return</option>
            <option>Overdue</option>
            <option>Returned</option>
            <option>Cancelled</option>
            <option>Maintenance</option>
          </select>
        </label>
        <button onClick={load.refresh}>Refresh</button>
      </div>
      {!load.data ? (
        <Loading error={load.error} retry={load.refresh} />
      ) : (
        <>
          <div
            className="table-wrap"
            tabIndex={0}
            role="region"
            aria-label="Scrollable records"
          >
            <table>
              <thead>
                <tr>
                  <th>Loan / member</th>
                  <th>Equipment</th>
                  <th>Pickup</th>
                  <th>Return</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                {load.data.bookings.map((b) => (
                  <tr key={b.id}>
                    <td>
                      <a href={`/bookings/${b.id}`}>
                        {b.reference || 'Maintenance'} ↗
                      </a>
                      <small>{b.member}</small>
                    </td>
                    <td>
                      {b.product}
                      <small className="mono">{b.asset}</small>
                    </td>
                    <td>{dates(b.start)}</td>
                    <td>{dates(b.end)}</td>
                    <td>
                      <span
                        className={`status ${b.overdue || b.atRisk ? 'risk' : ''}`}
                      >
                        {b.overdue ? 'Overdue' : b.atRisk ? 'At risk' : b.state}
                      </span>
                    </td>
                  </tr>
                ))}
                {!load.data.bookings.length && (
                  <tr>
                    <td colSpan={5}>No loans match this view.</td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
          <div className="pagination">
            <button disabled={page === 1} onClick={() => setPage((p) => p - 1)}>
              Previous
            </button>
            <span>
              Page {page} · {load.data.total} records
            </span>
            <button
              disabled={page * 50 >= load.data.total}
              onClick={() => setPage((p) => p + 1)}
            >
              Next
            </button>
          </div>
        </>
      )}
    </>
  );
}
function Inventory() {
  const load = useLoad<Product[]>('/api/desk/inventory');
  const [edit, setEdit] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [times, setTimes] = useState(defaultTimes);
  async function submit(
    e: React.FormEvent<HTMLFormElement>,
    path: string,
    scope?: string,
    extra = {},
  ) {
    e.preventDefault();
    const fields = Object.fromEntries(new FormData(e.currentTarget));
    setBusy(true);
    setError('');
    try {
      await api(path, { ...fields, ...extra }, scope);
      setEdit('');
      load.refresh();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  if (!load.data) return <Loading error={load.error} retry={load.refresh} />;
  return (
    <>
      <InventoryCreation products={load.data} refresh={load.refresh} />
      <ErrorBox message={error} />
      <p className="muted">
        Retirement requires resolving commitments first. Return damaged
        equipment, inspect it, then allow turnaround before lending again.
      </p>
      {load.data.map((p) => (
        <section className="inventory-product" key={p.id}>
          <div className="inventory-title">
            <Image name={p.image} alt="" />
            <div>
              <Label>{p.category}</Label>
              <h2>{p.name}</h2>
            </div>
            <button onClick={() => setEdit(`product-${p.id}`)}>
              Edit product
            </button>
            <a href={`/products/${p.id}`}>Schedule ↗</a>
          </div>
          {edit === `product-${p.id}` && (
            <form
              className="action-form"
              onSubmit={(e) =>
                submit(e, `/api/desk/products/${p.id}`, `product-${p.id}`, {
                  version: p.version,
                })
              }
            >
              <label>
                Name
                <input
                  name="name"
                  defaultValue={p.name}
                  required
                  maxLength={120}
                />
              </label>
              <label>
                Category
                <select name="category" defaultValue={p.category}>
                  <option>Cameras</option>
                  <option>Tools</option>
                  <option>Outdoors</option>
                </select>
              </label>
              <label>
                Description
                <textarea
                  name="description"
                  defaultValue={p.description}
                  required
                  maxLength={2000}
                />
              </label>
              <label>
                Specifications
                <input
                  name="specifications"
                  defaultValue={p.specifications}
                  maxLength={1000}
                />
              </label>
              <label>
                Accessories
                <textarea
                  name="accessories"
                  defaultValue={p.accessories}
                  maxLength={1000}
                />
              </label>
              <label>
                Reason
                <input name="reason" required maxLength={500} />
              </label>
              <button className="primary" disabled={busy}>
                Save product
              </button>
              <button type="button" onClick={() => setEdit('')}>
                Cancel
              </button>
            </form>
          )}
          <div className="asset-list">
            {p.assets.map((a) => (
              <div className="asset-entry" key={a.id}>
                <div className="asset-line">
                  <span className="mono">{a.tag}</span>
                  <span>{a.condition}</span>
                  <span className="status">{a.state}</span>
                  <div className="actions">
                    {a.state !== 'Retired' && (
                      <>
                        <button
                          className="quiet"
                          onClick={() => setEdit(`edit-${a.id}`)}
                        >
                          Condition
                        </button>
                        <button
                          className="quiet"
                          onClick={() => setEdit(`maintenance-${a.id}`)}
                        >
                          Maintenance
                        </button>
                        <button
                          className="quiet"
                          onClick={() => setEdit(`retire-${a.id}`)}
                        >
                          Retire
                        </button>
                      </>
                    )}
                    {a.state === 'Inspection' && (
                      <button onClick={() => setEdit(`inspect-${a.id}`)}>
                        Inspect & ready
                      </button>
                    )}
                  </div>
                </div>
                {edit.endsWith(a.id) && (
                  <form
                    className="action-form"
                    onSubmit={(e) => {
                      const action = edit.split('-')[0];
                      submit(
                        e,
                        action === 'maintenance'
                          ? '/api/desk/maintenance'
                          : `/api/desk/assets/${a.id}/${action}`,
                        edit,
                        action === 'maintenance'
                          ? { assetId: a.id, ...times }
                          : {},
                      );
                    }}
                  >
                    <h3>
                      {edit.startsWith('retire')
                        ? `Retire ${a.tag}?`
                        : `Update ${a.tag}`}
                    </h3>
                    {edit.startsWith('maintenance') && (
                      <TimeFields value={times} change={setTimes} />
                    )}
                    {edit.startsWith('edit-') && (
                      <label>
                        Condition
                        <input
                          name="condition"
                          defaultValue={a.condition}
                          required
                          maxLength={150}
                        />
                      </label>
                    )}
                    <label>
                      Reason / inspection record
                      <textarea
                        name="reason"
                        required
                        maxLength={500}
                        autoFocus
                      />
                    </label>
                    <button
                      className={
                        edit.startsWith('retire') ? 'danger' : 'primary'
                      }
                      disabled={busy}
                    >
                      Confirm update
                    </button>
                    <button type="button" onClick={() => setEdit('')}>
                      Keep as is
                    </button>
                  </form>
                )}
              </div>
            ))}
          </div>
        </section>
      ))}
    </>
  );
}
function InventoryCreation({
  products,
  refresh,
}: {
  products: Product[];
  refresh: () => void;
}) {
  const [kind, setKind] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  return (
    <section>
      <div className="actions">
        <button onClick={() => setKind('products')}>Add product model</button>
        <button onClick={() => setKind('assets')}>Add physical copy</button>
      </div>
      {kind && (
        <form
          className="action-form"
          onSubmit={async (e) => {
            e.preventDefault();
            const data = Object.fromEntries(new FormData(e.currentTarget));
            setBusy(true);
            setError('');
            try {
              await api(`/api/desk/${kind}`, data, `new-${kind}`);
              setKind('');
              refresh();
            } catch (e) {
              setError((e as Error).message);
            } finally {
              setBusy(false);
            }
          }}
        >
          <h2>
            {kind === 'products'
              ? 'A new model for the collection'
              : 'Label a physical copy'}
          </h2>
          {kind === 'products' ? (
            <>
              <label>
                Name
                <input name="name" required maxLength={120} />
              </label>
              <label>
                Category
                <select name="category">
                  <option>Cameras</option>
                  <option>Tools</option>
                  <option>Outdoors</option>
                </select>
              </label>
              <label>
                Description
                <textarea
                  name="description"
                  required
                  minLength={10}
                  maxLength={2000}
                />
              </label>
              <label>
                Specifications
                <input name="specifications" required maxLength={1000} />
              </label>
              <label>
                Included accessories
                <input name="accessories" required maxLength={1000} />
              </label>
              <label>
                Original illustration
                <select name="image">
                  {[
                    'camera',
                    'compact',
                    'drill',
                    'sander',
                    'tent',
                    'pack',
                    'lantern',
                    'mat',
                    'cookset',
                    'tripod',
                    'microphone',
                    'light',
                    'clamps',
                    'measure',
                    'jigsaw',
                  ].map((x) => (
                    <option key={x}>{x}</option>
                  ))}
                </select>
              </label>
            </>
          ) : (
            <>
              <label>
                Product model
                <select name="productId">
                  {products.map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.name}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Unique asset label
                <input
                  name="tag"
                  required
                  maxLength={40}
                  pattern="[A-Za-z0-9-]+"
                  placeholder="FR-016-01"
                />
              </label>
              <label>
                Condition
                <input name="condition" required maxLength={150} />
              </label>
              <label>
                Pickup location
                <input
                  name="location"
                  defaultValue="Fieldroom · 14 Workshop Lane"
                  required
                  maxLength={200}
                />
              </label>
            </>
          )}
          <label>
            Reason / intake note
            <textarea name="reason" required maxLength={500} />
          </label>
          <ErrorBox message={error} />
          <button className="primary" disabled={busy}>
            Save to inventory
          </button>
          <button type="button" onClick={() => setKind('')}>
            Cancel
          </button>
        </form>
      )}
    </section>
  );
}
function PolicyEditor() {
  const load = useLoad<{ policy: Policy }>('/api/policy');
  const [error, setError] = useState('');
  const [saved, setSaved] = useState(false);
  const [busy, setBusy] = useState(false);
  if (!load.data) return <Loading error={load.error} retry={load.refresh} />;
  const p = load.data.policy;
  const fields: [keyof Policy, string, number, number][] = [
    ['openHour', 'Opening hour', 0, 22],
    ['closeHour', 'Closing hour', 1, 23],
    ['leadMinutes', 'Minimum notice (minutes)', 0, 10080],
    ['maximumHours', 'Maximum loan (hours)', 1, 720],
    ['horizonDays', 'Advance horizon (days)', 1, 365],
    ['bufferMinutes', 'Turnaround (minutes)', 0, 1440],
    ['cancellationMinutes', 'Change cutoff (minutes)', 0, 10080],
    ['holdSeconds', 'Hold lifetime (seconds)', 30, 900],
    ['holdLimit', 'Holds per member', 1, 5],
  ];
  return (
    <form
      className="policy-editor"
      key={p.version}
      onSubmit={async (e) => {
        e.preventDefault();
        const f = Object.fromEntries(new FormData(e.currentTarget));
        const policy = { ...p };
        for (const [key] of fields)
          Object.assign(policy, { [key]: Number(f[key]) });
        setBusy(true);
        setError('');
        try {
          await api(
            '/api/desk/policy',
            { policy, reason: f.reason },
            'policy-edit',
          );
          setSaved(true);
          load.refresh();
        } catch (e) {
          setError((e as Error).message);
        } finally {
          setBusy(false);
        }
      }}
    >
      <h2>The lending policy</h2>
      <p>
        Version {p.version} · {p.timeZone}. Changes apply to new holds.
        Confirmed loans keep their accepted policy.
      </p>
      <div className="policy-fields">
        {fields.map(([key, label, min, max]) => (
          <label key={key}>
            {label}
            <input
              type="number"
              required
              name={key}
              min={min}
              max={max}
              defaultValue={p[key]}
            />
          </label>
        ))}
      </div>
      <label>
        Reason for change
        <textarea name="reason" required maxLength={500} />
      </label>
      <ErrorBox message={error} />
      {saved && (
        <p role="status">Policy saved. Existing commitments are unchanged.</p>
      )}
      <button className="primary" disabled={busy}>
        Save policy
      </button>
    </form>
  );
}
function Audit() {
  const [page, setPage] = useState(1);
  const load = useLoad<
    {
      id: number;
      actorId: string;
      action: string;
      reason: string;
      at: string;
      allocationId?: string;
      assetId?: string;
    }[]
  >(`/api/desk/audit?page=${page}`);
  if (!load.data) return <Loading error={load.error} retry={load.refresh} />;
  return (
    <>
      <h2>The operations record</h2>
      <div
        className="table-wrap"
        tabIndex={0}
        role="region"
        aria-label="Scrollable records"
      >
        <table>
          <thead>
            <tr>
              <th>When</th>
              <th>Action</th>
              <th>Actor</th>
              <th>Reason / reference</th>
            </tr>
          </thead>
          <tbody>
            {load.data.map((a) => (
              <tr key={a.id}>
                <td>{dates(a.at)}</td>
                <td>{a.action}</td>
                <td className="mono">{a.actorId.slice(0, 8)}</td>
                <td>
                  {a.reason}
                  {a.allocationId && (
                    <small>
                      <a href={`/bookings/${a.allocationId}`}>Open record ↗</a>
                    </small>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="pagination">
        <button disabled={page === 1} onClick={() => setPage((p) => p - 1)}>
          Previous
        </button>
        <span>Page {page}</span>
        <button
          disabled={load.data.length < 100}
          onClick={() => setPage((p) => p + 1)}
        >
          Next
        </button>
      </div>
    </>
  );
}

export { TimeFields, ErrorBox, PolicyNote };
if (document.getElementById('root'))
  createRoot(document.getElementById('root')!).render(
    <React.StrictMode>
      <App />
    </React.StrictMode>,
  );
