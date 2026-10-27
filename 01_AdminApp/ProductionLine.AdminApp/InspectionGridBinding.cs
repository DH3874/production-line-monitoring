namespace _01_AdminApp;

internal static class InspectionGridBinding
{
    // Restore by record ID, since newly arrived records change row indexes.
    public static void Bind<T>(DataGridView grid, List<T> records, Func<T,Guid> id)
    {
        var selected = grid.CurrentRow?.DataBoundItem is T item ? id(item) : (Guid?)null;
        var column = grid.CurrentCell?.ColumnIndex ?? 0;
        var top = grid.FirstDisplayedScrollingRowIndex;
        var horizontal = grid.HorizontalScrollingOffset;
        grid.DataSource = records;
        if (selected.HasValue)
            foreach (DataGridViewRow row in grid.Rows)
                if (row.DataBoundItem is T current && id(current) == selected.Value)
                { grid.CurrentCell = row.Cells[Math.Min(column,grid.Columns.Count-1)]; break; }
        if (top >= 0 && grid.Rows.Count > 0)
            grid.FirstDisplayedScrollingRowIndex = Math.Min(top,grid.Rows.Count-1);
        grid.HorizontalScrollingOffset = horizontal;
    }
}
