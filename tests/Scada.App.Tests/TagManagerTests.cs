using Scada.App.Services;
using Scada.App.ViewModels;
using Scada.Core.Configuration;
using Scada.Core.Devices;
using Scada.Core.Tags;
using Xunit;

namespace Scada.App.Tests;

public sealed class TagManagerTests
{
    [Fact]
    public void EngineeringNavigationExposesOneCanonicalTagManagerRoute()
    {
        var context = CreateContext();

        context.Navigation.Navigate(NavigationService.EngineeringTagManagerRoute);

        Assert.Equal(NavigationService.EngineeringTagManagerRoute, context.Navigation.CurrentRouteKey);
        Assert.Same(context.TagManager, context.Navigation.CurrentViewModel);
        Assert.Equal(2, context.Shell.NavigationItems[3].Children.Count);
        Assert.Equal(
            NavigationService.EngineeringTagManagerRoute,
            context.Shell.NavigationItems[3].Children[1].RouteKey);
    }

    [Fact]
    public void NewUnsavedTagIsNotSubscribedToTagCache()
    {
        var context = CreateContext();
        context.TagManager.Activate();
        context.TagManager.AddCommand.Execute(null);
        var added = context.TagManager.Rows[^1];
        context.TagManager.SetSelection([added]);

        Assert.Equal(0, context.Cache.ActiveSubscriptionCount);
        Assert.Equal(TagQuality.NotConfigured, added.Quality);
        Assert.Equal("Not Loaded", added.RuntimeStatus);
    }

    [Fact]
    public void SelectedExistingTagOwnsAtMostOneSubscriptionAndDeactivationDisposesIt()
    {
        var context = CreateContext();
        context.Cache.Seed(new TagValue("T1", 12.5, TagQuality.Good, DateTimeOffset.UtcNow, 1));
        context.TagManager.Activate();
        context.TagManager.SetSelection([context.TagManager.Rows[0]]);

        Assert.Equal(1, context.Cache.ActiveSubscriptionCount);
        Assert.Equal(12.5, context.TagManager.Rows[0].Value);

        context.TagManager.SetSelection([context.TagManager.Rows[1]]);
        Assert.Equal(1, context.Cache.ActiveSubscriptionCount);
        Assert.Equal(2, context.Cache.TotalSubscriptionCount);

        context.TagManager.Deactivate();
        Assert.Equal(0, context.Cache.ActiveSubscriptionCount);
    }

    [Fact]
    public void StaleSelectedCallbackCannotUpdateLaterSelection()
    {
        var context = CreateContext();
        context.TagManager.Activate();
        context.TagManager.SetSelection([context.TagManager.Rows[0]]);
        context.TagManager.SetSelection([context.TagManager.Rows[1]]);

        context.Cache.InvokeSubscription(
            0,
            new TagValue("T1", "stale", TagQuality.Good, DateTimeOffset.UtcNow, 2));

        Assert.Null(context.TagManager.Rows[0].Value);
        Assert.Same(context.TagManager.Rows[1], context.TagManager.SelectedRow);
    }

    [Fact]
    public void StaleCallbackFromFirstASelectionCannotUpdateReenteredASelection()
    {
        var context = CreateContext();
        context.TagManager.Activate();
        context.TagManager.SetSelection([context.TagManager.Rows[0]]);
        context.TagManager.SetSelection([context.TagManager.Rows[1]]);
        context.TagManager.SetSelection([context.TagManager.Rows[0]]);

        context.Cache.InvokeSubscription(
            0,
            new TagValue("T1", "stale first A", TagQuality.Good, DateTimeOffset.UtcNow, 2));

        Assert.Null(context.TagManager.Rows[0].Value);
        Assert.Same(context.TagManager.Rows[0], context.TagManager.SelectedRow);
        Assert.Equal(1, context.Cache.ActiveSubscriptionCount);
    }

