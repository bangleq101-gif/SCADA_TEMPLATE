using System.IO;
using Scada.App.Services;
using Scada.App.ViewModels;
using Scada.Core.Configuration;
using Scada.Core.MachineSettings;
using Scada.Core.Tags;
using Scada.Infrastructure.Persistence;
using Xunit;

namespace Scada.App.Tests;

public sealed class MachineSettingsTests
{
    [Fact]
    public void PageApplyIsTransactionalWhenAnyEditorIsInvalid()
    {
        var options = Options();
        var session = new ProjectEditSession(options, null, null);
        var viewModel = new MachineSettingsViewModel(session, new TestTagCache(), new ImmediateDispatcher());
        var page = Assert.Single(viewModel.Pages);
        page.Editors[0].EditValueText = "12";
        page.Editors[1].EditValueText = "not-a-number";

        Assert.False(page.Apply());
        Assert.Equal("10", session.WorkingProject.MachineSettings.Pages[0].Parameters[0].Value);
        Assert.Equal("20", session.WorkingProject.MachineSettings.Pages[0].Parameters[1].Value);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void ActivePageDeduplicatesLiveTagSubscriptionsAndDisposesOnDeactivate()
    {
        var cache = new TestTagCache();
        var options = Options();
        options.Tags = [new TagDefinition { Id = "live", Name = "Live", DeviceId = "SIM", Address = "X" }];
        options.MachineSettings.Pages[0].Parameters[0].LiveTagId = "live";
        options.MachineSettings.Pages[0].Parameters[1].LiveTagId = "live";
        var viewModel = new MachineSettingsViewModel(new ProjectEditSession(options, null, null), cache, new ImmediateDispatcher());

        viewModel.Activate();
        Assert.Equal(1, cache.ActiveSubscriptionCount);
        cache.Publish(new TagValue("live", 42L, TagQuality.Good, DateTimeOffset.UtcNow, 1));
        Assert.Equal(42L, viewModel.SelectedPage!.Editors[0].LiveValue);
        viewModel.Deactivate();
        Assert.Equal(0, cache.ActiveSubscriptionCount);
    }

    [Fact]
    public void UnresolvedLiveTagHasWarningAndNoSubscription()
    {
        var options = Options();
        options.MachineSettings.Pages[0].Parameters[0].LiveTagId = "missing";
        Assert.Contains(RuntimeOptionsValidation.CollectIssues(options), issue => issue.Code == "MACHINE_PARAMETER_LIVE_TAG_UNRESOLVED" && !issue.IsBlocking);
        var cache = new TestTagCache(); var viewModel = new MachineSettingsViewModel(new ProjectEditSession(options, null, null), cache, new ImmediateDispatcher());
        viewModel.Activate();
        Assert.Equal(0, cache.ActiveSubscriptionCount);
    }

    [Fact]
    public void FiveHundredParametersOnlySubscribeForActivePageDistinctTags()
    {
        var options = Options();
        options.Tags = Enumerable.Range(0, 4).Select(i => new TagDefinition { Id = $"T{i}", Name = $"T{i}", DeviceId = "SIM", Address = $"A{i}" }).ToList();
        options.MachineSettings.Pages[0].Parameters = Enumerable.Range(0, 500).Select(i => new MachineParameterDefinition { Id = $"P{i}", Name = $"P{i}", ValueType = MachineParameterValueType.Integer, Value = "1", LiveTagId = $"T{i % 4}" }).ToList();
        var cache = new TestTagCache(); var viewModel = new MachineSettingsViewModel(new ProjectEditSession(options, null, null), cache, new ImmediateDispatcher());
        viewModel.Activate();
        Assert.Equal(4, cache.ActiveSubscriptionCount);
        viewModel.Dispose();
        Assert.Equal(0, cache.ActiveSubscriptionCount);
    }

    [Fact]
    public void BooleanEditorChangesOnlyItsDraftUntilTransactionalApply()
    {
        var options = Options();
        var parameter = options.MachineSettings.Pages[0].Parameters[0];
        parameter.ValueType = MachineParameterValueType.Boolean;
        parameter.Value = "false";
        var session = new ProjectEditSession(options, null, null);
        var viewModel = new MachineSettingsViewModel(session, new TestTagCache(), new ImmediateDispatcher());
        var editor = viewModel.SelectedPage!.Editors[0];

        editor.BooleanValue = true;

        Assert.True(editor.BooleanValue);
        Assert.Equal("false", session.WorkingProject.MachineSettings.Pages[0].Parameters[0].Value);
        Assert.False(session.IsDirty);
        Assert.True(viewModel.SelectedPage.Apply());
        Assert.Equal("true", session.WorkingProject.MachineSettings.Pages[0].Parameters[0].Value);
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void SubscriptionAcquisitionDeactivationLeavesNoOwnedSubscription()
    {
        var options = LiveOptions(); var cache = new TestTagCache();
        var page = new MachineSettingsPageViewModel(options.MachineSettings.Pages[0], new ProjectEditSession(options, null, null), cache, new ImmediateDispatcher());
        cache.SubscribeHook = page.Deactivate;

        page.Activate();

        Assert.Equal(0, cache.ActiveSubscriptionCount);
        page.Dispose();
    }

    [Fact]
    public void SubscriptionAcquisitionDisposeLeavesNoOwnedSubscription()
    {
        var options = LiveOptions(); var cache = new TestTagCache();
        var page = new MachineSettingsPageViewModel(options.MachineSettings.Pages[0], new ProjectEditSession(options, null, null), cache, new ImmediateDispatcher());
        cache.SubscribeHook = page.Dispose;

        page.Activate();

        Assert.Equal(0, cache.ActiveSubscriptionCount);
    }

    [Fact]
    public void StaleQueuedCallbackCannotUpdateAnInactivePage()
    {
        var options = LiveOptions(); var cache = new TestTagCache(); var dispatcher = new QueuedDispatcher();
        var page = new MachineSettingsPageViewModel(options.MachineSettings.Pages[0], new ProjectEditSession(options, null, null), cache, dispatcher);
        page.Activate();
        cache.Publish(new TagValue("live", 7L, TagQuality.Good, DateTimeOffset.UnixEpoch, 1));
        page.Deactivate();

        dispatcher.Drain();

        Assert.Null(page.Editors[0].LiveValue);
        Assert.Equal(0, cache.ActiveSubscriptionCount);
    }

    [Fact]
    public void PageAtoBtoAAndReplacementKeepSubscriptionsBounded()
    {
        var options = LiveOptions();
        options.MachineSettings.Pages.Add(new MachineSettingsPageDefinition { Id = "b", Title = "B", Parameters = [new MachineParameterDefinition { Id = "b1", Name = "B1", ValueType = MachineParameterValueType.Integer, Value = "1", LiveTagId = "live" }] });
        var cache = new TestTagCache(); var session = new ProjectEditSession(options, null, null); var viewModel = new MachineSettingsViewModel(session, cache, new ImmediateDispatcher());
        viewModel.Activate();
        var pageA = viewModel.SelectedPage!;
        viewModel.SelectedPage = viewModel.Pages.Single(page => page.Id == "b");
        Assert.Equal(1, cache.ActiveSubscriptionCount);
        viewModel.SelectedPage = pageA;
        Assert.Equal(1, cache.ActiveSubscriptionCount);

        session.ReplaceWorkingProject(new RuntimeOptions { Tags = options.Tags, MachineSettings = new MachineSettingsOptions { Pages = [options.MachineSettings.Pages[1]] } });

        Assert.Equal(1, cache.ActiveSubscriptionCount);
        viewModel.Dispose();
        Assert.Equal(0, cache.ActiveSubscriptionCount);
    }

    [Fact]
    public void DisabledLiveTagIsWarnedAndNeverSubscribed()
    {
        var options = LiveOptions(); options.Tags[0].Enabled = false;
        var cache = new TestTagCache(); var viewModel = new MachineSettingsViewModel(new ProjectEditSession(options, null, null), cache, new ImmediateDispatcher());

        viewModel.Activate();

        Assert.Contains(RuntimeOptionsValidation.CollectIssues(options), issue => issue.Code == "MACHINE_PARAMETER_LIVE_TAG_UNRESOLVED" && !issue.IsBlocking);
        Assert.Equal(0, cache.ActiveSubscriptionCount);
    }

    [Fact]
    public void BooleanRevertRaisesBooleanValueAndRestoresPersistedDraft()
    {
        var definition = new MachineParameterDefinition { Id = "enabled", Name = "Enabled", ValueType = MachineParameterValueType.Boolean, Value = "false" };
        var editor = new ParameterEditorViewModel(definition);
        var notified = false;
        editor.PropertyChanged += (_, args) => notified |= args.PropertyName == nameof(ParameterEditorViewModel.BooleanValue);
        editor.BooleanValue = true;

        editor.ResetDraft();

        Assert.False(editor.BooleanValue);
        Assert.True(notified);
    }

    [Fact]
    public void PageGroupsAreDeterministicAndHiddenInvalidPagesCanBeExposed()
    {
        var options = Options();
        options.MachineSettings.Pages[0].Group = "Drive";
        options.MachineSettings.Pages.Add(new MachineSettingsPageDefinition { Id = "hidden", Title = "Hidden", Group = "Safety", IsVisible = false, Parameters = [new MachineParameterDefinition { Id = "bad", Name = "Bad", ValueType = MachineParameterValueType.Integer, Value = "invalid" }] });
        var viewModel = new MachineSettingsViewModel(new ProjectEditSession(options, null, null), new TestTagCache(), new ImmediateDispatcher());

        Assert.Single(viewModel.PageGroups);
        Assert.Contains(viewModel.ValidationIssues, issue => issue.IsBlocking && issue.ObjectId == "hidden/bad");
        viewModel.ShowHiddenConfiguration = true;

        Assert.Equal(["Drive", "Safety"], viewModel.PageGroups.Select(group => group.Name));
        Assert.Contains(viewModel.Pages, page => page.Id == "hidden");
    }

    [Fact]
    public void VisibilityRebuildPreservesUnappliedDrafts()
    {
        var options = Options();
        options.MachineSettings.Pages.Add(new MachineSettingsPageDefinition { Id = "hidden", Title = "Hidden", IsVisible = false });
        var viewModel = new MachineSettingsViewModel(new ProjectEditSession(options, null, null), new TestTagCache(), new ImmediateDispatcher());
        viewModel.SelectedPage!.Editors[0].EditValueText = "77";
        viewModel.ShowHiddenConfiguration = true;
        Assert.Equal("77", viewModel.SelectedPage!.Editors[0].EditValueText);
    }

    [Fact]
    public void SuccessfulProjectSavePreservesUnappliedDrafts()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"scada-m9-save-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = new ProjectPath(Path.Combine(directory, "project.json"));
            var session = new ProjectEditSession(Options(), path, new ProjectConfigurationStore(path));
            var viewModel = new MachineSettingsViewModel(session, new TestTagCache(), new ImmediateDispatcher());
            viewModel.SelectedPage!.Editors[0].EditValueText = "77";

            viewModel.SaveProjectCommand.Execute(null);

            Assert.True(File.Exists(path.FullPath));
            Assert.Equal("77", viewModel.SelectedPage!.Editors[0].EditValueText);
            Assert.Equal("10", session.SavedProject.MachineSettings.Pages[0].Parameters[0].Value);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RevertSavedDiscardsUnappliedDraftsAndRebuildsFromSavedProject()
    {
        var viewModel = new MachineSettingsViewModel(
            new ProjectEditSession(Options(), null, null),
            new TestTagCache(),
            new ImmediateDispatcher());
        viewModel.SelectedPage!.Editors[0].EditValueText = "77";

        viewModel.RevertProjectCommand.Execute(null);

        Assert.Equal("10", viewModel.SelectedPage!.Editors[0].EditValueText);
        Assert.Equal("10", viewModel.SelectedPage.Editors[0].Definition.Value);
    }

    [Fact]
    public void PagePresentationRowsFlattenGroupsAndEditorsForOneVirtualizationOwner()
    {
        var options = Options();
        options.MachineSettings.Pages[0].Parameters[0].Group = "Drive";
        options.MachineSettings.Pages[0].Parameters[1].Group = "Safety";
        var viewModel = new MachineSettingsViewModel(
            new ProjectEditSession(options, null, null),
            new TestTagCache(),
            new ImmediateDispatcher());

        var rows = viewModel.SelectedPage!.PresentationRows;

        Assert.Collection(
            rows,
            row => Assert.Equal("Drive", Assert.IsType<ParameterGroupViewModel>(row).Name),
            row => Assert.Equal("one", Assert.IsType<ParameterEditorViewModel>(row).Id),
            row => Assert.Equal("Safety", Assert.IsType<ParameterGroupViewModel>(row).Name),
            row => Assert.Equal("two", Assert.IsType<ParameterEditorViewModel>(row).Id));
    }

    private static RuntimeOptions Options() => new() { MachineSettings = new MachineSettingsOptions { Pages = [new MachineSettingsPageDefinition { Id = "machine", Title = "Machine", Parameters = [new MachineParameterDefinition { Id = "one", Name = "One", ValueType = MachineParameterValueType.Integer, Value = "10" }, new MachineParameterDefinition { Id = "two", Name = "Two", ValueType = MachineParameterValueType.Integer, Value = "20" }] }] } };
    private static RuntimeOptions LiveOptions() => new() { Tags = [new TagDefinition { Id = "live", Name = "Live", DeviceId = "SIM", Address = "X", Enabled = true }], MachineSettings = new MachineSettingsOptions { Pages = [new MachineSettingsPageDefinition { Id = "a", Title = "A", Parameters = [new MachineParameterDefinition { Id = "a1", Name = "A1", ValueType = MachineParameterValueType.Integer, Value = "1", LiveTagId = "live" }] }] } };
    private sealed class ImmediateDispatcher : IMachineSettingsDispatcher { public void Post(Action action) => action(); }
    private sealed class QueuedDispatcher : IMachineSettingsDispatcher { private readonly Queue<Action> _actions = []; public void Post(Action action) => _actions.Enqueue(action); public void Drain() { while (_actions.TryDequeue(out var action)) action(); } }
}
