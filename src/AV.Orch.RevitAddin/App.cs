using System;
using System.Collections.Generic;
using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AV.Orch.RevitAddin
{
    /// <summary>
    /// Native Revit 2023+ application entry point for AV Orch embedded Dockable Chat Pane.
    /// Shares the single "AV Tools" ribbon tab with NX Xplode (av-tools-xnf) and other AV Tools.
    /// </summary>
    public sealed class App : IExternalApplication
    {
        public static readonly Guid DockablePaneGuid = new Guid("D8A4B3C1-7F2E-4A91-9C62-11E5F8A24B09");
        public static readonly DockablePaneId PaneId = new DockablePaneId(DockablePaneGuid);

        private OrchChatPane? _chatPane;

        public Result OnStartup(UIControlledApplication application)
        {
            const string tabName = "AV Tools";
            const string panelName = "AI";

            // 1. Register embedded Dockable Chat Pane inside Revit
            try
            {
                _chatPane = new OrchChatPane();
                application.RegisterDockablePane(PaneId, "AV Orch — AI Chat", _chatPane);
            }
            catch
            {
                // Ignore if already registered during reload
            }

            // 2. Reuse existing "AV Tools" ribbon tab (e.g. NX Xplode) or create it if absent
            try
            {
                application.CreateRibbonTab(tabName);
            }
            catch
            {
                // Tab already exists; seamlessly share it
            }

            // 3. Locate or create "AI" panel on the shared "AV Tools" tab
            RibbonPanel? panel = null;
            try
            {
                List<RibbonPanel> panels = application.GetRibbonPanels(tabName);
                if (panels != null)
                {
                    foreach (RibbonPanel p in panels)
                    {
                        if (string.Equals(p.Name, panelName, StringComparison.OrdinalIgnoreCase))
                        {
                            panel = p;
                            break;
                        }
                    }
                }
            }
            catch
            {
                // Ignore lookup errors across Revit versions
            }

            panel ??= application.CreateRibbonPanel(tabName, panelName);

            // 4. Prevent duplicate button if already added
            foreach (RibbonItem item in panel.GetItems())
            {
                if (string.Equals(item.Name, "btn_AVOrchChat", StringComparison.OrdinalIgnoreCase))
                {
                    return Result.Succeeded;
                }
            }

            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            var buttonData = new PushButtonData(
                "btn_AVOrchChat",
                "AV Orch\nChat",
                assemblyPath,
                typeof(CommandToggleOrchChat).FullName!)
            {
                ToolTip = "Open or hide the embedded AV Orch AI Chat panel inside Revit (Claude Code, OpenAI Codex, Google Gemini CLI).",
                LongDescription = "Talk to your active Revit model using your own Claude, Codex, or Gemini subscription through pyRevit MCP — docked directly inside Revit."
            };

            panel.AddItem(buttonData);
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class CommandToggleOrchChat : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                DockablePane pane = commandData.Application.GetDockablePane(App.PaneId);
                if (pane.IsShown())
                {
                    pane.Hide();
                }
                else
                {
                    pane.Show();
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = "Could not toggle AV Orch Chat panel: " + ex.Message;
                return Result.Failed;
            }
        }
    }
}
