# StockPilot

A multi-tenant inventory and order management application built with ASP.NET Core.

## Project brief

Small teams need to know which stock is available, which units are reserved, and who changed an order. StockPilot brings these operations into one workspace.

The first milestone covers company accounts, role-based access, product catalogs, stock receipts, reservations, shipment confirmation, cancellation, and an audit history.

Engineering goals:

- Derive tenant identity from authenticated claims, never client-supplied company IDs.
- Prevent overselling through conditional database updates and transactions.
- Make order retries safe with tenant-scoped idempotency keys.
- Test important invariants over HTTP against a relational database.
- Keep setup reproducible and the interface useful for a short portfolio demo.

Implementation and documentation are tracked through repository issues and pull requests.
