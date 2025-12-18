using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Web.WebView2.WinForms;

namespace FSZBWebGuiLauncher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Start Kestrel serving wwwroot (copied into output)
        var cts = new CancellationTokenSource();
        var hostTask = StartWebHostAsync(cts.Token);

        using var form = new MainForm(onClose: () => cts.Cancel());
        Application.Run(form);

        // Stop the web host when the window closes
        cts.Cancel();
        hostTask.GetAwaiter().GetResult();
    }

    private static async Task StartWebHostAsync(CancellationToken token)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://0.0.0.0:5080");

        // Serve files from the local wwwroot (copied via csproj linkage)
        var wwwrootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        builder.Services.AddRouting();

        var app = builder.Build();
        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(wwwrootPath)
        });
        app.MapFallbackToFile("index.html");

        await app.StartAsync(token);
        await app.WaitForShutdownAsync(token);
    }
}

internal sealed class MainForm : Form
{
    private readonly WebView2 _webView;
    private readonly Action _onClose;

    public MainForm(Action onClose)
    {
        _onClose = onClose;
        Text = "RC Car HUD";
        Width = 1280;
        Height = 720;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None; // Chromeless
        WindowState = FormWindowState.Maximized; // Fullscreen
        TopMost = false;

        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = System.Drawing.Color.Black
        };

        Controls.Add(_webView);

        Load += (_, _) => InitializeAsync();
        FormClosed += (_, _) => _onClose();
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
        };
    }

        private async void InitializeAsync()
    {
        try
        {
                await _webView.EnsureCoreWebView2Async();

                // Auto-allow camera/microphone permissions for our local host
                _webView.CoreWebView2.PermissionRequested += (_, args) =>
                {
                    if (args.PermissionKind == Microsoft.Web.WebView2.Core.CoreWebView2PermissionKind.Camera ||
                        args.PermissionKind == Microsoft.Web.WebView2.Core.CoreWebView2PermissionKind.Microphone)
                    {
                        args.State = Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Allow;
                        args.Handled = true;
                        return;
                    }
                };

                _webView.CoreWebView2.Navigate("http://localhost:5080");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to initialize WebView2: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _onClose();
        }
    }
}
