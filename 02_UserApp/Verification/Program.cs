using ProductionLine.Shared;
using ProductionLine.UserApp;
using System.Reflection;

namespace ProductionLine.Verification;

internal static class Program
{
    [STAThread]
    static void ReadOnlyMain()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var api = new ProductionLineApiClient();
        api.LoginAsync("worker1", "worker123!").GetAwaiter().GetResult();
        using var form = new UserMainForm(api);
        var timer = new System.Windows.Forms.Timer { Interval = 4000 };
        form.Shown += (_, _) => timer.Start();
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                var pages = (TabControl)typeof(UserMainForm).GetField("_pages", flags)!.GetValue(form)!;
                if (pages.TabCount != 5) throw new Exception("Expected five separate pages");
                var status = (Label)typeof(UserMainForm).GetField("_connection", flags)!.GetValue(form)!;
                if (!status.Text.StartsWith("연결됨")) throw new Exception(status.Text);
                var counts = (Label)typeof(UserMainForm).GetField("_counts", flags)!.GetValue(form)!;
                if (!counts.Text.Contains("정상률")) throw new Exception("Counts missing");
                Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "previews"));
                for (var i = 0; i < pages.TabCount; i++)
                {
                    pages.SelectedIndex = i;
                    form.PerformLayout();
                    using var bitmap = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    bitmap.Save(Path.Combine(AppContext.BaseDirectory, "previews", $"page-{i + 1}.png"));
                }
                Console.WriteLine("PASS: login, allowed line, server refresh, counts, five pages and layout rendering");
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
            finally { timer.Dispose(); form.Close(); }
        };
        Application.Run(form);
    }
}
