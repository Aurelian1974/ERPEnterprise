namespace Erp.Setup.Tests.Sql;

/// <summary>Containerul SQL comun (Erp.Testing.Sql), ca fixture de colectie al acestui assembly.</summary>
public sealed class SqlServerFixture : SqlServerTestServer, IAsyncLifetime
{
    public ValueTask InitializeAsync() => new(StartAsync());

    public ValueTask DisposeAsync() => StopAsync();
}

/// <summary>Un singur container SQL pentru toate clasele de integrare.</summary>
[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServer";
}