    [Fact]
    public void DeactivateReactivateRejectsCallbackFromPreviousActivation()
    {
        var context = CreateContext();
        context.TagManager.Activate();
        context.TagManager.SetSelection([context.TagManager.Rows[0]]);
        context.TagManager.Deactivate();
        context.TagManager.Activate();

        context.Cache.InvokeSubscription(
            0,
            new TagValue("T1", "stale activation", TagQuality.Good, DateTimeOffset.UtcNow, 3));

        Assert.Null(context.TagManager.Rows[0].Value);
        Assert.Equal(1, context.Cache.ActiveSubscriptionCount);
    }

    [Fact]
    public void EditorOptionsNeverExposeAllAndUnknownReferencesRemainVisible()
    {
        var context = CreateContext();
        context.TagManager.Rows[0].DeviceId = "MISSING_DEVICE";
        context.TagManager.Rows[0].ScanGroup = "MISSING_GROUP";

        Assert.DoesNotContain("All", context.TagManager.DeviceOptions);
        Assert.DoesNotContain("All", context.TagManager.ScanGroupOptions);
        Assert.Contains("MISSING_DEVICE", context.TagManager.Rows[0].DeviceId);
        Assert.Contains("MISSING_GROUP", context.TagManager.Rows[0].ScanGroup);
    }

    [Fact]
    public void WarningOnlyProfileIssueIsNotAnEditorError()
    {
        var context = CreateContext();
        context.TagManager.Rows[0].HistoryProfile = "FutureHistory";

        var row = context.TagManager.Rows[0];
        Assert.False(row.HasErrors);
        Assert.True(row.HasWarnings);
        Assert.Empty(row.GetErrors(null).Cast<string>());
        Assert.Single(row.GetWarnings());
        Assert.Contains("FutureHistory", row.WarningSummary, StringComparison.Ordinal);
        Assert.Equal(2, context.TagManager.ItemsView.Cast<TagEditorRowViewModel>().Count());
        context.TagManager.ValidationOnly = true;
        Assert.Empty(context.TagManager.ItemsView.Cast<TagEditorRowViewModel>());
    }

    [Fact]
    public void DirtyRuntimeConfigurationShowsCurrentRuntimeRestartRequired()
    {
        var context = CreateContext();
        context.Cache.Seed(new TagValue("T1", 12.5, TagQuality.Good, DateTimeOffset.UtcNow, 1));
        context.TagManager.Activate();
        context.TagManager.SetSelection([context.TagManager.Rows[0]]);

        Assert.Equal("Current runtime", context.TagManager.Rows[0].RuntimeStatus);
        context.TagManager.Rows[0].Address = "A99";

        Assert.Equal("Current runtime / restart required", context.TagManager.Rows[0].RuntimeStatus);
        Assert.Equal(12.5, context.TagManager.Rows[0].Value);
    }

    [Fact]
    public void CrudAndBulkEditOperateOnWorkingProjectOnly()
    {
        var context = CreateContext();
        context.TagManager.SetSelection([context.TagManager.Rows[0]]);

        context.TagManager.DuplicateCommand.Execute(null);
        Assert.Equal(3, context.TagManager.Rows.Count);
        Assert.Equal(3, context.Session.WorkingProject.Tags.Count);
        Assert.Equal(2, context.Session.StartupProject.Tags.Count);

        context.TagManager.SetSelection(context.TagManager.Rows.Cast<object>());
        context.TagManager.BulkSetEnabled(false);
        Assert.All(context.Session.WorkingProject.Tags, tag => Assert.False(tag.Enabled));

        context.TagManager.DeleteCommand.Execute(null);
        Assert.Empty(context.Session.WorkingProject.Tags);
        Assert.Equal(2, context.Session.StartupProject.Tags.Count);
    }

