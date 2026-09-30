using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EEMOCantilanSDS.Testing;

public sealed class CollectionSourceShapeModelTests
{
    [Fact]
    public void DailyCollectionMeatWeighingIsRepresentableInPostedAndDraftRelationalModels()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;
        using var context = new AppDbContext(options);
        var model = context.GetService<IDesignTimeModel>().Model;

        AssertAllowsMeatWeighing(model, typeof(CollectionLine), "CK_CollectionLines_SourceShape");
        AssertAllowsMeatWeighing(model, typeof(CollectionAllocation), "CK_CollectionAllocations_SourceShape");
        AssertAllowsMeatWeighing(model, typeof(WebCollectionDraftLine), "CK_WebCollectionDraftLines_SourceShape");
        AssertAllowsMeatWeighing(model, typeof(WebCollectionDraftAllocation), "CK_WebCollectionDraftAllocations_SourceShape");
    }

    private static void AssertAllowsMeatWeighing(Microsoft.EntityFrameworkCore.Metadata.IModel model, Type entityType, string constraintName)
    {
        var entity = model.FindEntityType(entityType);
        Assert.NotNull(entity);

        var constraint = Assert.Single(entity!.GetCheckConstraints(), check => check.Name == constraintName);
        Assert.Contains("\"SourceKind\" = 2", constraint.Sql);
        Assert.Contains("\"SourcePart\" IN (3, 4, 5)", constraint.Sql);
    }
}
