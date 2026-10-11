# Reserving one physical thing, reliably

A shared library has two sources of truth: who has reserved an interval, and who is
actually holding the object. A stale availability screen, lost HTTP response or overdue
loan must not turn those into two promises for one copy.

Fieldroom deliberately keeps the design small. A single PostgreSQL allocation ledger
holds reservations, expiring holds and maintenance. A GiST exclusion constraint rejects
overlap even when code is bypassed. Row locks establish the decision order, including a
member lock to enforce limits across assets. A fresh post-lock database clock handles
the awkward case where confirmation started on time but waited past expiry. Requested
times and buffered occupancy remain separate so users see the interval they accepted.

The race suite uses separate database contexts/connections and a synchronization gate.
Two members attempting the same interval produced one hold and one corresponding audit
event. Direct SQL attempts also failed under the database constraint. Additional tests
observe a real PostgreSQL lock waiter, then release it after the stored expiry boundary;
confirmation fails and no booking reference appears. The expiry decision works without
a cleanup worker. Identical, contained and turnaround-overlapping intervals fail, while
different assets and valid buffered adjacency succeed.

Retries need more than disabling a button. The transaction stores actor/operation/key,
payload hash and outcome. Savepoints allow a failed reschedule to preserve the original
reservation while persisting its conflict outcome. The browser keeps an uncertain key
until a full response arrives. Independent review caught two subtle gaps: reusing a
completed key forever prevented fresh holds, and accepting headers before reading the
body could duplicate a committed creation after body loss. Regression tests now cover
both, plus authentication/throttling during recovery.

Custody uses one open checkout per asset. A due time does not imply a return. Late
equipment blocks new allocations; existing commitments become visibly at risk. Damaged
returns enter inspection, and inspection cannot shorten turnaround already promised
under an accepted policy. Early returns retain the original range. This sacrifices
some utilization for simple, explainable safety.

The visual design uses Schibsted Grotesk, forest ink, pale paper and original vector
still lifes. A [research-led revision](DESIGN.md) replaced the initial serif treatment
with a consistent type hierarchy, illustrated category links and a responsive catalog rail.
The date summary emphasizes copy identity and Auckland-local times. Real mobile and
desktop screenshots and automated accessibility checks revealed a schedule scroller
that lacked keyboard focus; it was fixed. These checks cover the tested screens and
journeys, not a complete accessibility certification.

Observed results and exact commands are in [verification](VERIFICATION.md). This is a
fictional portfolio system: there are no invented customers, commercial outcomes,
load benchmarks, live deposits or deployment evidence.
