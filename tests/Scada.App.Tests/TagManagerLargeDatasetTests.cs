using System.ComponentModel;
using Scada.App.Services;
using Scada.App.ViewModels;
using Scada.Core.Configuration;
using Scada.Core.Devices;
using Scada.Core.Tags;
using Xunit;

namespace Scada.App.Tests;

public sealed class TagManagerLargeDatasetTests
{
    [Fact]
    public void TenThousandTagsRemainFunctionallyManageableWithSingleSelectedSubscription()
    {
        var options = new RuntimeOptions
        {
            Devices =
            [
                new DeviceDefinition { Id = "SIM01", Name = "Simulator 1", DriverType = "Simulator" },
                new DeviceDefinition { Id = "SIM02", Name = "Simulator 2", DriverType = "Simulator" }
            ],
            ScanGroups =
            [
                new ScanGroupDefinition { Name = "Normal", IntervalMilliseconds = 500 },
                new ScanGroupDefinition { Name = "Fast", IntervalMilliseconds = 100 }
            ],
            Tags = Enumerable.Range(0, 10_000).Select(index => new TagDefinition
            {
                Id = $"T{index:D5}",
                Name = $"Tag {index:D5}",
                DeviceId = index % 2 == 0 ? "SIM01" : "SIM02",
                Address = $"A{index}",
                DataType = (index % 3) switch
                {
                    0 => TagDataType.Boolean,
                    1 => TagDataType.Int32,
                    _ => TagDataType.Double
                },
                ScanGroup = index % 2 == 0 ? "Fast" : "Normal",
                Enabled = index % 4 != 0
            }).ToList()
        };
        var cache = new TestTagCache();
        var session = new ProjectEditSession(options, null, null);
        var manager = new TagManagerViewModel(
            session,
            cache,
            new TestClipboardAdapter(),
            new TestImportDecisionService(),
            new TestDeleteConfirmation());

        Assert.Equal(10_000, manager.Rows.Count);
        Assert.Equal(0, cache.ActiveSubscriptionCount);
        Assert.Equal(10_000, cache.TryGetCount);

        var firstRow = manager.Rows[0];
        manager.SearchText = "Tag 09999";
        Assert.Single(manager.ItemsView.Cast<TagEditorRowViewModel>());
        Assert.Same(firstRow, manager.Rows[0]);

        manager.SearchText = string.Empty;
        manager.DeviceFilter = "SIM02";
        Assert.NotEmpty(manager.ItemsView.Cast<TagEditorRowViewModel>());
        Assert.Same(firstRow, manager.Rows[0]);
        manager.DataTypeFilter = "Int32";
        Assert.NotEmpty(manager.ItemsView.Cast<TagEditorRowViewModel>());
        Assert.Same(firstRow, manager.Rows[0]);
        manager.ScanGroupFilter = "Normal";
        Assert.NotEmpty(manager.ItemsView.Cast<TagEditorRowViewModel>());
        Assert.Same(firstRow, manager.Rows[0]);

        manager.DeviceFilter = "All";
        manager.DataTypeFilter = "All";
        manager.ScanGroupFilter = "All";
        manager.ItemsView.SortDescriptions.Add(new SortDescription(nameof(TagEditorRowViewModel.Name), ListSortDirection.Descending));
        Assert.Equal("Tag 09999", manager.ItemsView.Cast<TagEditorRowViewModel>().First().Name);

        var selected = manager.Rows.Skip(100).Take(100).Cast<object>().ToArray();
        var namesBeforeBulk = manager.Rows.Skip(100).Take(100).Select(row => row.Name).ToArray();
        manager.SetSelection(selected);
        Assert.Equal(BulkEditValueKind.Mixed, manager.BulkEdit.Enabled.Kind);
        manager.BulkEdit.Enabled = BulkEditValue<bool>.Explicit(false);
        manager.ApplyBulkEdit();

        Assert.All(session.WorkingProject.Tags.Skip(100).Take(100), tag => Assert.False(tag.Enabled));
        Assert.Equal(namesBeforeBulk, manager.Rows.Skip(100).Take(100).Select(row => row.Name));

        manager.Activate();
        manager.SetSelection([manager.Rows[123]]);
        Assert.Equal(1, cache.ActiveSubscriptionCount);
        manager.Deactivate();
        Assert.Equal(0, cache.ActiveSubscriptionCount);
    }

    private sealed class TestClipboardAdapter : IClipboardAdapter
    {
        public string? GetText() => null;

        public void SetText(string text)
        {
        }
    }

    private sealed class TestImportDecisionService : ITagImportDecisionService
    {
        public TagImportDecision Decide(TagImportPreparation preparation, string operation) => TagImportDecision.ApplyAll;
    }

    private sealed class TestDeleteConfirmation : IDeleteConfirmation
    {
        public bool ConfirmDelete(int count) => true;
    }
}
