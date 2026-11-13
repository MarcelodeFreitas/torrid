using System.Xml.Linq;
using Torrid.App.Startup;

namespace Torrid.Tests;

public class TaskSchedulerStartupTests
{
    private const string ExePath = @"C:\Apps\Torrid\Torrid.App.exe";
    private const string User = @"PC\Marcelo";

    private static string CommandOf(string xml) =>
        XDocument.Parse(xml).Descendants().First(e => e.Name.LocalName == "Command").Value;

    [Fact]
    public void BuildTaskXml_RunsTheGivenExe_AtLogon_WithHighestPrivileges()
    {
        var xml = TaskSchedulerStartup.BuildTaskXml(ExePath, User);

        Assert.Contains("<LogonTrigger>", xml);
        Assert.Contains("<RunLevel>HighestAvailable</RunLevel>", xml);
        Assert.Equal(ExePath, CommandOf(xml));
    }

    [Fact]
    public void BuildTaskXml_ScopesTriggerAndPrincipalToTheGivenUser()
    {
        var xml = TaskSchedulerStartup.BuildTaskXml(ExePath, User);

        // Without a UserId the logon trigger fires for every account on the machine.
        Assert.Equal(2, XDocument.Parse(xml).Descendants().Count(e => e.Name.LocalName == "UserId" && e.Value == User));
    }

    [Fact]
    public void BuildTaskXml_HasNoTimeLimitAndIgnoresBattery()
    {
        var xml = TaskSchedulerStartup.BuildTaskXml(ExePath, User);

        Assert.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>", xml);
        Assert.Contains("<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>", xml);
        Assert.Contains("<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>", xml);
    }

    [Fact]
    public void BuildTaskXml_EscapesXmlInThePath()
    {
        const string path = @"C:\A & B\Torrid <1>.exe";

        var xml = TaskSchedulerStartup.BuildTaskXml(path, User);

        Assert.Equal(path, CommandOf(xml));
    }
}
