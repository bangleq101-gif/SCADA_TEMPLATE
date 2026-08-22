using System.Windows;

namespace Scada.App.Services;

public sealed class WpfDeleteConfirmation : IDeleteConfirmation
{
    public bool ConfirmDelete(int count) =>
        MessageBox.Show(
            $"Delete {count} selected tag(s)? This change affects the working project.",
            "Confirm tag deletion",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
}

public sealed class WpfTagImportDecisionService : ITagImportDecisionService
{
    public TagImportDecision Decide(TagImportPreparation preparation, string operation)
    {
        if (!preparation.HasConflicts)
        {
            var result = MessageBox.Show(
                $"{operation} is ready to add {preparation.Candidates.Count} tag(s). Apply now?",
                operation,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            return result == MessageBoxResult.Yes ? TagImportDecision.ApplyAll : TagImportDecision.Cancel;
        }

        var conflictSummary = string.Join(
            Environment.NewLine,
            preparation.Conflicts.Take(5).Select(conflict => $"Row {conflict.SourceRow}: {conflict.Message}"));
        var remaining = preparation.Conflicts.Count > 5
            ? $"{Environment.NewLine}... and {preparation.Conflicts.Count - 5} more."
            : string.Empty;
        var nonConflictingCount = preparation.NonConflictingCandidates.Count;
        var choice = MessageBox.Show(
            $"{operation} found {preparation.Conflicts.Count} conflict(s).{Environment.NewLine}{conflictSummary}{remaining}{Environment.NewLine}{Environment.NewLine}Yes: append {nonConflictingCount} non-conflicting tag(s). No: cancel without changes.",
            $"{operation} conflicts",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        return choice == MessageBoxResult.Yes
            ? TagImportDecision.AppendNonConflicting
            : TagImportDecision.Cancel;
    }
}
