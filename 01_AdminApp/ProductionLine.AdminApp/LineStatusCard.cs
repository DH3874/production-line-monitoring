using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using ProductionLine.Shared;

namespace _01_AdminApp
{
    public sealed class LineStatusCard : UserControl
    {
        private readonly Label _title = new Label();
        private readonly Label _state = new Label();
        private readonly Label _counts = new Label();
        private readonly Label _updated = new Label();
        private readonly Button _detail = new Button();
        private readonly Button _emergency = new Button();
        private readonly ConveyorView _conveyor = new ConveyorView();
        private readonly ToolTip _tip = new ToolTip();
        private readonly TableLayoutPanel _layout;
        private bool _compact;
        private bool _localEmergencyStopped;
        private int _openIssues;
        private LineStatusDto? _line;
        public event Action<LineStatusDto>? DetailRequested;

        public LineStatusCard()
        {
            Height = 420;
            Margin = new Padding(0, 0, 0, 14);
            BackColor = AdminTheme.Surface;
            DoubleBuffered = true; ResizeRedraw = true;
            Padding = new Padding(14);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
            _layout = layout;
            foreach (float h in new[] { 40F, 50F, 45F }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
            _title.Dock = DockStyle.Fill;
            _title.AutoEllipsis = true;
            _title.Font = new Font("맑은 고딕", 15F, FontStyle.Bold);
            _title.ForeColor = AdminTheme.Text;
            _detail.Text = "상세 보기";
            _detail.Dock = DockStyle.Fill;
            _detail.Click += (_, _) => { if (_line != null) DetailRequested?.Invoke(_line); };
            header.Controls.Add(_title, 0, 0); header.Controls.Add(_detail, 1, 0);
            _state.Dock = _counts.Dock = _updated.Dock = DockStyle.Fill;
            _state.TextAlign = _counts.TextAlign = _updated.TextAlign = ContentAlignment.MiddleLeft;
            _state.AutoEllipsis = _counts.AutoEllipsis = _updated.AutoEllipsis = true;
            _updated.ForeColor = AdminTheme.Muted;
            _conveyor.Dock = DockStyle.Fill;
            _emergency.Text = "긴급 정지 (시뮬레이션)";
            _emergency.Dock = DockStyle.Fill;
            _emergency.BackColor = Color.Firebrick;
            _emergency.ForeColor = Color.White;
            _emergency.UseVisualStyleBackColor = false;
            _emergency.FlatStyle = FlatStyle.Flat;
            _emergency.Enabled = false;
            _emergency.Click += (_, _) =>
            {
                if (_line?.IsReal == false) SetEmergencyStopped(!_localEmergencyStopped);
            };
            layout.Controls.Add(header, 0, 0);
            layout.Controls.Add(_state, 0, 1);
            layout.Controls.Add(_counts, 0, 2);
            layout.Controls.Add(_conveyor, 0, 3);
            layout.Controls.Add(_emergency, 0, 4);
            layout.Controls.Add(_updated, 0, 5);
            Controls.Add(layout);
            _title.DoubleClick += (_, _) => { if (_line != null) DetailRequested?.Invoke(_line); };
        }

        public void UseCompactLayout()
        {
            _compact = true; Padding = new Padding(8); Margin = Padding.Empty;
            _title.Font = new Font("맑은 고딕",12F,FontStyle.Bold);
            _state.Font = _counts.Font = _updated.Font = _emergency.Font = new Font("맑은 고딕",9F);
            _layout.RowStyles[0].Height = 34;
            _layout.RowStyles[1].Height = 24;
            _layout.RowStyles[2].Height = 24;
            _layout.RowStyles[4].Height = 30;
            _layout.RowStyles[5].Height = 28;
            _conveyor.CompactMode = true;
        }

        protected override void Dispose(bool disposing)
        { if (disposing) _tip.Dispose(); base.Dispose(disposing); }

        // 둥근 어두운 카드 (모서리 바깥은 부모 바탕색)
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (var canvas = new SolidBrush(Parent?.BackColor ?? AdminTheme.Canvas)) e.Graphics.FillRectangle(canvas, ClientRectangle);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = AdminTheme.Round(new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), 12);
            using var fill = new SolidBrush(AdminTheme.Surface); e.Graphics.FillPath(fill, path);
            using var pen = new Pen(_line?.IsReal == true ? Color.FromArgb(52, 96, 160) : AdminTheme.Border); e.Graphics.DrawPath(pen, path);
        }

