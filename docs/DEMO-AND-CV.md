# Demo and interview guide

## A five-minute walkthrough

1. Start the app and sign in as `admin@stockpilot.demo` with `StockPilot!2026`.
2. Explain the overview: catalog stock value, available units, reserved orders, low-stock alerts.
3. Open Inventory, create a product, and receive 5 units with a receipt reference.
4. Reserve 3 units on Orders. Show that on-hand stays 5 while available drops to 2.
5. Try to reserve 3 more. Explain the conflict and why no partial order is recorded.
6. Cancel the first order. Availability returns to 5. Reserve again and ship to demonstrate the other transition.
7. Open Activity to trace the changes.
8. Sign out and use `viewer@stockpilot.demo` to show restricted actions, or create a second company to demonstrate isolation.
9. Open the concurrency tests and the GitHub Actions run. Explain the assertions and their limits.

## CV wording

Use wording you understand and can defend. A starting point after reviewing the implementation:

> **StockPilot — ASP.NET Core inventory and order management**  
> Built a multi-tenant application with EF Core, ASP.NET Core Identity, and a responsive Razor Pages dashboard. Implemented transactional stock reservations, tenant-scoped idempotency, role-based authorization, and auditable order transitions. Added 20 HTTP integration tests covering concurrency, tenant isolation, validation, and CSRF, with GitHub Actions for build, test, and publish validation.

Repository: https://github.com/ZiadMaghraby/StockPilot

Do not claim production customers, uptime, throughput, SQL Server validation, Docker execution, or cloud deployment without performing and recording that work. The Docker configuration is supplied but was not run in the initial development environment. This version was developed with AI assistance; be transparent if asked and be ready to explain and modify the code yourself.

## Questions worth practicing

- Why is a conditional SQL update safer than checking available stock in C# first?
- What rolls back if the audit insert fails after stock was reserved?
- Why is `RequestId` scoped to a company? What if the same key has a different quantity?
- What happens when cancellation and shipment run concurrently?
- What do global query filters protect, and how could future code bypass them?
- Why store money in cents? Which currencies would need a different model?
- Why did the first release choose a monolith and SQLite? What changes for SQL Server?
- Why do cookie-authenticated API writes need CSRF protection?
- Which release limitations would you address before inviting real users?

## Contribution workflow

Choose one real roadmap issue, write its acceptance criteria, implement it on a branch, add tests that can fail for the right reason, and open a PR explaining the result. Keep changes small enough to review. Avoid empty commits, duplicate PRs, artificial daily activity, or claims of review that did not happen.

For external open-source contributions, first reproduce an actual bug or documentation gap in a dependency. Follow that project's contribution rules and expect maintainers to decide whether to accept the PR. This repository's initial PRs are self-authored project work, not third-party open-source approvals.
