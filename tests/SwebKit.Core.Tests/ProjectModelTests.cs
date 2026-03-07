using SwebKit.Core.Domain;

namespace SwebKit.Core.Tests;

public class ProjectModelTests
{
    [Fact]
    public void ProjectEnvironment_IsProduction_TrueForProductionTier()
    {
        var env = new ProjectEnvironment
        {
            ProjectId = Guid.NewGuid(),
            Name = "Prod",
            Tier = EnvironmentTier.Production
        };

        Assert.True(env.IsProduction);
    }

    [Fact]
    public void Project_DefaultIconColor_IsSet()
    {
        var project = new Project { Name = "Orders" };

        Assert.False(string.IsNullOrWhiteSpace(project.IconColor));
    }
}