        public void UpdateLine(LineStatusDto line, int openIssues, int completionRequests)
        {
            _line = line;
            _conveyor.HardwareLinked = line.IsReal;
            _conveyor.Hardware = line.Hardware;
            if (!line.IsReal) _conveyor.ConfigureVirtualLine(line.Id);
            _openIssues = openIssues;
            _title.Text = line.Name + (line.IsReal ? "  [실제]" : $"  [가상 · {RotaryGateSimulation.ProfileName(line.Id)}]");
            _state.Text = $"가동: {line.OperationStatus}   |   검사: {line.InspectionStatus}   |   미종결 문제 {openIssues}건";
            _state.ForeColor = line.OperationStatus == "가동" ? Color.SeaGreen : Color.Firebrick;
            long determined = (long)line.NormalCount + line.DefectCount;
            string rate = determined == 0 ? "-" : $"{line.DefectCount * 100.0 / determined:0.0}%";
            _counts.Text = $"정상 {line.NormalCount:N0}   /   불량 {line.DefectCount:N0}   /   판정 불가 {line.UndeterminedCount:N0}   /   불량률 {rate}";
            _updated.Text = $"데이터 갱신: {line.UpdatedAt:yyyy-MM-dd HH:mm:ss}   |   {(line.IsReal ? "실제 라인 현황" : "가상 데이터 - 실제 생산 통계와 구분")}";
            if (line.IsReal) _updated.Text += "\r\n" + HardwareStatusText.Format(line.Hardware);
            _tip.SetToolTip(_updated,_updated.Text);
            if (_compact)
                _updated.Text = line.IsReal
                    ? $"갱신 {line.UpdatedAt:HH:mm:ss} · " + (line.Hardware is { IsFresh:true } h
                        ? $"온도 {h.TemperatureCelsius:0.#}℃ / 습도 {h.HumidityPercent:0.#}% / 가스 A0 {h.GasAnalogValue}"
                        : "센서 미연결 · 자세한 상태는 상세 보기")
                    : $"갱신 {line.UpdatedAt:HH:mm:ss} · 가상 예시 데이터";
            _tip.SetToolTip(_state,_state.Text); _tip.SetToolTip(_counts,_counts.Text);
            if (line.IsReal)
            {
                _emergency.Enabled = false;
                _emergency.Text = line.Hardware is not { IsFresh: true } ? "긴급 정지 · 장비 상태 미확인"
                    : line.Hardware.EmergencyStopped ? "D12 긴급 정지 작동" : "D12 긴급 정지 해제됨";
                _conveyor.EmergencyStopped = line.Hardware?.EmergencyStopped == true;
            }
            else
            {
                _emergency.Enabled = true;
                _conveyor.EmergencyStopped = _localEmergencyStopped || line.OperationStatus == "긴급 정지";
                _emergency.Text = _conveyor.EmergencyStopped ? "긴급 정지 해제 (시뮬레이션)"
                    : line.OperationStatus == "가동" ? "긴급 정지 (시뮬레이션)" : "시뮬레이션 · " + line.OperationStatus;
                _emergency.BackColor = _conveyor.EmergencyStopped ? Color.Firebrick : Color.FromArgb(65,85,105);
            }
            _detail.Text = line.IsReal ? "상세 보기" : "가상 라인 안내";
            _conveyor.Running = line.IsReal ? line.Hardware is { IsFresh: true, BeltRunning: true }
                : line.OperationStatus == "가동";
            _conveyor.Invalidate();
        }
        public void Animate() => _conveyor.Advance();
        public int SimulationNormalCount => _conveyor.CompletedNormalCount;
        public int SimulationDefectCount => _conveyor.CompletedDefectCount;
        public void SetPlaying(bool playing) { _conveyor.Playing = playing; _conveyor.Invalidate(); }
        public void SetSimulationPlaying(bool playing)
        {
            _conveyor.DemoMode = _line?.IsReal != true;
            _conveyor.Playing = _line?.IsReal == true || playing;
            if (_line?.IsReal == false)
            {
                _line = _line with { OperationStatus = playing ? "가동" : "정지" };
                _state.Text = $"가동: {_line.OperationStatus}   |   검사: {_line.InspectionStatus}   |   미종결 문제 {_openIssues}건";
                _state.ForeColor = playing ? Color.SeaGreen : Color.Firebrick;
                _emergency.Text = _conveyor.EmergencyStopped ? "긴급 정지 해제 (시뮬레이션)"
                    : playing ? "긴급 정지 (시뮬레이션)" : "가상 시뮬레이션 · 정지";
                _emergency.BackColor = _conveyor.EmergencyStopped ? Color.FromArgb(120,30,30) : Color.Firebrick;
            }
            _conveyor.Invalidate();
        }
        public bool EmergencyStopped => _conveyor.EmergencyStopped;
        public bool IsActive => _line?.IsReal == true
            ? _line.Hardware is { IsFresh: true, BeltRunning: true, EmergencyStopped: false }
            : _conveyor.Playing && !_conveyor.EmergencyStopped;
        public void SetEmergencyStopped(bool stopped)
        {
            if (_line?.IsReal == true) return; // A visual toggle must never claim to stop physical equipment.
            _localEmergencyStopped = stopped;
            _conveyor.EmergencyStopped = stopped || _line?.OperationStatus == "긴급 정지";
            _emergency.Text = stopped ? "긴급 정지 해제 (시뮬레이션)" : "긴급 정지 (시뮬레이션)";
            _emergency.BackColor = stopped ? Color.FromArgb(120, 30, 30) : Color.Firebrick;
            _conveyor.Invalidate();
        }

