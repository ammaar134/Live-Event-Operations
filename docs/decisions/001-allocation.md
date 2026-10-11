# One allocation ledger, one guarded decision

The invariant is that an asset has no overlapping persisted blocking ranges.
Products describe models; assets identify physical copies. An allocation is a
hold, booking, or maintenance interval in one ledger. A booking is the same row
after confirmation, with a unique reference, eliminating a second nullable
booking/allocation linkage. PostgreSQL btree_gist exclusion is the final guard.

Requested instants and buffered occupancy are separate. Occupancy uses finite,
nonempty `[)` tstzrange values, checked explicitly for nulls, omitted bounds and
timestamp infinity. Returned loans retain their original buffered occupancy.

Global guard order: policy row (shared), actor/member Identity row (exclusive),
physical asset (exclusive). Each operation affects one asset; any future multi-asset
operation must acquire assets in UUID order. Allocation reads/transitions run under
that asset guard, with row-write locks acquired when saved. The idempotency lookup and
insert run under the actor guard, so duplicate keys cannot race; they need no additional
independent lock. No path acquires a member or policy lock after an asset lock.
Every occupancy or custody mutation uses the same transaction wrapper. Actor
serialization also enforces a member's hold limit across different assets.
Policy changes take an exclusive policy lock before the actor lock and persist their
idempotency outcome in the same transaction. Product edits lock the product after the
actor and do not change occupancy. Foreign keys and constraints protect the ledger.

After all guards, sample clock_timestamp() in a separate statement. This is the
temporal decision point, at PostgreSQL microsecond precision. Expiry is <= that
instant, including after lock waits. Expire old persisted holds under the asset
guard before decisions. Advisory reads ignore logically expired holds. No
worker is necessary for correctness. No clock-dependent index predicates.

Idempotency is actor + operation + key with a SHA-256 payload digest and stored
JSON response, committed with state/audit. Same key returns the original result;
different payload is rejected. Unknown commit responses recover using the same
key. No implicit EF retry strategy wraps manual transactions; infrastructure
errors return a retryable response, with bounded browser retries by the same key.
Lock timeout is 5 seconds; statement timeout is 15 seconds. No external I/O
occurs while occupancy locks are held.

References verified 2026-10-11:
- https://www.postgresql.org/docs/current/rangetypes.html#RANGETYPES-CONSTRAINT
- https://www.postgresql.org/docs/current/sql-createindex.html
- https://www.postgresql.org/docs/current/functions-datetime.html#FUNCTIONS-DATETIME-CURRENT
- https://www.postgresql.org/docs/current/explicit-locking.html
- https://learn.microsoft.com/en-us/ef/core/saving/transactions
