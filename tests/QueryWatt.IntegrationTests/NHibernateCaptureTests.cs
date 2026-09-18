using NHibernate;
using NHibernate.Cfg;
using NHibernate.Dialect;
using NHibernate.Driver;
using NHibernate.Linq;
using NHibernate.Mapping.ByCode;
using NHibernate.Mapping.ByCode.Conformist;
using Xunit;

namespace QueryWatt.IntegrationTests;

[Collection(SqlServerCollection.Name)]
public sealed class NHibernateCaptureTests(SqlServerFixture fixture) : IDisposable
{
    private ISessionFactory? _sessionFactory;

    public void Dispose() => _sessionFactory?.Dispose();

    [Fact(Skip = "Needs the QueryWatt connection wrapper (PR #9b): this stack closes the reader without advancing past the last result set, so SqlClient discards the STATISTICS IO/TIME messages before QueryWatt can read them.")]
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

    [Fact(Skip = "Needs the QueryWatt connection wrapper (PR #9b): this stack closes the reader without advancing past the last result set, so SqlClient discards the STATISTICS IO/TIME messages before QueryWatt can read them.")]
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
        var configuration = new Configuration();

        configuration.DataBaseIntegration(database =>
        {
            database.ConnectionString = fixture.ConnectionString;
            database.Driver<MicrosoftDataSqlClientDriver>();
            database.Dialect<MsSql2012Dialect>();
            database.LogSqlInConsole = false;
        });

        var mapper = new ModelMapper();
        mapper.AddMapping<ProductMapping>();
        configuration.AddMapping(
            mapper.CompileMappingForAllExplicitlyAddedEntities());

        return configuration.BuildSessionFactory();
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