        private sealed class ConveyorView : Control
        {
            private readonly RotaryGateSimulation _simulation = new();
            private double _beltPhase;
            private double _hardwareProductX = 40;
            private bool _hasHardwareProduct;
            public bool Running { get; set; }
            public bool Playing { get; set; } = true;
            public bool EmergencyStopped { get; set; }
            public bool DemoMode { get; set; }
            public bool CompactMode { get; set; }
            public bool HardwareLinked { get; set; }
            public LineHardwareStatusDto? Hardware { get; set; }
            public int CompletedNormalCount => _simulation.CompletedNormalCount;
            public int CompletedDefectCount => _simulation.CompletedDefectCount;
            public void ConfigureVirtualLine(int lineId) => _simulation.ConfigureLine(lineId);
            public double GateAngleDegrees => HardwareLinked
                ? Hardware is { IsFresh: true } h ? Math.Clamp(h.SorterAngle, 0, 90) : 0
                : _simulation.GateAngleDegrees;

            public ConveyorView()
            {
                DoubleBuffered = true;
                BackColor = AdminTheme.Input;
                SetStyle(ControlStyles.ResizeRedraw, true);
            }
            public void Advance()
            {
                if (HardwareLinked)
                {
                    if (Hardware is { IsFresh: true, BeltRunning: true, EmergencyStopped: false } h)
                    {
                        var speed = h.SpeedBoosted ? 2 : 1;
                        _beltPhase = (_beltPhase + .06 * speed) % 1;
                        _hardwareProductX += 3 * speed;
                        if (_hardwareProductX > 525) _hardwareProductX = 40;
                        _hasHardwareProduct = true;
                    }
                    Invalidate();
                    return;
                }
                if (!Playing || EmergencyStopped || !DemoMode && !Running) return;
                _simulation.Advance(.05);
                if (_simulation.BeltMoving) _beltPhase = (_beltPhase + .06) % 1;
                Invalidate();
            }
            private string MotionText => HardwareLinked
                ? Hardware is not { IsFresh: true } ? "장비 상태 미확인"
                    : Hardware.EmergencyStopped ? "긴급 정지"
                    : Hardware.Sorting ? "회전판 분류 중"
                    : !Hardware.BeltRunning ? "벨트 정지"
                    : Hardware.SpeedBoosted ? "2배 가속 중" : "물체 이동 중"
                : EmergencyStopped ? "긴급 정지" : !Playing ? "시뮬레이션 정지"
                    : !DemoMode && !Running ? "라인 정지" : _simulation.StageText;

