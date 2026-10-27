namespace ProductionLine.UserApp;

// Keep the viewport separate from WinForms AutoScroll, which can move its display rectangle
// during layout/focus changes caused by polling. Only the scrollbar/wheel/resize set this offset.
internal sealed class InspectionScrollPanel : Panel, IMessageFilter
{
    private readonly VScrollBar _bar = new() { SmallChange = 24, TabStop = true };
    private Control? _content;
    private int _offset, _minimumHeight, _wheelDelta;
    private bool _arranging, _filterAttached;

    public int ScrollOffset
    {
        get => _offset;
        set => SetOffset(value);
    }
    public bool CanScroll => _bar.Visible;

    public InspectionScrollPanel()
    {
        AutoScroll = false;
        DoubleBuffered = true;
        Controls.Add(_bar);
        _bar.ValueChanged += (_, _) => { if (!_arranging) SetOffset(_bar.Value); };
    }

    public void SetContent(Control content, int minimumHeight)
    {
        if (_content != null) throw new InvalidOperationException("검사 화면은 한 번만 설정할 수 있습니다.");
        _content = content;
        _minimumHeight = minimumHeight;
        content.Dock = DockStyle.None;
        content.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        Controls.Add(content);
        _bar.BringToFront();
        ArrangeContent();
    }

    private void SetOffset(int value)
    {
        if (_content == null) return;
        _offset = Math.Clamp(value, 0, Math.Max(0, _content.Height - ClientSize.Height));
        if (_bar.Value != _offset) _bar.Value = _offset;
        if (_content.Top != -_offset) _content.Top = -_offset;
    }

    private void ArrangeContent()
    {
        if (_arranging || _content == null) return;
        _arranging = true;
        try
        {
            int height = Math.Max(_minimumHeight, ClientSize.Height);
            int maxOffset = Math.Max(0, height - ClientSize.Height);
            _offset = Math.Clamp(_offset, 0, maxOffset);
            _bar.Visible = maxOffset > 0;
            _bar.Bounds = new Rectangle(Math.Max(0, ClientSize.Width - _bar.Width), 0, _bar.Width, ClientSize.Height);
            _bar.Minimum = 0;
            _bar.Maximum = Math.Max(0, height - 1);
            _bar.LargeChange = Math.Max(1, ClientSize.Height);
            _bar.Value = _offset;
            _content.SetBounds(0, -_offset, Math.Max(0, ClientSize.Width - (maxOffset > 0 ? _bar.Width : 0)), height);
        }
        finally { _arranging = false; }
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        ArrangeContent();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (!CanScroll) { base.OnMouseWheel(e); return; }
        _wheelDelta += e.Delta;
        int steps = _wheelDelta / SystemInformation.MouseWheelScrollDelta;
        _wheelDelta %= SystemInformation.MouseWheelScrollDelta;
        int lines = SystemInformation.MouseWheelScrollLines;
        int distance = lines < 0 ? Math.Max(24, ClientSize.Height - 24) : lines * 24;
        SetOffset(_offset - steps * distance);
        if (e is HandledMouseEventArgs handled) handled.Handled = true;
    }

    // Wheel messages can target nested camera/table panels instead of the outer viewport.
    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg != 0x020A || !Visible || !CanScroll || IsDisposed) return false;
        var target = Control.FromChildHandle(m.HWnd);
        if (target == null || (target != this && !Contains(target))) return false;
        for (Control? current = target; current != null && current != this; current = current.Parent)
            if (current is NumericUpDown or ComboBox or TextBoxBase or DataGridView) return false;
        int delta = unchecked((short)((m.WParam.ToInt64() >> 16) & 0xffff));
        OnMouseWheel(new HandledMouseEventArgs(MouseButtons.None, 0, 0, 0, delta));
        return true;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!_filterAttached) { Application.AddMessageFilter(this); _filterAttached = true; }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (_filterAttached) { Application.RemoveMessageFilter(this); _filterAttached = false; }
        base.OnHandleDestroyed(e);
    }
}
