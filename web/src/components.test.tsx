import { afterEach, describe, expect, it } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { TimeFields, ErrorBox, PolicyNote } from './main';
import { dates, defaultTimes } from './api';
afterEach(cleanup);
describe('booking clarity', () => {
  it('labels both time fields and reports changed local values', () => { let current = defaultTimes(); render(<TimeFields value={current} change={v => { current = v; }} />); fireEvent.change(screen.getByLabelText('Pick up'), { target: { value: '2026-12-10T10:30' } }); expect(current.start).toBe('2026-12-10T10:30'); expect(screen.getByLabelText('Return')).toBeRequired(); });
  it('announces recoverable session errors with a sign-in route', () => { render(<ErrorBox message="Your session has expired." />); expect(screen.getByRole('alert')).toHaveTextContent('session has expired'); expect(screen.getByRole('link', { name: 'Sign in' })).toHaveAttribute('href', '/account?next=%2F'); });
  it('renders the accepted timezone and policy constraints', () => { render(<PolicyNote p={{ id: 1, timeZone: 'Pacific/Auckland', openHour: 9, closeHour: 18, leadMinutes: 60, maximumHours: 168, horizonDays: 90, bufferMinutes: 60, cancellationMinutes: 120, holdSeconds: 300, holdLimit: 2, version: 1 }} />); expect(screen.getByText('Pacific/Auckland')).toBeInTheDocument(); expect(screen.getByText(/2 hours before pickup/)).toBeInTheDocument(); });
  it('formats the organization timezone regardless of browser timezone', () => { expect(dates('2026-10-19T21:00:00Z')).toMatch(/20 Oct.*10:00/); });
});