            private void DrawCompactScene(Graphics g)
            {
                using var font = new Font("맑은 고딕",8F);
                using var pen = new Pen(Color.FromArgb(96,118,145));
                using var frame = new SolidBrush(Color.FromArgb(58,78,104));
                using var belt = new SolidBrush(Color.FromArgb(30,46,68));
                using var motor = new SolidBrush(Color.FromArgb(110,128,150));
                TextRenderer.DrawText(g,"직선 라인 · 회전판 분류",font,new Rectangle(5,0,Width/2-10,20),AdminTheme.Muted,TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g,MotionText,font,new Rectangle(Width/2,0,Width/2-5,20),EmergencyStopped?AdminTheme.Bad:AdminTheme.Muted,TextFormatFlags.Right|TextFormatFlags.EndEllipsis);
                float left=18, right=Width-18, width=Math.Max(20,right-left);
                float area=Math.Max(25,Height-38), beltHeight=Math.Clamp(area*.24F,12,38);
                float beltTop=22+area*.52F, pivotY=beltTop+beltHeight+6;
                float pivotX=left+width*.65F, length=Math.Min(width*.215F,area*.65F);
                float binX=pivotX+length*.574F, binWidth=Math.Max(28,length*.7F), binTop=28;
                float binHeight=Math.Max(10,beltTop-binTop-10);
                using(var bin = new SolidBrush(Color.FromArgb(26,40,60)))
                    g.FillRectangle(bin,binX-binWidth/2,binTop,binWidth,binHeight);
                g.DrawRectangle(pen,binX-binWidth/2,binTop,binWidth,binHeight);
                g.FillRectangle(frame,binX-binWidth*.38F,binTop+binHeight,binWidth*.76F,beltTop-binTop-binHeight);
                g.FillRectangle(frame,left,beltTop,width,beltHeight);g.DrawRectangle(pen,left,beltTop,width,beltHeight);
                g.FillRectangle(belt,left+4,beltTop+3,Math.Max(1,width-8),Math.Max(1,beltHeight-6));
                using(var tread=new Pen(Color.FromArgb(52,72,98)))
                    for(float x=left+5+(float)_beltPhase*24;x<right-4;x+=24)
                        g.DrawLine(tread,x,beltTop+4,x-5,beltTop+beltHeight-4);
                g.FillEllipse(motor,left+3,beltTop+3,8,beltHeight-6);
                g.FillEllipse(motor,right-11,beltTop+3,8,beltHeight-6);
                float cameraX=left+width*.397F,cameraTop=Math.Max(29,beltTop-29);
                foreach(float x in new[]{cameraX-17,cameraX+13})
                    g.FillRectangle(frame,x,cameraTop+5,4,beltTop+beltHeight-cameraTop-3);
                g.FillRectangle(frame,cameraX-17,cameraTop,34,8);g.DrawRectangle(pen,cameraX-17,cameraTop,34,8);
                g.FillRectangle(motor,cameraX-10,cameraTop+8,20,12);
                using(var lens=new SolidBrush(Color.FromArgb(65,70,74)))g.FillEllipse(lens,cameraX-3,cameraTop+11,6,6);
                g.FillRectangle(motor,pivotX-10,pivotY+2,20,16);
                var plate=g.Save();g.TranslateTransform(pivotX,pivotY);g.RotateTransform((float)-GateAngleDegrees);
                g.FillRectangle(frame,0,-2.5F,length,5);g.DrawRectangle(pen,0,-2.5F,length,5);g.Restore(plate);
                using(var shaft=new SolidBrush(Color.FromArgb(80,86,92)))g.FillEllipse(shaft,pivotX-4,pivotY-4,8,8);
                var visible=HardwareLinked ? _hasHardwareProduct && Hardware is {IsFresh:true} : _simulation.ProductVisible;
                if(visible)
                {
                    double sx=HardwareLinked ? _hardwareProductX : _simulation.ProductX;
                    float x=left+(float)((sx-20)/474)*width,y=beltTop+beltHeight/2;
                    bool reject=HardwareLinked ? Hardware!.Sorting : _simulation.IsDefective;
                    if(reject && sx>=310 && GateAngleDegrees>1)
                    {
                        float contactX=pivotX+(pivotY-y)/1.428F;
                        if(sx<347)x=(left+290F/474*width)+(contactX-(left+290F/474*width))*(float)(sx-310)/37;
                        else
                        {
                            float slide=Math.Clamp((float)(sx-347)/53,0,1);
                            x=contactX+(binX-contactX)*slide;
                            y+=(binTop+binHeight/2-y)*slide;
                        }
                    }
                    if(!HardwareLinked || !reject || sx<=400)
                    {
                        using var product = new SolidBrush(Color.FromArgb(210,217,221));
                        using var border = new Pen(!HardwareLinked && _simulation.Inspected
                            ? reject ? AdminTheme.Warn : AdminTheme.Good : Color.FromArgb(95,105,112));
                        float ph=Math.Clamp(beltHeight*.76F,11,26),pw=ph*.59F;
                        g.FillRectangle(product,x-pw/2,y-ph/2,pw,ph);g.DrawRectangle(border,x-pw/2,y-ph/2,pw,ph);
                    }
                }
                if(Height>=100)
                {
                    TextRenderer.DrawText(g,"인식부",font,new Point((int)cameraX-20,(int)cameraTop-17),AdminTheme.Muted);
                    TextRenderer.DrawText(g,"불량 분류함",font,new Point((int)(binX+binWidth/2+5),(int)binTop),AdminTheme.Warn);
                    TextRenderer.DrawText(g,"모터 축 · 회전 분류판",font,new Point((int)pivotX+27,(int)pivotY+9),AdminTheme.Muted);
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (Width < 10 || Height < 10) return;
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if(CompactMode && Height<220) { DrawCompactScene(g);return; }
                // Same camera/bin/pivot arrangement as the GIF, shared by actual and virtual lines.
                var saved = g.Save();
                float scale = Math.Min(Width / 520F, Height / 280F);
                g.TranslateTransform((Width-520*scale)/2, (Height-280*scale)/2);
                g.ScaleTransform(scale, scale);
                using var text = new Font("맑은 고딕", 9F);
                using var small = new Font("맑은 고딕", 8F);
                using var pen = new Pen(Color.FromArgb(96,118,145),1);
                using var frame = new SolidBrush(Color.FromArgb(58,78,104));
                using var belt = new SolidBrush(Color.FromArgb(30,46,68));
                using var dark = new SolidBrush(Color.FromArgb(110,128,150));
                {
                    Label(g,"직선 라인 · 회전판 분류",text,AdminTheme.Text,15,10);
                    Label(g,MotionText,small,EmergencyStopped?AdminTheme.Bad:AdminTheme.Muted,338,12);
                    using var divider = new Pen(Color.FromArgb(43,62,88));
                    g.DrawLine(divider,15,37,505,37);
                }
                using (var bin = new SolidBrush(Color.FromArgb(26,40,60))) g.FillRectangle(bin,348,59,64,43);
                g.DrawRectangle(pen,348,59,64,43);
                Label(g,"불량 분류함",small,AdminTheme.Warn,331,41);
                using (var chute = new SolidBrush(Color.FromArgb(34,50,74))) g.FillRectangle(chute,355,102,50,26);
                g.DrawLine(pen,355,102,355,126);g.DrawLine(pen,405,102,405,126);
                g.FillRectangle(frame,20,126,474,60);g.DrawRectangle(pen,20,126,474,60);
                g.FillRectangle(belt,27,132,460,48);
                using (var tread = new Pen(Color.FromArgb(52,72,98)))
                    for (float x=29+(float)_beltPhase*22;x<487;x+=22) g.DrawLine(tread,x,133,x-7,179);
                foreach(float x in new[]{31F,482F})
                { g.FillEllipse(dark,x-7,139,14,34);g.DrawEllipse(pen,x-7,139,14,34); }
                g.DrawLine(pen,32,126,355,126);g.DrawLine(pen,405,126,482,126);
                { Label(g,"투입 →",small,AdminTheme.Muted,23,106);Label(g,"통과 →",small,AdminTheme.Good,444,106); }
                g.FillRectangle(frame,181,84,7,104);g.FillRectangle(frame,228,84,7,104);
                g.FillRectangle(frame,181,82,54,12);g.DrawRectangle(pen,181,82,54,12);
                g.FillRectangle(dark,195,94,27,18);
                using (var lens = new SolidBrush(Color.FromArgb(65,70,74))) g.FillEllipse(lens,204,98,9,9);
                Label(g,"인식부",small,AdminTheme.Muted,190,61);

                // Pivot fixed below the belt: horizontal at 0°, rotates diagonally for rejects.
                g.FillRectangle(dark,326,190,29,29);g.DrawRectangle(pen,326,190,29,29);
                var plate = g.Save();
                g.TranslateTransform(340,193);g.RotateTransform((float)-GateAngleDegrees);
                g.FillRectangle(frame,0,-4,112,8);g.DrawRectangle(pen,0,-4,112,8);g.Restore(plate);
                using (var shaft = new SolidBrush(Color.FromArgb(80,86,92))) g.FillEllipse(shaft,335,188,10,10);
                { Label(g,"모터 축",small,AdminTheme.Muted,317,224);Label(g,"회전 분류판",small,AdminTheme.Muted,385,224); }

                var visible = HardwareLinked ? _hasHardwareProduct && Hardware is { IsFresh:true } : _simulation.ProductVisible;
                if (visible)
                {
                    float x=(float)(HardwareLinked ? _hardwareProductX : _simulation.ProductX);
                    float y=(float)(HardwareLinked ? 156 : _simulation.ProductY);
                    if (HardwareLinked && Hardware!.Sorting && x>=347 && x<=400)
                        y=156-80*(x-347)/53;
                    if (!HardwareLinked || !Hardware!.Sorting || x<=400)
                    {
                        var colour = HardwareLinked ? Color.FromArgb(95,105,112) : !_simulation.Inspected
                            ? Color.FromArgb(95,105,112) : _simulation.IsDefective ? AdminTheme.Warn : AdminTheme.Good;
                        using var product = new SolidBrush(Color.FromArgb(210,217,221));
                        using var border = new Pen(colour,1.7F);
                        g.FillRectangle(product,x-10,y-17,20,34);g.DrawRectangle(border,x-10,y-17,20,34);
                        g.DrawLine(pen,x-6,y-12,x+6,y-12);
                    }
                }
                {
                    var result = HardwareLinked ? Hardware is {IsFresh:true} h ? $"분류 서보 {h.SorterAngle}° · {(h.Sorting?"분류 중":"대기")}" : "장비 연결을 확인하세요."
                        : !_simulation.Inspected ? "판정 대기" : _simulation.IsDefective ? "불량 · 대각선 회전 분류" : "정상 · 판 고정 / 직진 통과";
                    Label(g,result,small,AdminTheme.Muted,16,207);
                    Label(g,HardwareLinked ? "제품 이동은 예시 · 벨트/분류판은 실제 장비 상태" : _simulation.DemoDescription+" · 가상 데이터",small,AdminTheme.Muted,15,263);
                }
                g.Restore(saved);
            }
            private static void Label(Graphics g,string value,Font font,Color colour,float x,float y)
            { using var brush=new SolidBrush(colour);g.DrawString(value,font,brush,x,y); }
        }
    }
}
