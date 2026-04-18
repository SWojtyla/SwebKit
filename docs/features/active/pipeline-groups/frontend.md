# Frontend — Pipeline Groups

## Goal

Add a "Groups" tab to the Pipelines page that lets users create, edit, delete, and trigger groups of pipelines.

## New components

| Component                    | Location                                                | Responsibility                                                                                                             |
| ---------------------------- | ------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------- |
| `PipelineGroupList`          | `Components/Pipelines/PipelineGroupList.razor`          | Scrollable list of group cards; "New Group" button                                                                         |
| `PipelineGroupEditor`        | `Components/Pipelines/PipelineGroupEditor.razor`        | Inline right-panel: rename group, add/remove pipelines from any pinned project                                             |
| `PipelineGroupTriggerDialog` | `Components/Pipelines/PipelineGroupTriggerDialog.razor` | Modal: per-pipeline branch select (defaults to last-used branch), "Run All" button, real-time per-pipeline result feedback |

## PipelinesPage changes

- Add `"groups"` to the tab bar between `"activity"` and `"releases"`:
  ```razor
  <button class="pill-tab ..." @onclick="@(() => SetTab("groups"))">Groups</button>
  ```
- Add the tab content case:
  ```razor
  case "groups":
      <div class="pipeline-split">
          <div class="pipeline-split__left">
              <PipelineGroupList Groups="_devOpsConfig.PipelineGroups"
                                 SelectedGroup="_selectedGroup"
                                 OnSelect="OnGroupSelected"
                                 OnCreate="CreateGroup"
                                 OnDelete="DeleteGroup" />
          </div>
          <div class="pipeline-split__right">
              @if (_selectedGroup is not null)
              {
                  <PipelineGroupEditor Group="_selectedGroup"
                                       PinnedProjects="EffectivePinnedProjects"
                                       OnSaved="SaveGroups" />
              }
          </div>
      </div>
      break;
  ```
- Load `DevOpsConfig` in `PipelinesPage` (already needed for group persistence):
  ```csharp
  @inject DevOpsConfigRepository DevOpsConfigRepo
  private DevOpsConfig _devOpsConfig = new();
  ```

## PipelineGroupTriggerDialog behaviour

1. User clicks "Trigger Group" on a group card.
2. Dialog opens showing each pipeline entry with:
   - Pipeline name + project badge
   - Branch selector (populated from `GetBranchesAsync`; pre-selected to last-used branch or `main`)
   - Status column (idle initially; spinner while running; ✓ succeeded / ✗ failed after result)
3. "Run All" fires `TriggerPipelineRunAsync` for each entry sequentially with 200 ms delay between calls.
4. Each pipeline row updates its status indicator as the trigger completes (StateHasChanged after each).
5. Errors per pipeline are shown inline; overall success shown via Notifications.

## Stale pipeline warning

In `PipelineGroupEditor`, after loading pipelines for each pinned project, mark any `PipelineGroupEntry` whose `PipelineId` is not found in the loaded pipeline list with a `⚠ not found` badge. This is advisory — the entry remains in the group.

## Persistence pattern

- After any create/edit/delete operation on groups, call `DevOpsConfigRepository.SaveAsync(config)`.
- No new repository or service abstraction needed.

## CSS

Add scoped styles to `PipelineGroupList.razor.css`, `PipelineGroupEditor.razor.css`, and `PipelineGroupTriggerDialog.razor.css`. Follow existing pipeline-tree and pipeline-detail CSS patterns for variable names and spacing.
