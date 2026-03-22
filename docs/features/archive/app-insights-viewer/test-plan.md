# Test Plan — Application Insights Viewer

## Strategy

- Unit tests for all query-building logic and model mapping in `SwebKit.Observability.Tests`
- Integration tests (opt-in, require real Azure credentials) marked `[Category("Integration")]`
- Component tests (bUnit) for filter, time range, and tab switching behavior
- Manual verification for charts, Monaco editor, and detail panes

---

## Unit Test Scenarios

### Resource Discovery
- [ ] `ListResourcesAsync` yields one resource per App Insights component found
- [ ] Empty subscription list returns no resources
- [ ] Exception during one subscription scan does not abort the rest (graceful skip + log)

### Query Building
- [ ] `GetTopExceptionsAsync` generates valid KQL with correct time filter
- [ ] `RunKqlAsync` passes time range correctly to `LogsQueryClient`
- [ ] Truncation flag is set when result rows == `MaxRowsPerQuery`
- [ ] `KqlPresets` substitutes time range placeholder correctly for each preset

### Model Mapping
- [ ] `LogsQueryResult` maps column names and all primitive types correctly
- [ ] `OverviewMetrics` aggregates failure rate from request count and failed count correctly
- [ ] `OperationPerformance` maps P50/P95/P99 percentile columns correctly

---

## Component Test Scenarios (bUnit)

### Resource Selector
- [ ] Shows loading indicator while `ListResourcesAsync` is in progress
- [ ] Groups resources correctly by subscription name
- [ ] Filter box narrows results by name, subscription, and resource group
- [ ] Selecting a resource calls `OnResourceSelected` and closes the dialog

### Time Range Picker
- [ ] Each preset option updates `SelectedRange` with correct start/end offsets
- [ ] Custom range enforces start < end (end before start shows validation message)

### Logs Tab
- [ ] Clicking a preset populates the Monaco editor content and fires query
- [ ] Truncation warning banner appears when `LogQueryResult.Truncated == true`
- [ ] "Copy CSV" writes correctly formatted CSV to clipboard for multi-column results
- [ ] Saved query persists to `AppInsightsConfig.SavedQueries` and re-appears on reload

### Failures Tab
- [ ] Clicking an exception row triggers detail pane load with correct exception type
- [ ] "View in Logs tab" switches to Logs tab and pre-populates KQL

---

## Integration Test Scenarios

> These require `AZURE_SUBSCRIPTION_ID` environment variable and valid `DefaultAzureCredential`.

- [ ] `AppInsightsDiscoveryService` returns at least one resource from the configured subscription
- [ ] `AzureAppInsightsClient.GetTopExceptionsAsync` returns results or an empty list (no exception) for a valid resource
- [ ] `AzureAppInsightsClient.RunKqlAsync` with a simple `requests | count` query returns one row

---

## Manual Verification Checklist

### Overview Tab
- [ ] Summary cards show correct values matching a reference Azure Portal query
- [ ] Trend charts render correctly and tooltip shows on hover
- [ ] Failure rate card changes color correctly (green / amber / red)

### Failures Tab
- [ ] Stack trace is readable and complete for real exceptions
- [ ] "Copy Stack Trace" places plain text on clipboard

### Performance Tab
- [ ] P95 bar proportions are visually sensible across different value ranges
- [ ] Color thresholds apply correctly (green / amber / red)
- [ ] Clicking row opens detail pane with trend chart

### Logs Tab
- [ ] Monaco editor has KQL syntax highlighting
- [ ] `Ctrl+Enter` triggers query execution
- [ ] Large result sets (close to 500 rows) display correctly without UI freeze

### Availability Tab
- [ ] Heatmap renders with correct colors for pass/fail cells
- [ ] Failure cell click opens detail pane

### Resource Switching
- [ ] Switching resources reloads all tabs with new data
- [ ] Previously selected resource is remembered across restarts

---

## Acceptance Criteria

- All unit and component tests pass
- Overview, Failures, and Logs tabs return correct data verified against Azure Portal for a real App Insights resource
- No UI freezes on result sets up to 500 rows
- Keyboard shortcuts work as documented
- `profiles.json` persists selected resource ID and saved queries correctly
