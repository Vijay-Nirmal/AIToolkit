# AIToolkit.Sql

`AIToolkit.Sql` contains the shared contracts used by database-specific AIToolkit packages.

Reference this package when you want to:

- build a provider-specific SQL package on top of the common abstractions
- reuse the shared provider-neutral `SqlQueryExecutor` with your own connection opener and SQL classifier
- integrate custom stateless connection resolution or approval logic into an AI tool host
- share query result, metadata, and mutation-safety models across providers

Most applications will use a provider package such as `AIToolkit.Sql.SqlServer` directly.