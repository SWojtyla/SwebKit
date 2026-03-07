using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Core.Services;

public class AppStateService
{
    private readonly ProfileRepository _profiles;
    private readonly UiStateRepository _uiState;
    private readonly IAppEventBus _events;

    public AppStateService(ProfileRepository profiles, UiStateRepository uiState, IAppEventBus events)
    {
        _profiles = profiles;
        _uiState = uiState;
        _events = events;
    }

    public Project? CurrentProject { get; private set; }
    public ProjectEnvironment? CurrentEnvironment { get; private set; }

    public IReadOnlyList<Project> AllProjects => _profiles.Projects;

    public bool IsProduction => CurrentEnvironment?.IsProduction ?? false;

    public async Task InitializeAsync()
    {
        await _profiles.LoadAsync();
        await _uiState.LoadAsync();

        var state = _uiState.State;
        if (state.LastProjectId.HasValue)
        {
            var project = _profiles.FindProject(state.LastProjectId.Value);
            if (project is not null)
            {
                CurrentProject = project;
                if (state.LastEnvironmentId.HasValue)
                    CurrentEnvironment = project.Environments.FirstOrDefault(e => e.Id == state.LastEnvironmentId.Value)
                                         ?? project.Environments.FirstOrDefault();
                else
                    CurrentEnvironment = project.Environments.FirstOrDefault();
            }
        }

        if (CurrentProject is null && _profiles.Projects.Count > 0)
        {
            CurrentProject = _profiles.Projects[0];
            CurrentEnvironment = CurrentProject.Environments.FirstOrDefault();
        }
    }

    public async Task SelectProjectAsync(Guid projectId)
    {
        var project = _profiles.FindProject(projectId);
        if (project is null) return;

        CurrentProject = project;
        CurrentEnvironment = project.Environments.FirstOrDefault();
        _uiState.State.LastProjectId = projectId;
        _uiState.State.LastEnvironmentId = CurrentEnvironment?.Id;

        _events.Publish(new ProjectChangedEvent(projectId));
        if (CurrentEnvironment is not null)
            _events.Publish(new EnvironmentChangedEvent(projectId, CurrentEnvironment.Id));

        await _uiState.SaveAsync();
    }

    public async Task SelectEnvironmentAsync(Guid environmentId)
    {
        if (CurrentProject is null) return;
        var env = CurrentProject.Environments.FirstOrDefault(e => e.Id == environmentId);
        if (env is null) return;

        CurrentEnvironment = env;
        _uiState.State.LastEnvironmentId = environmentId;

        _events.Publish(new EnvironmentChangedEvent(CurrentProject.Id, environmentId));

        await _uiState.SaveAsync();
    }

    public async Task AddProjectAsync(Project project)
    {
        _profiles.AddProject(project);
        await _profiles.SaveAsync();
        if (CurrentProject is null) await SelectProjectAsync(project.Id);
    }

    public async Task UpdateProjectAsync(Project project)
    {
        _profiles.UpdateProject(project);
        if (CurrentProject?.Id == project.Id) CurrentProject = project;
        await _profiles.SaveAsync();
    }

    public async Task DeleteProjectAsync(Guid projectId)
    {
        _profiles.DeleteProject(projectId);
        if (CurrentProject?.Id == projectId)
        {
            CurrentProject = _profiles.Projects.FirstOrDefault();
            CurrentEnvironment = CurrentProject?.Environments.FirstOrDefault();
        }
        await _profiles.SaveAsync();
    }
}
