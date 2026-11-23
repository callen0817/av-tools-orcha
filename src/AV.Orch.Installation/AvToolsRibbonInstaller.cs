using AV.Orch.Core.Abstractions;
using AV.Orch.Core.Models;

namespace AV.Orch.Installation;

public sealed record AvToolsRibbonStatus(
    bool IsAvToolsRibbonPresent,
    bool IsAvOrchButtonInstalled,
    IReadOnlyList<string> DetectedOtherAvTools,
    string ExtensionRootPath,
    string ButtonDirectoryPath,
    string Summary);

/// <summary>
/// Manages the shared "AV Tools" Revit ribbon tab (matching Artificial Vision's av-tools-xnf / NX Xplode convention).
/// - If AV Tools is already installed (e.g. NX Xplode), attaches AV Orch to the SAME "AV Tools" ribbon without creating a duplicate.
/// - If no AV Tools are installed, creates the "AV Tools" ribbon extension and adds the AV Orch tool.
/// - On uninstall, removes ONLY the AV Orch button and preserves NX Xplode and any other AV Tools.
/// </summary>
public sealed class AvToolsRibbonInstaller
{
    public const string ExtensionFolderName = "AV Tools.extension";
    public const string TabFolderName = "AV Tools.tab";
    public const string PanelFolderName = "AI.panel";
    public const string ButtonFolderName = "AV Orch.pushbutton";

    private const string DefaultExtensionJson = """
{
  "type": "extension",
  "name": "AV Tools",
  "description": "Artificial Vision Revit Tools Suite (NX Xplode, AV Orch, and future AV Tools)",
  "author": "Artificial Vision, Inc.",
  "version": "1.0.0"
}
""";

    private const string BundleYamlContent = """
title: "AV Orch"
tooltip: |
  Launch AV Orch — Artificial Vision's local AI-to-Revit MCP connector for
  OpenAI Codex, Google Gemini CLI, and Anthropic Claude Code (Revit 2023+).
author: "Artificial Vision, Inc."
highlight: new
""";

    private const string ScriptPyContent = """
# -*- coding: utf-8 -*-
# Launch AV Orch from the shared AV Tools Revit ribbon tab.
__title__ = "AV Orch"
__author__ = "Artificial Vision, Inc."

import os
import subprocess
from pyrevit import forms


def find_av_orch_exe():
    local_app_data = os.environ.get("LOCALAPPDATA", "")
    program_files = os.environ.get("ProgramFiles", "")
    candidates = [
        os.path.join(local_app_data, "Artificial Vision", "AV Orch", "AVOrch.exe"),
        os.path.join(program_files, "Artificial Vision", "AV Orch", "AVOrch.exe"),
    ]
    for path in candidates:
        if path and os.path.isfile(path):
            return path
    return None


def main():
    exe_path = find_av_orch_exe()
    if not exe_path:
        forms.alert(
            "AV Orch desktop application (AVOrch.exe) was not found.\n\n"
            "Please install or repair AV Orch using the AV Orch installer.",
            title="AV Orch — Not Found",
            warn_icon=True,
        )
        return

    try:
        subprocess.Popen([exe_path], cwd=os.path.dirname(exe_path))
    except Exception as exc:
        forms.alert(
            "Could not launch AV Orch:\n{0}".format(exc),
            title="AV Orch — Launch Error",
            warn_icon=True,
        )


if __name__ == "__main__":
    main()
""";

    private readonly IFileSystem _fileSystem;
    private readonly IEnvironmentProvider _environment;
    private readonly IProcessRunner _processRunner;
    private readonly IAppLogger _logger;
    private readonly IAuditLogService _auditLog;

    public AvToolsRibbonInstaller(
        IFileSystem fileSystem,
        IEnvironmentProvider environment,
        IProcessRunner processRunner,
        IAppLogger logger,
        IAuditLogService auditLog)
    {
        _fileSystem = fileSystem;
        _environment = environment;
        _processRunner = processRunner;
        _logger = logger;
        _auditLog = auditLog;
    }

    public string ExtensionsBaseDirectory =>
        Path.Combine(_environment.RoamingAppDataDirectory, "pyRevit", "Extensions");

    public string ExtensionRootDirectory =>
        Path.Combine(ExtensionsBaseDirectory, ExtensionFolderName);

    public string TabDirectory =>
        Path.Combine(ExtensionRootDirectory, TabFolderName);

    public string PanelDirectory =>
        Path.Combine(TabDirectory, PanelFolderName);

    public string ButtonDirectory =>
        Path.Combine(PanelDirectory, ButtonFolderName);

