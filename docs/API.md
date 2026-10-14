# HTTP API

The minimal API shares the web application's Identity cookie. It is intended for same-origin integrations and local exploration. It does not issue JWTs or claim OAuth support.

1. Sign in using `/Account/Login`. The login form itself requires its hidden antiforgery token.
2. Preserve the session cookies.
3. `GET /api/csrf` returns `{ "token": "..." }` and sets the antiforgery cookie.
4. For API writes, preserve both cookies and send that token as `X-CSRF-TOKEN`.

OpenAPI is available to signed-in users at `/openapi/v1.json`. The short PowerShell example at `scripts/api-demo.ps1` demonstrates the complete login/session flow against the local demo. Never reuse demo credentials for an internet deployment.

| Method | Route | Access | Behavior |
| --- | --- | --- | --- |
| GET | `/api/csrf` | Signed in | Issue request token |
| GET | `/api/products` | All roles | Paginated products, by name then ID |
| POST | `/api/products` | Admin | Create product, zero opening stock |
| POST | `/api/products/{id}/receive` | Admin, Operator | Add stock and a movement |
| GET | `/api/orders` | All roles | Paginated orders, newest first |
| GET | `/api/orders/{id}` | All roles | Tenant-scoped order detail |
| POST | `/api/orders` | Admin, Operator | Reserve stock; idempotent request key |
| POST | `/api/orders/{id}/confirm` | Admin, Operator | Ship a reserved order |
| POST | `/api/orders/{id}/cancel` | Admin, Operator | Release a reserved order |
| GET | `/api/movements` | All roles | Paginated stock movements, newest first |
| GET | `/api/audit` | All roles | Paginated audit events, newest first |
| GET | `/health` | Public | Database connectivity check |

## Pagination

All four list endpoints accept `page` (one-based, default `1`) and `pageSize` (1–250). The default page size remains 250 for products, orders, and movements, and 100 for audit events. Existing clients still receive a JSON array.

For example, `GET /api/products?page=2&pageSize=50` returns the next 50 products. Responses include `X-Total-Count` (current company's total records), `X-Page`, and `X-Page-Size`. An empty company or a page past the end returns `[]` with the same metadata. Invalid numbers and out-of-range values return `400`.

Products sort by name then ID ascending. Orders, movements, and audit events sort by creation time then ID descending. The ID tie-breaker keeps pages deterministic when names or timestamps match. Pagination uses offsets, not a snapshot: concurrent additions or deletions can shift page boundaries, and counts and rows are read separately. Clients needing a snapshot should not treat multiple page requests as one atomic export.

The web interface still uses bounded lists; API pagination does not change its forms. Filters and CSV exports remain tracked in issue #8.

## Payloads

Create product:

```json
{ "sku": "KB-105", "name": "Compact keyboard", "category": "Peripherals", "priceCents": 7900, "reorderPoint": 10 }
```

Receive stock:

```json
{ "quantity": 25, "note": "Supplier delivery / PO-1042" }
```

Reserve an order (replace the sample product ID with a real one):

```json
{ "productId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "quantity": 2, "customer": "Acme Studio", "requestId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb" }
```

Use a new request ID for each logical order. Preserve it for retries. The response contains an `id`; successful reservations return `201` and a `Location` header. A replay returns the same order with `201`, even if it has since shipped or been cancelled.

## Limits and errors

- Quantity: 1–10,000 per command. On-hand stock: at most 1,000,000 units through receipt commands.
- SKU: 1–40 characters, normalized to uppercase, unique per company.
- Name/customer: 1–100 characters. Receipt reference: 1–200 characters.
- Price: 0–100,000,000 cents. Reorder point: 0–1,000,000 units.
- `400`: invalid payload/business value or missing/invalid CSRF token.
- `401`: not signed in.
- `403`: role cannot perform the action.
- `404`: order detail is absent from the current company.
- `409`: insufficient stock, duplicate SKU, reused request key with a different payload, or invalid order state.

Business and CSRF errors use Problem Details with a `title`. Product availability conflicts intentionally do not reveal whether another company owns a supplied ID.
