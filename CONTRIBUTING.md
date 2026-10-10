# Contributing

Start from a repository issue with a clear problem, acceptance criteria, and validation plan. Use `feat/`, `fix/`, `test/`, or `docs/` branches and descriptive commits. Keep each PR focused on one reviewable change.

```sh
dotnet restore --locked-mode
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
```

For dependency updates, regenerate the lock files intentionally, review them, and run `dotnet list package --vulnerable --include-transitive`.

Business changes should test the failure case as well as success. Tenant-owned queries and writes must preserve tenant isolation. A new stock command must preserve `0 <= Reserved <= OnHand` and record its movement and audit entry transactionally. New API writes require role authorization and antiforgery validation. Include screenshots for UI changes and keep documentation aligned with actual behavior.

Link the issue using `Closes #N`, describe the result, and record how it was verified. Wait for CI before merging. Do not include databases, credentials, generated binaries, or personal data in commits. Development demo credentials are intentionally public and must never grant access to real data.
