using DQOPR.NetPulse.Core.UI;

namespace DQOPR.NetPulse.Core.Tests.UI;

public sealed class UiCommandCatalogTests
{
    [Fact]
    public void VisibleCommandsAreImplementedOrDisabled()
    {
        Assert.NotEmpty(UiCommandCatalog.VisibleCommands);
        Assert.All(
            UiCommandCatalog.VisibleCommands,
            command =>
            {
                Assert.False(command.Visible && command.Enabled && !command.Implemented);
                if (command.Visible && !command.Enabled)
                {
                    Assert.False(string.IsNullOrWhiteSpace(command.DisabledReason));
                }
            });
    }

    [Fact]
    public void RequiredMenuCommandsAreInventoried()
    {
        var labels = UiCommandCatalog.VisibleCommands.Select(command => command.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var required in new[]
        {
            "New monitoring session",
            "Run Quick Test",
            "Open session",
            "Export current session",
            "Settings",
            "Exit",
            "Dashboard",
            "History",
            "Reports",
            "Activity log",
            "Refresh",
            "Documentation",
            "Open data folder",
            "Open logs folder",
            "Report an issue",
            "About"
        })
        {
            Assert.Contains(required, labels);
        }
    }
}
