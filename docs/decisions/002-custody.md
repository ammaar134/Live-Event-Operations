# Custody is not a time interval

The allocation ledger protects planned occupancy. Checkouts record possession:
one unreturned checkout per asset through a partial unique index, plus a composite
foreign key requiring the checkout and allocation to identify the same asset.
Only a manager can pick up a confirmed reservation during its requested interval.
Pickup creates a checkout and changes the allocation in the same transaction.

An unreturned overdue checkout blocks all new holds, confirmations and pickups.
The application checks this under the asset lock; no scheduled worker is required.
Future commitments remain intact and are displayed at risk. Inspection state and
turnaround crossing pickup also make a future commitment at risk. Managers resolve
commitments explicitly with reasons; the system never silently substitutes an asset.

Returns always remain possible for an open loan, including when overdue. A return
records condition and accessories, closes custody, and retains the original bounded
allocation. It never stretches the range into another loan. Every return also sets
ReadyAt to return time + the accepted turnaround. Damaged equipment enters Inspection;
an explicit inspection can mark it Ready but starts a new turnaround, retaining the
later of existing readiness and the current policy's new turnaround. During turnaround
the conservative implementation rejects new holds and confirmations even for future
dates. Existing reservations are not cancelled. Early release of a returned reservation
is deliberately unsupported; it keeps its original occupancy.

Retirement fails while holds, future bookings, maintenance or an open checkout remain.
Managers must contact affected members, cancel commitments explicitly with an audit
reason, end maintenance, and receive outstanding equipment. Retired assets cannot be
reactivated through metadata editing.

Permitted transitions:

| From | To | Actor |
| --- | --- | --- |
| Hold | Confirmed, Released | Hold owner |
| Hold | Expired | Guarded server decision |
| Confirmed | Cancelled | Owner before cutoff, or manager with reason |
| Confirmed | CheckedOut | Manager, during reservation |
| Confirmed | NoShow | Manager, after due time |
| CheckedOut | Returned | Manager, including overdue |
| Maintenance | MaintenanceEnded | Manager |

Rescheduling changes a Confirmed interval atomically on the same asset, before its
accepted cutoff, using its accepted policy. Exclusion failure rolls back the change
and its audit event. Terminal allocations cannot be updated by the application or
ordinary SQL updates; a trigger enforces this. Business audit is append-only through
the application and database triggers, but a database administrator can bypass them.
This is accountability, not tamper-proof compliance.
