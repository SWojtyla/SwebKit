using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public class AppStateServiceTests
{
    [Fact]
    public async Task InitializeAsync_LoadsPersistedProjects()
    {
        using var sandbox = new AppDataSandbox();
        var project = CreateProject("Orders", "Dev", isProd: false);

        var seedProfiles = new ProfileRepository();
        seedProfiles.AddProject(project);
        await seedProfiles.SaveAsync();

        var profiles = new ProfileRepository();
        var ui = new UiStateRepository();
        var sut = new AppStateService(profiles, ui, new AppEventBus());

        await sut.InitializeAsync();

        Assert.Single(sut.AllProjects);
        Assert.Equal(project.Id, sut.AllProjects[0].Id);
    }

    [Fact]
    public async Task AddProjectAsync_AppearsInAllProjects()
    {
        using var sandbox = new AppDataSandbox();
        var sut = await CreateInitializedSutAsync();
        var project = CreateProject("Billing", "Dev", isProd: false);

        await sut.AddProjectAsync(project);

        Assert.Contains(sut.AllProjects, p => p.Id == project.Id);
    }

    [Fact]
    public async Task AddProjectAsync_SelectsNewProject()
    {
        using var sandbox = new AppDataSandbox();
        var existing = CreateProject("Orders", "Dev", isProd: false);
        var sut = await CreateInitializedSutAsync(existing);
        var added = CreateProject("Billing", "Test", isProd: false);

        await sut.AddProjectAsync(added);

        Assert.Equal(added.Id, sut.CurrentProject?.Id);
        Assert.Equal(added.Environments[0].Id, sut.CurrentEnvironment?.Id);
    }

    [Fact]
    public async Task UpdateProjectAsync_ReplacesExistingProject()
    {
        using var sandbox = new AppDataSandbox();
        var existing = CreateProject("Orders", "Dev", isProd: false);
        var sut = await CreateInitializedSutAsync(existing);

        var updated = new Project
        {
            Id = existing.Id,
            Name = "Orders v2",
            Description = "updated",
            IconColor = "#107C10",
            Environments = existing.Environments
        };

        await sut.UpdateProjectAsync(updated);

        var saved = sut.AllProjects.Single(p => p.Id == existing.Id);
        Assert.Equal("Orders v2", saved.Name);
    }

    [Fact]
    public async Task DeleteProjectAsync_RemovesProject()
    {
        using var sandbox = new AppDataSandbox();
        var toDelete = CreateProject("DeleteMe", "Dev", isProd: false);
        var keep = CreateProject("KeepMe", "Dev", isProd: false);
        var sut = await CreateInitializedSutAsync(toDelete, keep);
        await sut.SelectProjectAsync(toDelete.Id);

        await sut.DeleteProjectAsync(toDelete.Id);

        Assert.DoesNotContain(sut.AllProjects, p => p.Id == toDelete.Id);
        Assert.Equal(keep.Id, sut.CurrentProject?.Id);
    }

    [Fact]
    public async Task SelectProjectAsync_SetsCurrentProject()
    {
        using var sandbox = new AppDataSandbox();
        var project = CreateProject("Gateway", "Dev", isProd: false, "Prod", isProd2: true);
        var sut = await CreateInitializedSutAsync(project);

        await sut.SelectProjectAsync(project.Id);

        Assert.Equal(project.Id, sut.CurrentProject?.Id);
        Assert.Equal(project.Environments[0].Id, sut.CurrentEnvironment?.Id);
    }

    [Fact]
    public async Task SelectEnvironmentAsync_SetsCurrentEnvironment()
    {
        using var sandbox = new AppDataSandbox();
        var project = CreateProject("Orders", "Dev", isProd: false, "Prod", isProd2: true);
        var sut = await CreateInitializedSutAsync(project);
        await sut.SelectProjectAsync(project.Id);
        var target = project.Environments[1];

        await sut.SelectEnvironmentAsync(target.Id);

        Assert.Equal(target.Id, sut.CurrentEnvironment?.Id);
    }

    [Fact]
    public async Task IsProduction_TrueWhenCurrentEnvIsProd()
    {
        using var sandbox = new AppDataSandbox();
        var project = CreateProject("Orders", "Dev", isProd: false, "Prod", isProd2: true);
        var sut = await CreateInitializedSutAsync(project);
        await sut.SelectProjectAsync(project.Id);

        await sut.SelectEnvironmentAsync(project.Environments[0].Id);
        Assert.False(sut.IsProduction);

        await sut.SelectEnvironmentAsync(project.Environments[1].Id);
        Assert.True(sut.IsProduction);
    }

    private static async Task<AppStateService> CreateInitializedSutAsync(params Project[] projects)
    {
        var profiles = new ProfileRepository();
        foreach (var project in projects)
        {
            profiles.AddProject(project);
        }

        await profiles.SaveAsync();

        var sut = new AppStateService(new ProfileRepository(), new UiStateRepository(), new AppEventBus());
        await sut.InitializeAsync();
        return sut;
    }

    private static Project CreateProject(string projectName, string env1, bool isProd, string? env2 = null, bool isProd2 = false)
    {
        var project = new Project
        {
            Name = projectName,
            Environments =
            [
                new ProjectEnvironment
                {
                    ProjectId = Guid.NewGuid(),
                    Name = env1,
                    Tier = isProd ? EnvironmentTier.Production : EnvironmentTier.NonProd
                }
            ]
        };

        project.Environments[0].ProjectId = project.Id;

        if (!string.IsNullOrWhiteSpace(env2))
        {
            project.Environments.Add(new ProjectEnvironment
            {
                ProjectId = project.Id,
                Name = env2,
                Tier = isProd2 ? EnvironmentTier.Production : EnvironmentTier.NonProd
            });
        }

        return project;
    }

    private sealed class AppDataSandbox : IDisposable
    {
        private readonly string? _originalAppData;
        private readonly string _tempRoot;

        public AppDataSandbox()
        {
            _originalAppData = Environment.GetEnvironmentVariable("APPDATA");
            _tempRoot = Path.Combine(Path.GetTempPath(), "SwebKit.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            Environment.SetEnvironmentVariable("APPDATA", _tempRoot);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("APPDATA", _originalAppData);
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
    }
}
