using AV.Orch.Core.Services;
using AV.Orch.Installation;
using AV.Orch.Security;
using AV.Orch.UnitTests.Fakes;
using Xunit;

namespace AV.Orch.UnitTests;

public sealed class AvToolsRibbonInstallerTests
{
    [Fact]
    public async Task FreshInstall_CreatesAvToolsRibbonAndAddsAvOrchTool_AndCleanlyRemovesWhenAlone()
    {
        var fs = new InMemoryFileSystem();
        var env = new FakeEnvironmentProvider();
        var proc = new FakeProcessRunner();
        proc.PathExecutables["pyrevit"] = @"C:\Program Files\pyRevit-Master\bin\pyrevit.exe";
        var redactor = new SecretRedactor();
        var logger = new StructuredAppLogger(fs, env, redactor);
        var audit = new AuditLogService(fs, env, redactor);

        var installer = new AvToolsRibbonInstaller(fs, env, proc, logger, audit);

        var before = installer.InspectRibbon();
        Assert.False(before.IsAvToolsRibbonPresent);
        Assert.False(before.IsAvOrchButtonInstalled);

        var installRes = await installer.InstallOrUpdateRibbonAsync();
        Assert.True(installRes.Succeeded, installRes.Message);
        Assert.Contains("Created the 'AV Tools' Revit ribbon", installRes.Message);

        var after = installer.InspectRibbon();
        Assert.True(after.IsAvToolsRibbonPresent);
        Assert.True(after.IsAvOrchButtonInstalled);
        Assert.Empty(after.DetectedOtherAvTools);
        Assert.True(fs.FileExists(Path.Combine(installer.ExtensionRootDirectory, "extension.json")));
        Assert.True(fs.FileExists(Path.Combine(installer.ButtonDirectory, "bundle.yaml")));
        var scriptPath = Path.Combine(installer.ButtonDirectory, "script.py");
        Assert.True(fs.FileExists(scriptPath));
        Assert.DoesNotContain("\\\"", fs.ReadAllText(scriptPath));

        // Removing when alone cleans up empty AV Tools folders
        var removeRes = await installer.RemoveAvOrchFromRibbonAsync();
        Assert.True(removeRes.Succeeded, removeRes.Message);
        Assert.False(fs.DirectoryExists(installer.ButtonDirectory));
        Assert.False(fs.DirectoryExists(installer.TabDirectory));
    }

    [Fact]
    public async Task CoexistenceWithNxXplode_SharesSameAvToolsRibbon_AndPreservesNxXplodeOnUninstall()
    {
        var fs = new InMemoryFileSystem();
        var env = new FakeEnvironmentProvider();
        var proc = new FakeProcessRunner();
        var redactor = new SecretRedactor();
        var logger = new StructuredAppLogger(fs, env, redactor);
        var audit = new AuditLogService(fs, env, redactor);

        var installer = new AvToolsRibbonInstaller(fs, env, proc, logger, audit);

        // Simulate existing NX Xplode installation from av-tools-xnf
        var existingExtJson = Path.Combine(installer.ExtensionRootDirectory, "extension.json");
        var nxXplodeDir = Path.Combine(installer.TabDirectory, "Family.panel", "NX Xplode.pushbutton");
        var nxScript = Path.Combine(nxXplodeDir, "script.py");
        const string nxExtJsonContent = """{"type":"extension","name":"NX Xplode"}""";
        fs.WriteAllText(existingExtJson, nxExtJsonContent);
        fs.WriteAllText(nxScript, "# NX Xplode");

        // Install AV Orch -> must attach to the SAME AV Tools.tab without creating a second ribbon
        var installRes = await installer.InstallOrUpdateRibbonAsync();
        Assert.True(installRes.Succeeded, installRes.Message);
        Assert.Contains("existing shared 'AV Tools' Revit ribbon", installRes.Message);

        // Verify only ONE extension folder exists under pyRevit/Extensions
        var extensions = fs.EnumerateDirectories(installer.ExtensionsBaseDirectory).ToList();
        Assert.Single(extensions);
        Assert.Equal("AV Tools.extension", Path.GetFileName(extensions[0]));

        // Verify existing extension.json and NX Xplode are untouched
        Assert.Equal(nxExtJsonContent, fs.ReadAllText(existingExtJson));
        Assert.True(fs.FileExists(nxScript));

        var status = installer.InspectRibbon();
        Assert.True(status.IsAvToolsRibbonPresent);
        Assert.True(status.IsAvOrchButtonInstalled);
        Assert.Contains("NX Xplode.pushbutton", status.DetectedOtherAvTools);

        // Uninstall AV Orch -> removes ONLY AV Orch.pushbutton, leaving NX Xplode and AV Tools.tab intact
        var removeRes = await installer.RemoveAvOrchFromRibbonAsync();
        Assert.True(removeRes.Succeeded, removeRes.Message);
        Assert.False(fs.DirectoryExists(installer.ButtonDirectory));
        Assert.True(fs.DirectoryExists(installer.TabDirectory));
        Assert.True(fs.FileExists(nxScript));
        Assert.Equal(nxExtJsonContent, fs.ReadAllText(existingExtJson));
    }
}
