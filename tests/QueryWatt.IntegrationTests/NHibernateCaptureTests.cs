using System.Data.Common;
using NHibernate;
using NHibernate.Cfg;
using NHibernate.Dialect;
using NHibernate.Driver;
using NHibernate.Linq;
using NHibernate.Mapping.ByCode;
using NHibernate.Mapping.ByCode.Conformist;
using QueryWatt.SqlServer.Wrapping;
using Xunit;

namespace QueryWatt.IntegrationTests;

[Collection(SqlServerCollection.Name)]
public sealed class NHibernateCaptureTests(SqlServerFixture fixture) : IDisposable
{
    private ISessionFactory? _sessionFactory;

    public void Dispose() => _sessionFactory?.Dispose();

    [Fact]
    public void AnNHibernateLinqQueryComesBackUnchangedAndFullyMeasured()
    {
        var reference = fixture.WithoutMeasurement(QueryProducts);
        var measured = fixture.Measure("catalog.product-search.nhibernate", QueryProducts);

        Assert.Equal(20, reference.Count);
        Assert.Equal(reference.Count, measured.Result.Count);
        Assert.Equal(reference[0].Name, measured.Result[0].Name);

        var command = MeasurementAssertions.SingleFullyMeasuredCommand(measured.Record);

        Assert.Equal(20, command.RowsReturned);
    }

    [Fact]
    public void AnNHibernateHqlQueryIsMeasured()
    {
        // Building the session factory runs NHibernate's own metadata queries; that is
        // application startup work, not the query under measurement.
        SessionFactory();

        var measured = fixture.Measure("catalog.product-count.nhibernate", () =>
        {
            using var session = SessionFactory().OpenSession();

            return session
                .CreateQuery("select count(*) from Product p where p.IsActive = :active")
                .SetParameter("active", true)
                .UniqueResult<long>();
        });

        Assert.Equal(TestSchema.ProductCount, measured.Result);

        MeasurementAssertions.SingleFullyMeasuredCommand(measured.Record);
    }

    private List<Product> QueryProducts()
    {
        using var session = SessionFactory().OpenSession();

        return session.Query<Product>()
            .Where(product => product.IsActive)
            .OrderBy(product => product.Name)
            .Take(20)
            .ToList();
    }

    private ISessionFactory SessionFactory() => _sessionFactory ??= BuildSessionFactory();

    private ISessionFactory BuildSessionFactory()
    {
        var configuration = new NHibernate.Cfg.Configuration();

        configuration.DataBaseIntegration(database =>
        {
            database.ConnectionString = fixture.ConnectionString;
            database.Driver<QueryWattSqlClientDriver>();
            database.Dialect<MsSql2012Dialect>();
            database.LogSqlInConsole = false;
        });

        var mapper = new ModelMapper();
        mapper.AddMapping<ProductMapping>();
        configuration.AddMapping(
            mapper.CompileMappingForAllExplicitlyAddedEntities());

        return configuration.BuildSessionFactory();
    }

    /// <summary>
    /// NHibernate builds its commands from the driver rather than from the connection, so both
    /// halves are wrapped here. This is the NHibernate equivalent of the one line an application
    /// adds elsewhere.
    /// </summary>
    public sealed class QueryWattSqlClientDriver : MicrosoftDataSqlClientDriver
    {
        public override DbConnection CreateConnection() =>
            QueryWattConnection.Wrap(base.CreateConnection());

        public override DbCommand CreateCommand() =>
            QueryWattCommand.Wrap(base.CreateCommand());
    }

    public class Product
    {
        public virtual int ProductId { get; set; }

        public virtual string Sku { get; set; } = string.Empty;

        public virtual string Name { get; set; } = string.Empty;

        public virtual decimal ListPrice { get; set; }

        public virtual bool IsActive { get; set; }
    }

    private sealed class ProductMapping : ClassMapping<Product>
    {
        public ProductMapping()
        {
            Table("Product");
            EntityName("Product");
            Schema("dbo");
            Lazy(false);

            Id(product => product.ProductId, map => map.Generator(Generators.Identity));
            Property(product => product.Sku);
            Property(product => product.Name);
            Property(product => product.ListPrice);
            Property(product => product.IsActive);
        }
    }
}
