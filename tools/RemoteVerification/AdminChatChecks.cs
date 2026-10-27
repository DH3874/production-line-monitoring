using System.Reflection;
using ProductionLine.Shared;
using ProductionLine.UserApp;
using _01_AdminApp;
using _01_AdminApp.Services;

internal static class AdminChatChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object form, string name) => (T)form.GetType().GetField(name, Flags)!.GetValue(form)!;
    private static async Task Call(object form, string name) => await (Task)form.GetType().GetMethod(name, Flags)!.Invoke(form, null)!;
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS: " + message); }
    private static async Task Wait(Func<bool> ready)
    {
        for (var i = 0; i < 160; i++) { if (ready()) return; await Task.Delay(50); }
        throw new TimeoutException("Administrator chat did not update");
    }

    public static async Task RunAsync(UserMainForm form, ProductionLineApiClient worker, ProductionLineApiClient admin)
    {
        var issueCount=(await worker.GetIssuesAsync()).Count;
        Field<TextBox>(form,"_aiInput").Text="컨베이어 벨트가 움직이지 않아요";
        await Call(form,"SendAiChatAsync");
        Check(Field<RichTextBox>(form,"_aiMessages").Text.Contains("컨베이어 벨트가 움직이지 않아요") &&
            Field<TextBox>(form,"_aiInput").Text=="" && (await worker.GetIssuesAsync()).Count==issueCount,
            "free AI conversation succeeds without selecting or creating a problem");
        var first = await worker.CreateIssueAsync(new(1, IssueType.ProductionLine, "chat-check-A", "isolated test"));
        var second = await worker.CreateIssueAsync(new(1, IssueType.ProductionLine, "chat-check-B", "isolated test"));
        try { await worker.SendIssueMessageAsync(first.Id,"발생 상태 전송 금지"); throw new Exception("Occurred issue accepted chat"); }
        catch(InvalidOperationException ex) when(ex.Message.Contains("조치 중")) { Check(true,"server refuses worker messages before action starts"); }
        await worker.UpdateIssueAsync(first.Id,new(IssueStatus.Acknowledged,"확인"));
        first=await worker.UpdateIssueAsync(first.Id,new(IssueStatus.InProgress,"점검 시작"));
        await Call(form, "RefreshAfterChangeAsync");
        var input = Field<TextBox>(form, "_issueChatInput");
        var history = Field<RichTextBox>(form, "_issueChatMessages");
        var issueGrid=Field<DataGridView>(form,"_issues");
        void SelectIssue(Guid id)
        {
            foreach(DataGridViewRow row in issueGrid.Rows)
                if(row.DataBoundItem is IssueDto item && item.Id==id) {issueGrid.CurrentCell=row.Cells.Cast<DataGridViewCell>().First(c=>c.Visible);break;}
            form.GetType().GetMethod("ShowIssue",Flags)!.Invoke(form,null);
        }
        var pages = Field<TabControl>(form, "_pages");
        pages.SelectedIndex = 2;
        Check(pages.TabPages.Count == 5 && pages.SelectedTab!.Text == "문제 · 조치" &&
            pages.TabPages.Cast<TabPage>().All(p=>p.Text!="관리자 1:1 채팅") && pages.TabPages[4].Text=="단체 채팅" && input.Parent != null,
            "worker has only embedded problem/action chat and retains group chat as fifth menu");
        SelectIssue(first.Id);
        input.Text = "작업자 1:1 문의 테스트";
        await Call(form, "SendIssueChatMessageAsync");
        var sent = await admin.GetIssueMessagesAsync(first.Id);
        Check(sent.Count(m => m.Message == "작업자 1:1 문의 테스트" && m.SenderRole == UserRole.User) == 1 && input.Text == "",
            "worker chat sends exactly one message to existing administrator inquiry endpoint");
        await admin.SendIssueMessageAsync(first.Id, "관리자 답변 테스트");
        await Wait(() => history.Text.Contains("관리자 답변 테스트"));
        Check(history.Text.Contains("작업자 1:1 문의 테스트") && history.Text.Contains("관리자"),
            "automatic polling displays administrator reply with existing conversation");
        foreach(DataGridViewRow row in issueGrid.Rows)
            if(row.DataBoundItem is IssueDto item && item.Id==first.Id) {issueGrid.CurrentCell=row.Cells.Cast<DataGridViewCell>().First(c=>c.Visible);break;}
        form.GetType().GetMethod("ShowIssue",Flags)!.Invoke(form,null);
        var issueInput=Field<TextBox>(form,"_issueChatInput");
        Check(!issueInput.ReadOnly && Field<Button>(form,"_issueChatSend").Enabled,"problem/action screen enables chat only for in-progress selection");
        issueInput.Text="문제 조치창에서 전송";
        await Call(form,"SendIssueChatMessageAsync");
        Check((await admin.GetIssueMessagesAsync(first.Id)).Any(m=>m.Message=="문제 조치창에서 전송"),"problem/action chat reaches existing administrator conversation");
        using(var completion=new CompletionRequestForm(new ApiDashboardService(admin),admin)
            {ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-3000,-3000)})
        {
            completion.Show();var grid=Field<DataGridView>(completion,"_grid");
            await Wait(()=>grid.Rows.Cast<DataGridViewRow>().Any(r=>r.DataBoundItem is IssueDto d && d.Id==first.Id));
            foreach(DataGridViewRow row in grid.Rows) if(row.DataBoundItem is IssueDto item && item.Id==first.Id) {grid.CurrentCell=row.Cells[0];break;}
            completion.GetType().GetMethod("ShowDetails",Flags)!.Invoke(completion,null);
            var panel=Field<object>(completion,"_chat");
            Check(Field<Button>(panel,"_send").Enabled,"completion review window includes in-progress issues and enables their chat");
            Field<TextBox>(panel,"_draft").Text="완료 요청 확인창 관리자 답변";
            await Call(panel,"SendAsync");
            await Wait(()=>Field<RichTextBox>(form,"_issueChatMessages").Text.Contains("완료 요청 확인창 관리자 답변"));
            Check(true,"administrator completion-window reply appears in worker problem/action chat");
            await worker.UpdateIssueAsync(first.Id,new(IssueStatus.CompletionRequested,"점검 완료"));
            try {await worker.SendIssueMessageAsync(first.Id,"상태 변경 후 전송 금지");throw new Exception("completed request accepted chat");}
            catch(InvalidOperationException ex) when(ex.Message.Contains("조치 중")) {Check(true,"server rejects stale chat after status leaves in-progress");}
            await Call(form,"RefreshAfterChangeAsync");
            Check(issueInput.ReadOnly && !Field<Button>(form,"_issueChatSend").Enabled && input.ReadOnly,
                "worker disables embedded chat when completion is requested");
            await Call(completion,"ReloadAsync");
            Check(Field<TextBox>(panel,"_draft").ReadOnly,"completion review chat becomes read-only after status change");
            completion.Close();
        }
        await admin.UpdateIssueAsync(first.Id,new(IssueStatus.InProgress,"재조치 확인"));
        await Call(form,"RefreshAfterChangeAsync");
        input.Text = "A 문제 작성 중";
        SelectIssue(second.Id);
        Check(!history.Text.Contains("관리자 답변 테스트"), "switching issue clears the previous conversation immediately");
        input.Text = "B 문제 작성 중";
        SelectIssue(first.Id);
        Check(input.Text == "A 문제 작성 중", "issue-specific unsent drafts survive switching conversations");
        await Call(form, "RefreshAfterChangeAsync");
        Check(input.Text == "A 문제 작성 중", "automatic data refresh preserves administrator chat draft");
    }
}
