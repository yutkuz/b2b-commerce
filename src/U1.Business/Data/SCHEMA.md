# Database schema ownership

U1 Business uses **SQL-first schema migrations with EF Core as the runtime ORM**.

## Source of truth

- `Data/001-schema.sql` defines a clean database.
- Later numbered SQL files contain forward-only upgrades for existing databases.
- `SchemaVersions` records the applied schema version.
- `Database.Initialize` serializes schema upgrades with `sp_getapplock` and applies them transactionally.
- `BusinessDbContext` must mirror the SQL schema for runtime mapping, indexes and relationships, but it does **not** own schema creation.

Do not create or apply EF Core migrations for this project unless the repository is intentionally converted away from the SQL-first strategy. Mixing `dotnet ef database update` with the existing `SchemaVersions` mechanism would create two competing migration histories.

## Adding a schema change

1. Add the next forward-only SQL upgrade under `Data/`.
2. Update the schema-version handling in `Database.Initialize`.
3. Update `BusinessDbContext` when columns, indexes, keys or relationships change.
4. Preserve existing data; avoid destructive migration steps unless a separately reviewed data migration exists.
5. Add or update integration tests that start from a clean LocalDB and exercise the changed behavior.
6. Let GitHub Actions validate the application build, API integration suite and browser flow before merging.

SQL Server-specific locking used by checkout (`UPDLOCK`, `HOLDLOCK`, `sp_getapplock`) is intentionally retained through EF Core raw SQL / ADO.NET where EF LINQ has no equivalent.
