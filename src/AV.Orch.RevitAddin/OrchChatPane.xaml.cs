using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Autodesk.Revit.UI;

namespace AV.Orch.RevitAddin
{
    public partial class OrchChatPane : Page, IDockablePaneProvider
    {
        private CancellationTokenSource? _activeTurnCts;
        private bool _hasPriorTurn;

        public OrchChatPane()
        {
            InitializeComponent();
            Loaded += async (_, _) => await RefreshProviderStatusAsync();
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = this;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Right
            };
        }

        private string SelectedProviderTag =>
            (ProviderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "claude";

        private string SelectedProviderLabel =>
            (ProviderCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Anthropic Claude Code";

        private async void OnProviderChanged(object sender, SelectionChangedEventArgs e)
        {
            _hasPriorTurn = false;
            if (IsLoaded)
            {
                await RefreshProviderStatusAsync();
            }
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            await RefreshProviderStatusAsync();
        }

        private async Task RefreshProviderStatusAsync()
        {
            string tag = SelectedProviderTag;
            ProviderStatusText.Text = $"Checking {SelectedProviderLabel}...";

            string? exe = await Task.Run(() => FindInPath(tag));
            if (string.IsNullOrWhiteSpace(exe))
            {
                ProviderStatusText.Text = $"{SelectedProviderLabel} CLI ('{tag}') is not installed. Click 'Install Latest CLI'.";
                BtnInstallCli.Visibility = Visibility.Visible;
                return;
            }

            string? version = await Task.Run(() => RunQuickOutput(exe!, "--version"));
            ProviderStatusText.Text = $"{SelectedProviderLabel} detected ({(string.IsNullOrWhiteSpace(version) ? "installed" : version)}). Ready for chat.";
        }

        private async void OnInstallCliClick(object sender, RoutedEventArgs e)
        {
            string tag = SelectedProviderTag;
            (string pkg, string url) = tag switch
            {
                "codex" => ("@openai/codex", "https://developers.openai.com/"),
                "gemini" => ("@google/gemini-cli", "https://geminicli.com/docs/get-started/"),
                _ => ("@anthropic-ai/claude-code", "https://code.claude.com/docs/en/overview")
            };

            var confirm = MessageBox.Show(
                $"Install latest {SelectedProviderLabel}?\n\nPackage: {pkg}\nOfficial Source: {url}\n\nClick Yes to run 'npm install -g {pkg}' (or open the official page if npm is not installed).",
                "Install Latest AI CLI",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            string? npm = await Task.Run(() => FindInPath("npm"));
            if (string.IsNullOrWhiteSpace(npm))
            {
                LaunchShell(url, string.Empty);
                AppendTranscript("System", $"npm was not found on PATH. Opened official install page: {url}");
                return;
            }

            ProviderStatusText.Text = $"Installing {pkg} via npm...";
            BtnInstallCli.IsEnabled = false;
            try
            {
                (int exitCode, string stdout, string stderr) = await Task.Run(() =>
                    RunProcessCapture(npm!, $"install -g {pkg}", TimeSpan.FromMinutes(3), CancellationToken.None));

                if (exitCode == 0)
                {
                    AppendTranscript("System", $"Installed latest {SelectedProviderLabel} ({pkg}). Click 'Sign In' if not yet authenticated.");
                }
                else
                {
                    AppendTranscript("System", $"Install failed (exit {exitCode}): {(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr)}");
                }
            }
            finally
            {
                BtnInstallCli.IsEnabled = true;
                await RefreshProviderStatusAsync();
            }
        }

        private async void OnSignInClick(object sender, RoutedEventArgs e)
        {
            string tag = SelectedProviderTag;
            string? exe = await Task.Run(() => FindInPath(tag));
            if (string.IsNullOrWhiteSpace(exe))
            {
                MessageBox.Show($"{SelectedProviderLabel} CLI is not installed yet. Click 'Install Latest CLI' first.", "CLI Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string loginArgs = tag switch
            {
                "claude" => "auth login",
                "codex" => "login",
                _ => string.Empty
            };

            LaunchShell(exe!, loginArgs);
            AppendTranscript("System", $"Launched official login flow for {SelectedProviderLabel}. Complete sign-in in the terminal/browser window.");
        }

        private async void OnConnectMcpClick(object sender, RoutedEventArgs e)
        {
            string tag = SelectedProviderTag;
            string? pyrevit = await Task.Run(() => FindInPath("pyrevit"));
            if (string.IsNullOrWhiteSpace(pyrevit))
            {
                AppendTranscript("System", "pyRevit CLI was not found on PATH. Open Settings to configure pyRevit 7.1+.");
                return;
            }

            ProviderStatusText.Text = $"Connecting {SelectedProviderLabel} to pyRevit MCP...";
            await Task.Run(() => RunProcessCapture(pyrevit!, "configs agent enable", TimeSpan.FromSeconds(15), CancellationToken.None));

            if (tag == "claude" || tag == "codex")
            {
                (int code, string stdout, string stderr) = await Task.Run(() =>
                    RunProcessCapture(pyrevit!, $"mcp install {tag}", TimeSpan.FromSeconds(20), CancellationToken.None));
                AppendTranscript("System", code == 0
                    ? $"Registered pyRevit MCP server with {SelectedProviderLabel}."
                    : $"MCP registration output: {(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr)}");
            }
            else
            {
                // For Gemini CLI, open AV Orch Settings where surgical ~/.gemini/settings.json registration runs with backup/rollback
                OpenAvOrchDesktop();
                AppendTranscript("System", "Opened AV Orch Desktop to register Gemini CLI into ~/.gemini/settings.json with automatic backup.");
            }

            await RefreshProviderStatusAsync();
        }

        private void OnOpenSettingsClick(object sender, RoutedEventArgs e) => OpenAvOrchDesktop();

        private void OnNewChatClick(object sender, RoutedEventArgs e)
        {
            _hasPriorTurn = false;
            ChatTranscript.Text = $"Started new {SelectedProviderLabel} chat session.";
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            _activeTurnCts?.Cancel();
        }

        private void OnPromptKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                e.Handled = true;
                OnSendClick(sender, e);
            }
        }

        private async void OnSendClick(object sender, RoutedEventArgs e)
        {
            string prompt = PromptInput.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(prompt))
            {
                return;
            }

            string tag = SelectedProviderTag;
            string? exe = await Task.Run(() => FindInPath(tag));
            if (string.IsNullOrWhiteSpace(exe))
            {
                AppendTranscript("System", $"'{tag}' CLI is not installed. Click 'Install Latest CLI' above.");
                return;
            }

            PromptInput.Text = string.Empty;
            AppendTranscript("You", prompt);

            BtnSend.IsEnabled = false;
            BtnCancel.IsEnabled = true;
            _activeTurnCts?.Dispose();
            _activeTurnCts = new CancellationTokenSource();

            try
            {
                string escaped = prompt.Replace("\"", "\\\"");
                string args = tag switch
                {
                    "claude" => _hasPriorTurn ? $"-p --continue \"{escaped}\"" : $"-p \"{escaped}\"",
                    "codex" => $"exec \"{escaped}\"",
                    _ => $"-p \"{escaped}\""
                };

                (int exitCode, string stdout, string stderr) = await Task.Run(() =>
                    RunProcessCapture(exe!, args, TimeSpan.FromMinutes(5), _activeTurnCts.Token));

                if (_activeTurnCts.IsCancellationRequested)
                {
                    AppendTranscript("System", "Request cancelled.");
                }
                else if (exitCode == 0)
                {
                    _hasPriorTurn = true;
                    AppendTranscript(SelectedProviderLabel, string.IsNullOrWhiteSpace(stdout) ? "(No text output returned)" : stdout);
                }
                else
                {
                    string err = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                    AppendTranscript("Error", $"Provider exited with code {exitCode}:\n{err}");
                }
            }
            catch (Exception ex)
            {
                AppendTranscript("Error", ex.Message);
            }
            finally
            {
                BtnSend.IsEnabled = true;
                BtnCancel.IsEnabled = false;
            }
        }

        private void AppendTranscript(string speaker, string text)
        {
            string stamp = DateTime.Now.ToString("HH:mm:ss");
            string block = $"\n\n[{stamp}] {speaker}:\n{text.Trim()}";
            ChatTranscript.AppendText(block);
            ChatScroll.ScrollToEnd();
        }

        private static void OpenAvOrchDesktop()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string exe = Path.Combine(local, "Artificial Vision", "AV Orch", "AVOrch.exe");
            if (File.Exists(exe))
            {
                LaunchShell(exe, string.Empty);
            }
            else
            {
                MessageBox.Show("AVOrch.exe was not found in %LOCALAPPDATA%\\Artificial Vision\\AV Orch.", "AV Orch", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private static void LaunchShell(string fileName, string arguments)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Ignore shell launch errors
            }
        }

        private static string? FindInPath(string command)
        {
            (int code, string stdout, _) = RunProcessCapture("where.exe", command, TimeSpan.FromSeconds(5), CancellationToken.None);
            if (code != 0 || string.IsNullOrWhiteSpace(stdout))
            {
                return null;
            }

            string[] lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return lines.Length > 0 ? lines[0].Trim() : null;
        }

        private static string? RunQuickOutput(string fileName, string args)
        {
            (int code, string stdout, _) = RunProcessCapture(fileName, args, TimeSpan.FromSeconds(8), CancellationToken.None);
            if (code != 0 || string.IsNullOrWhiteSpace(stdout))
            {
                return null;
            }

            return stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        }

        private static (int ExitCode, string StdOut, string StdErr) RunProcessCapture(
            string fileName,
            string arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var proc = new Process { StartInfo = psi };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

            if (!proc.Start())
            {
                return (-1, string.Empty, $"Failed to start {fileName}.");
            }

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            int waitedMs = 0;
            const int sliceMs = 150;
            int maxMs = (int)timeout.TotalMilliseconds;
            while (!proc.WaitForExit(sliceMs))
            {
                waitedMs += sliceMs;
                if (cancellationToken.IsCancellationRequested || waitedMs >= maxMs)
                {
                    try { proc.Kill(); } catch { }
                    return (-1, stdout.ToString().Trim(), cancellationToken.IsCancellationRequested ? "Cancelled." : "Timed out.");
                }
            }

            proc.WaitForExit();
            return (proc.ExitCode, stdout.ToString().Trim(), stderr.ToString().Trim());
        }
    }
}
