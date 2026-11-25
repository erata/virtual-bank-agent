using System.Data.Common;

namespace VirtualBank.Infrastructure.Persistence;

/// <summary>
/// customer_db, reached with the least-privilege login customer_api. One module, one database, one DB role:
/// the Credit module cannot even connect here (CONNECT is revoked).
/// </summary>
public sealed record CustomerDatabase(DbDataSource DataSource);

/// <summary>credit_db, reached with the least-privilege login credit_api.</summary>
public sealed record CreditDatabase(DbDataSource DataSource);