    public AvToolsRibbonStatus InspectRibbon()
    {
        var ribbonPresent = _fileSystem.DirectoryExists(TabDirectory);
        var avOrchPresent = _fileSystem.FileExists(Path.Combine(ButtonDirectory, "script.py")) &&
                            _fileSystem.FileExists(Path.Combine(ButtonDirectory, "bundle.yaml"));

        var otherTools = new List<string>();
        if (ribbonPresent)
        {
            foreach (var panelDir in _fileSystem.EnumerateDirectories(TabDirectory))
            {
                foreach (var itemDir in _fileSystem.EnumerateDirectories(panelDir))
                {
                    var itemName = Path.GetFileName(itemDir);
                    if (!string.Equals(itemName, ButtonFolderName, StringComparison.OrdinalIgnoreCase))
                    {
                        otherTools.Add(itemName);
                    }
                }
            }
        }

        string summary;
        if (avOrchPresent && otherTools.Count > 0)
        {
            summary = $"AV Orch is installed on the shared 'AV Tools' ribbon alongside {otherTools.Count} other AV Tool(s): {string.Join(", ", otherTools)}.";
        }
        else if (avOrchPresent)
        {
            summary = "AV Orch is installed on the 'AV Tools' ribbon (AI panel).";
        }
        else if (ribbonPresent)
        {
            summary = $"Existing 'AV Tools' ribbon detected ({otherTools.Count} tool(s)); AV Orch button not yet added.";
        }
        else
        {
            summary = "No 'AV Tools' ribbon installed yet; installing AV Orch will create the 'AV Tools' ribbon and add the AV Orch tool.";
        }

        return new AvToolsRibbonStatus(
            ribbonPresent,
            avOrchPresent,
            otherTools,
            ExtensionRootDirectory,
            ButtonDirectory,
            summary);
    }

