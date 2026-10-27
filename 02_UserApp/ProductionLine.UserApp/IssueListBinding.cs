using System.Runtime.InteropServices;
using ProductionLine.Shared;

namespace ProductionLine.UserApp;

public sealed partial class UserMainForm
{
    private IssueDto[] _displayedIssues = Array.Empty<IssueDto>();

    private void BindIssues(IReadOnlyList<IssueDto> issues)
    {
        if (_displayedIssues.SequenceEqual(issues)) return;
        var selectedId = SelectedIssue()?.Id;
        var column = _issues.CurrentCell?.OwningColumn.Name;
        int firstVisible = _issues.FirstDisplayedScrollingRowIndex;
        Guid? firstVisibleId = firstVisible >= 0
            ? (_issues.Rows[firstVisible].DataBoundItem as IssueDto)?.Id : null;
        int horizontalOffset = _issues.HorizontalScrollingOffset;
        _binding = true;
        try
        {
            _issues.DataSource = issues.ToList();
            ConfigureIssues();
            foreach (DataGridViewRow row in _issues.Rows)
                if (row.DataBoundItem is IssueDto issue && issue.Id == selectedId)
                {
                    _issues.CurrentCell = column != null && _issues.Columns.Contains(column) && _issues.Columns[column].Visible
                        ? row.Cells[column] : row.Cells[nameof(IssueDto.Title)];
                    break;
                }
            // Selecting a cell can scroll it into view, so restore the viewport afterwards.
            if (firstVisible >= 0 && _issues.Rows.Count > 0)
            {
                int restoredIndex = Math.Min(firstVisible, _issues.Rows.Count - 1);
                foreach (DataGridViewRow row in _issues.Rows)
                    if (row.DataBoundItem is IssueDto issue && issue.Id == firstVisibleId)
                    { restoredIndex = row.Index; break; }
                _issues.FirstDisplayedScrollingRowIndex = restoredIndex;
                _issues.HorizontalScrollingOffset = horizontalOffset;
            }
            _displayedIssues = issues.ToArray();
        }
        finally { _binding = false; }
    }

    private void SetIssueDetailText(string text, bool preservePosition)
    {
        // Resetting Text every polling tick also resets the read position/selection.
        if (_issueDetail.Text == text) return;
        int selection = _issueDetail.SelectionStart, length = _issueDetail.SelectionLength;
        int firstLine = preservePosition && _issueDetail.IsHandleCreated
            ? IssueDetailMessage(_issueDetail.Handle, 0xCE, IntPtr.Zero, IntPtr.Zero).ToInt32() : 0; // EM_GETFIRSTVISIBLELINE
        _issueDetail.Text = text;
        if (!preservePosition) return;
        int start = Math.Min(selection, text.Length);
        _issueDetail.Select(start, Math.Min(length, text.Length - start));
        if (_issueDetail.IsHandleCreated)
        {
            int currentLine = IssueDetailMessage(_issueDetail.Handle, 0xCE, IntPtr.Zero, IntPtr.Zero).ToInt32();
            IssueDetailMessage(_issueDetail.Handle, 0xB6, IntPtr.Zero, new IntPtr(firstLine - currentLine)); // EM_LINESCROLL
        }
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr IssueDetailMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
