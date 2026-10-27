namespace ProductionLine.UserApp;

internal sealed class GridViewportState
{
    private readonly Guid? _selected, _top;
    private readonly string? _column;
    private readonly int _topIndex, _horizontal;
    private GridViewportState(DataGridView grid,Func<DataGridViewRow,Guid?> id)
    {
        _selected = grid.CurrentRow is { } row ? id(row) : null;
        _column = grid.CurrentCell?.OwningColumn.Name;
        _topIndex = grid.FirstDisplayedScrollingRowIndex;
        _top = _topIndex >= 0 && _topIndex < grid.Rows.Count ? id(grid.Rows[_topIndex]) : null;
        _horizontal = grid.HorizontalScrollingOffset;
    }
    public static GridViewportState Capture(DataGridView grid,Func<DataGridViewRow,Guid?> id) => new(grid,id);
    public void Restore(DataGridView grid,Func<DataGridViewRow,Guid?> id)
    {
        if (grid.Rows.Count == 0) return;
        var column = grid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c=>c.Visible && c.Name==_column)
            ?? grid.Columns.Cast<DataGridViewColumn>().Where(c=>c.Visible).OrderBy(c=>c.DisplayIndex).FirstOrDefault();
        if (column == null) return;
        if (_selected.HasValue)
            foreach (DataGridViewRow row in grid.Rows)
                if (row.Visible && id(row)==_selected) { grid.CurrentCell=row.Cells[column.Index]; break; }
        // CurrentCell can scroll into view, so restore the viewport afterwards.
        var topIndex = Math.Clamp(_topIndex,0,grid.Rows.Count-1);
        if (_top.HasValue)
            foreach (DataGridViewRow row in grid.Rows)
                if (row.Visible && id(row)==_top) { topIndex=row.Index; break; }
        if (grid.Rows[topIndex].Visible && !grid.Rows[topIndex].Frozen)
            grid.FirstDisplayedScrollingRowIndex=topIndex;
        grid.HorizontalScrollingOffset=_horizontal;
    }
}
