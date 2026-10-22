using Kurippu.Desktop.ViewModels;

namespace Kurippu.Desktop.Tests;

public class ActivityLogTests
{
    [Fact]
    public void AppendsLinesAndNotifiesAnOpenView()
    {
        var log = new ActivityLog();
        var changes = 0;
        log.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(ActivityLog.Text)) changes++; };

        log.AppendLine("First");
        log.AppendLine("Second");

        Assert.Equal("First" + Environment.NewLine + "Second" + Environment.NewLine, log.Text);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void ClearResetsContentsButKeepsTheSameLogInstance()
    {
        var log = new ActivityLog();
        log.AppendLine("Previous session");

        log.Clear();
        log.AppendLine("Current session");

        Assert.Equal("Current session" + Environment.NewLine, log.Text);
    }
}