    public async Task<OperationResult> InstallOrUpdateRibbonAsync(string? pyRevitExecutablePath = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var preExistingRibbon = _fileSystem.DirectoryExists(TabDirectory);

            _fileSystem.CreateDirectory(ExtensionsBaseDirectory);
            _fileSystem.CreateDirectory(ExtensionRootDirectory);
            _fileSystem.CreateDirectory(TabDirectory);
            _fileSystem.CreateDirectory(PanelDirectory);
            _fileSystem.CreateDirectory(ButtonDirectory);

            // Preserve existing extension.json if NX Xplode or another AV Tool already created it
            var extensionJsonPath = Path.Combine(ExtensionRootDirectory, "extension.json");
            if (!_fileSystem.FileExists(extensionJsonPath))
            {
                _fileSystem.WriteAllText(extensionJsonPath, DefaultExtensionJson + Environment.NewLine);
            }

            _fileSystem.WriteAllText(Path.Combine(ButtonDirectory, "bundle.yaml"), BundleYamlContent + Environment.NewLine);
            _fileSystem.WriteAllText(Path.Combine(ButtonDirectory, "script.py"), ScriptPyContent + Environment.NewLine);

            // Register search path with pyRevit CLI if available
            var pyRevitExe = pyRevitExecutablePath ?? await _processRunner.FindExecutableInPathAsync("pyrevit", cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(pyRevitExe))
            {
                await _processRunner.RunAsync(
                    pyRevitExe,
                    new[] { "extensions", "paths", "add", ExtensionsBaseDirectory },
                    timeout: TimeSpan.FromSeconds(15),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            // Also register native Revit 2023+ Dockable Chat Pane add-in manifests (AVTools_OrchDock.addin)
            RegisterNativeDockablePaneAddins();

            var message = preExistingRibbon
                ? "Added AV Orch (ribbon + embedded Revit chat pane) to the existing shared 'AV Tools' Revit ribbon (all existing AV Tools preserved)."
                : "Created the 'AV Tools' Revit ribbon and added the AV Orch tool + embedded Revit chat pane.";

            _logger.Info("AvToolsRibbon", message);
            _auditLog.Record("InstallAvToolsRibbon", "System", ButtonDirectory, true, message);
            return OperationResult.Ok(message);
        }
        catch (Exception ex)
        {
            _logger.Error("AvToolsRibbon", "Failed to install AV Tools ribbon button", ex);
            _auditLog.Record("InstallAvToolsRibbon", "System", ButtonDirectory, false, ex.Message);
            return OperationResult.Fail($"Failed to install AV Orch onto the AV Tools ribbon: {ex.Message}", "RIBBON_INSTALL_FAILED");
        }
    }

    public Task<OperationResult> RemoveAvOrchFromRibbonAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            RemoveNativeDockablePaneAddins();

            if (!_fileSystem.DirectoryExists(ButtonDirectory))
            {
                return Task.FromResult(OperationResult.Ok("AV Orch button was not present on the AV Tools ribbon."));
            }

            _fileSystem.DeleteDirectory(ButtonDirectory, recursive: true);

            // Clean up AI.panel ONLY if empty
            if (_fileSystem.DirectoryExists(PanelDirectory) &&
                !_fileSystem.EnumerateFiles(PanelDirectory).Any() &&
                !_fileSystem.EnumerateDirectories(PanelDirectory).Any())
            {
                _fileSystem.DeleteDirectory(PanelDirectory, recursive: false);
            }

            // Clean up AV Tools.tab and AV Tools.extension ONLY if no other AV Tools (e.g. NX Xplode) remain
            var remainingPanels = _fileSystem.DirectoryExists(TabDirectory)
                ? _fileSystem.EnumerateDirectories(TabDirectory).ToList()
                : new List<string>();

            if (remainingPanels.Count == 0 && _fileSystem.DirectoryExists(TabDirectory))
            {
                _fileSystem.DeleteDirectory(TabDirectory, recursive: true);
                var extJson = Path.Combine(ExtensionRootDirectory, "extension.json");
                if (_fileSystem.FileExists(extJson))
                {
                    _fileSystem.DeleteFile(extJson);
                }

                if (!_fileSystem.EnumerateFiles(ExtensionRootDirectory).Any() &&
                    !_fileSystem.EnumerateDirectories(ExtensionRootDirectory).Any())
                {
                    _fileSystem.DeleteDirectory(ExtensionRootDirectory, recursive: false);
                }
            }

            var message = remainingPanels.Count > 0
                ? "Removed AV Orch from the 'AV Tools' ribbon while preserving existing AV Tools plugins."
                : "Removed AV Orch and cleaned up empty 'AV Tools' ribbon folder.";

            _logger.Info("AvToolsRibbon", message);
            _auditLog.Record("RemoveAvToolsRibbon", "Uninstaller", ButtonDirectory, true, message);
            return Task.FromResult(OperationResult.Ok(message));
        }
        catch (Exception ex)
        {
            _logger.Error("AvToolsRibbon", "Failed to remove AV Orch from AV Tools ribbon", ex);
            return Task.FromResult(OperationResult.Fail($"Failed to remove AV Orch ribbon button: {ex.Message}", "RIBBON_REMOVE_FAILED"));
        }
    }

    public int RegisterNativeDockablePaneAddins()
    {
        var installRoot = Path.Combine(_environment.LocalAppDataDirectory, "Artificial Vision", "AV Orch");
        var net48Dll = Path.Combine(installRoot, "addin", "net48", "AVTools_OrchDock.dll");
        var net8Dll = Path.Combine(installRoot, "addin", "net8.0-windows", "AVTools_OrchDock.dll");
        var addinsRoot = Path.Combine(_environment.RoamingAppDataDirectory, "Autodesk", "Revit", "Addins");

        if (!_fileSystem.DirectoryExists(addinsRoot))
        {
            return 0;
        }

        var registeredCount = 0;
        foreach (var yearDir in _fileSystem.EnumerateDirectories(addinsRoot))
        {
            var folderName = Path.GetFileName(yearDir);
            if (!int.TryParse(folderName, out var year) || year < 2023)
            {
                continue;
            }

            var dllPath = year <= 2024 ? net48Dll : net8Dll;
            if (!_fileSystem.FileExists(dllPath))
            {
                continue;
            }

            var manifestPath = Path.Combine(yearDir, "AVTools_OrchDock.addin");
            var manifestXml = $"""
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>AV Tools - AV Orch Chat</Name>
    <Assembly>{dllPath}</Assembly>
    <AddInId>D8A4B3C1-7F2E-4A91-9C62-11E5F8A24B09</AddInId>
    <FullClassName>AV.Orch.RevitAddin.App</FullClassName>
    <VendorId>AVTL</VendorId>
    <VendorDescription>Artificial Vision, Inc. - AV Tools</VendorDescription>
  </AddIn>
</RevitAddIns>
""";
            _fileSystem.WriteAllText(manifestPath, manifestXml + Environment.NewLine);
            registeredCount++;
        }

        return registeredCount;
    }

    public void RemoveNativeDockablePaneAddins()
    {
        var addinsRoot = Path.Combine(_environment.RoamingAppDataDirectory, "Autodesk", "Revit", "Addins");
        if (!_fileSystem.DirectoryExists(addinsRoot))
        {
            return;
        }

        foreach (var yearDir in _fileSystem.EnumerateDirectories(addinsRoot))
        {
            var manifestPath = Path.Combine(yearDir, "AVTools_OrchDock.addin");
            if (_fileSystem.FileExists(manifestPath))
            {
                _fileSystem.DeleteFile(manifestPath);
            }
        }
    }
}
