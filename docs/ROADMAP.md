# Roadmap

The initial release is a working local demo. Future contributions should address a real capability or reliability gap, with a focused issue and PR.

## v0.1 — Portfolio demo

- [Tenant-isolated inventory and orders](https://github.com/ZiadMaghraby/StockPilot/issues/1)
- [Reproducible packaging and documentation](https://github.com/ZiadMaghraby/StockPilot/issues/3)

## v0.2 — Reliability and scale

| Priority | Issue | What a useful PR demonstrates |
| --- | --- | --- |
| 1 | [Migrations and SQL Server validation](https://github.com/ZiadMaghraby/StockPilot/issues/4) | Schema evolution, provider-specific correctness, and database integration tests |
| 2 | [Warehouses and stock transfers](https://github.com/ZiadMaghraby/StockPilot/issues/5) | Domain modeling and transactional transfers |
| 3 | [Multi-line orders](https://github.com/ZiadMaghraby/StockPilot/issues/6) | All-or-nothing reservations and richer idempotency |
| 4 | [Invitations and account recovery](https://github.com/ZiadMaghraby/StockPilot/issues/7) | Secure onboarding and email workflows |
| 5 | [Pagination, filters, and CSV](https://github.com/ZiadMaghraby/StockPilot/issues/8) | Stable queries, scalability, and safe exports |

See [milestones](https://github.com/ZiadMaghraby/StockPilot/milestones) for progress. Each issue has acceptance criteria. Priorities are a suggested sequence, not a promise of scheduled background work.

## Later, when justified

- Hosted staging demo with deployment automation and real operational monitoring.
- Measured load testing with a documented workload and reproducible results.
- Background low-stock notifications with an outbox and retry policy.
- Real external contributions to dependencies based on reproduced bugs or documentation gaps.

No automated daily commits, artificial contribution counts, or backdated activity. Progress should reflect features, fixes, tests, and documentation that make the project better.