    [Fact]
    public void BulkEditAppliesOnlyExplicitFieldsAndPreservesUnchangedFields()
    {
        var context = CreateContext();
        context.TagManager.SetSelection(context.TagManager.Rows.Cast<object>());
        context.TagManager.BulkEdit.DeviceId = BulkEditValue<string>.Explicit("SIM01");
        context.TagManager.BulkEdit.Enabled = BulkEditValue<bool>.Explicit(true);

        var originalNames = context.Session.WorkingProject.Tags.Select(tag => tag.Name).ToArray();
        context.TagManager.ApplyBulkEdit();

        Assert.All(context.Session.WorkingProject.Tags, tag =>
        {
            Assert.Equal("SIM01", tag.DeviceId);
            Assert.True(tag.Enabled);
        });
        Assert.Equal(originalNames, context.Session.WorkingProject.Tags.Select(tag => tag.Name));
        Assert.All(context.Session.WorkingProject.Tags, tag => Assert.Equal(TagDataType.Double, tag.DataType));
    }

    [Fact]
    public void BulkEditRejectsBlockingCandidateBeforeWorkingProjectMutation()
    {
        var context = CreateContext();
        context.TagManager.Rows[0].HistoryProfile = string.Empty;
        context.TagManager.SetSelection([context.TagManager.Rows[0]]);
        context.TagManager.BulkEdit.HistoryEnabled = BulkEditValue<bool>.Explicit(true);

        context.TagManager.ApplyBulkEdit();

        var tag = context.Session.WorkingProject.Tags.Single(tag => tag.Id == "T1");
        Assert.False(tag.HistoryEnabled);
        Assert.Equal(string.Empty, tag.HistoryProfile);
        Assert.Same(context.TagManager.Rows[0], context.TagManager.SelectedRow);
        Assert.Contains("blocked", context.TagManager.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HISTORY_PROFILE_REQUIRED", context.TagManager.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void BulkEditAllowsWarningOnlyCandidate()
    {
        var context = CreateContext();
        context.TagManager.SetSelection([context.TagManager.Rows[0]]);
        context.TagManager.BulkEdit.HistoryProfile = BulkEditValue<string>.Explicit("FutureHistory");

        context.TagManager.ApplyBulkEdit();

        var tag = context.Session.WorkingProject.Tags.Single(tag => tag.Id == "T1");
        Assert.Equal("FutureHistory", tag.HistoryProfile);
        Assert.Contains("applied", context.TagManager.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            context.Session.ValidationIssues,
            issue => issue.Code == "HISTORY_PROFILE_UNKNOWN" && !issue.IsBlocking);
    }

    [Fact]
    public void DeleteCancellationDoesNotMutateWorkingProject()
    {
        var context = CreateContext();
        context.TagManager.SetSelection([context.TagManager.Rows[0]]);
        context.DeleteConfirmation.Confirmed = false;

        context.TagManager.DeleteCommand.Execute(null);

        Assert.Equal(2, context.Session.WorkingProject.Tags.Count);
        Assert.Contains(context.Session.WorkingProject.Tags, tag => tag.Id == "T1");
    }

    [Fact]
    public void ImportConflictDoesNotMutateUntilExplicitResolution()
    {
        var context = CreateContext();
        context.Clipboard.Text = TagClipboardCodec.Export(
        [
            new TagDefinition
            {
                Id = "T1",
                Name = "Conflicting Name",
                DeviceId = "SIM01",
                Address = "A9"
            },
            new TagDefinition
            {
                Id = "T3",
                Name = "New Tag",
                DeviceId = "SIM01",
                Address = "A10"
            }
        ]);
        context.ImportDecision.Decision = TagImportDecision.Cancel;

        context.TagManager.PasteCommand.Execute(null);

        Assert.Equal(2, context.Session.WorkingProject.Tags.Count);
        Assert.NotNull(context.ImportDecision.LastPreparation);
        Assert.True(context.ImportDecision.LastPreparation!.HasConflicts);
        Assert.Contains(context.ImportDecision.LastPreparation.NonConflictingCandidates, candidate => candidate.Definition.Id == "T3");
    }

    [Fact]
    public void ImportCanAppendOnlyNonConflictingRowsAfterExplicitChoice()
    {
        var context = CreateContext();
        context.Clipboard.Text = TagClipboardCodec.Export(
        [
            new TagDefinition
            {
                Id = "T1",
                Name = "Conflicting Name",
                DeviceId = "SIM01",
                Address = "A9"
            },
            new TagDefinition
            {
                Id = "T3",
                Name = "New Tag",
                DeviceId = "SIM01",
                Address = "A10"
            }
        ]);
        context.ImportDecision.Decision = TagImportDecision.AppendNonConflicting;

        context.TagManager.PasteCommand.Execute(null);

        Assert.Equal(3, context.Session.WorkingProject.Tags.Count);
        Assert.Contains(context.Session.WorkingProject.Tags, tag => tag.Id == "T3");
        Assert.DoesNotContain(context.Session.WorkingProject.Tags, tag => tag.Name == "Conflicting Name");
    }

    [Fact]
    public void SearchAndFiltersUseViewWithoutRecreatingRows()
    {
        var context = CreateContext();
        var firstRow = context.TagManager.Rows[0];

        context.TagManager.SearchText = "Run";
        Assert.Single(context.TagManager.ItemsView.Cast<TagEditorRowViewModel>());
        Assert.Same(firstRow, context.TagManager.Rows[0]);

        context.TagManager.SearchText = string.Empty;
        context.TagManager.EnabledFilter = "Disabled";
        Assert.Single(context.TagManager.ItemsView.Cast<TagEditorRowViewModel>());
        Assert.Same(firstRow, context.TagManager.Rows[0]);
    }

    private static TestContext CreateContext()
    {
        var options = new RuntimeOptions
        {
            Devices =
            [
                new DeviceDefinition { Id = "SIM01", Name = "Simulator", DriverType = "Simulator" }
            ],
            Tags =
            [
                new TagDefinition
                {
                    Id = "T1",
                    Name = "Pump Run",
                    DeviceId = "SIM01",
                    Address = "A1",
                    Enabled = true
                },
                new TagDefinition
                {
                    Id = "T2",
                    Name = "Pump Fault",
                    DeviceId = "SIM01",
                    Address = "A2",
                    Enabled = false
                }
            ]
        };
        var cache = new TestTagCache();
        var session = new ProjectEditSession(options, null, null);
        var clipboard = new TestClipboardAdapter();
        var importDecision = new TestImportDecisionService();
        var deleteConfirmation = new TestDeleteConfirmation();
        var tagManager = new TagManagerViewModel(session, cache, clipboard, importDecision, deleteConfirmation);
        var operation = new OperationViewModel(options);
        var machineSettings = new MachineSettingsViewModel();
        var monitoring = new MonitoringViewModel(cache, options);
        var engineering = new EngineeringViewModel();
        var navigation = new NavigationService(operation, machineSettings, monitoring, engineering, tagManager);
        var shell = new ShellViewModel(navigation, options);
        return new TestContext(options, cache, session, tagManager, navigation, shell, clipboard, importDecision, deleteConfirmation);
    }

    private sealed record TestContext(
        RuntimeOptions Options,
        TestTagCache Cache,
        ProjectEditSession Session,
        TagManagerViewModel TagManager,
        NavigationService Navigation,
        ShellViewModel Shell,
        TestClipboardAdapter Clipboard,
        TestImportDecisionService ImportDecision,
        TestDeleteConfirmation DeleteConfirmation);

    private sealed class TestClipboardAdapter : IClipboardAdapter
    {
        public string? Text { get; set; }

        public string? GetText() => Text;

        public void SetText(string text) => Text = text;
    }

    private sealed class TestImportDecisionService : ITagImportDecisionService
    {
        public TagImportDecision Decision { get; set; } = TagImportDecision.ApplyAll;

        public TagImportPreparation? LastPreparation { get; private set; }

        public TagImportDecision Decide(TagImportPreparation preparation, string operation)
        {
            LastPreparation = preparation;
            return Decision;
        }
    }

    private sealed class TestDeleteConfirmation : IDeleteConfirmation
    {
        public bool Confirmed { get; set; } = true;

        public bool ConfirmDelete(int count) => Confirmed;
    }
}
