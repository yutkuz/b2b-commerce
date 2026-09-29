using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;
using U1.Business.Testing;

namespace U1.Business.BrowserTests;

public sealed class DealerCheckoutFlowTests : PageTest
{
    [Fact]
    public async Task Lost_checkout_response_keeps_the_same_request_id_after_reload()
    {
        await using var application = await BrowserTestApplication.StartAsync(TestContext.Current.CancellationToken);
        await RunWithDiagnostics(application, async () =>
        {
        await Page.GotoAsync($"{application.BaseUrl}/#login");
        await Page.GetByLabel("E-posta adresi").FillAsync("bayi@u1.local");
        await Page.GetByLabel("Şifre").FillAsync("U1Bayi!2026");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Giriş yap" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("#home$"));

        await Page.GotoAsync($"{application.BaseUrl}/#catalog");
        await Page.GetByLabel("Ürün ara").FillAsync("DG-001");
        var productRow = Page.Locator("tbody tr").Filter(new() { HasText = "DG-001" }).First;
        await Expect(productRow).ToBeVisibleAsync();
        await productRow.Locator("[data-action='add']").ClickAsync();
        await Expect(Page.Locator("[data-cart-count]").First).ToHaveTextAsync("1");

        var requestIds = new List<Guid>();
        var loseFirstResponse = true;
        await Page.RouteAsync("**/api/orders", async route =>
        {
            if (route.Request.Method != "POST")
            {
                await route.ContinueAsync();
                return;
            }

            using var payload = JsonDocument.Parse(route.Request.PostData!);
            requestIds.Add(payload.RootElement.GetProperty("requestId").GetGuid());
            if (loseFirstResponse)
            {
                loseFirstResponse = false;
                await using var committed = await route.FetchAsync();
                Assert.Equal(200, committed.Status);
                await route.AbortAsync();
            }
            else
            {
                await route.ContinueAsync();
            }
        });

        await Page.GotoAsync($"{application.BaseUrl}/#cart");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Siparişi gözden geçir" }).ClickAsync();
        await Page.GetByRole(AriaRole.Dialog)
            .GetByRole(AriaRole.Button, new() { Name = "Siparişi oluştur" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Dialog)
            .GetByRole(AriaRole.Button, new() { Name = "Önceki siparişi sorgula" }))
            .ToBeVisibleAsync();

        await Page.ReloadAsync();
        await Page.GotoAsync($"{application.BaseUrl}/#cart");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Bekleyen siparişi sorgula" }).ClickAsync();
        await Page.GetByRole(AriaRole.Dialog)
            .GetByRole(AriaRole.Button, new() { Name = "Önceki siparişi sorgula" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("#orders$"));
        Assert.Equal(2, requestIds.Count);
        Assert.Equal(requestIds[0], requestIds[1]);
        await Expect(Page.Locator("#page .data-table tbody tr")).ToHaveCountAsync(1);
    
        });
}

    [Fact]
    public async Task Dealer_can_login_add_product_to_cart_and_place_order()
    {
        await using var application = await BrowserTestApplication.StartAsync(TestContext.Current.CancellationToken);
        await RunWithDiagnostics(application, async () =>
        {

        await Page.GotoAsync($"{application.BaseUrl}/#login");

        await Page.GetByLabel("E-posta adresi").FillAsync("bayi@u1.local");
        await Page.GetByLabel("Şifre").FillAsync("U1Bayi!2026");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Giriş yap" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("#home$"));

        await Page.GotoAsync($"{application.BaseUrl}/#catalog");

        var search = Page.GetByLabel("Ürün ara");
        await Expect(search).ToBeVisibleAsync();
        await search.FillAsync("DG-001");

        var productRow = Page
            .Locator("tbody tr")
            .Filter(new() { HasText = "DG-001" })
            .First;

        await Expect(productRow).ToBeVisibleAsync();
        await productRow.Locator("[data-action='add']").ClickAsync();

        await Expect(Page.Locator("[data-cart-count]").First).ToHaveTextAsync("1");

        await Page.GotoAsync($"{application.BaseUrl}/#cart");

        await Expect(
            Page.GetByRole(AriaRole.Heading, new() { Name = "Sepetim" }))
            .ToBeVisibleAsync();

        await Page
            .GetByRole(AriaRole.Button, new() { Name = "Siparişi gözden geçir" })
            .ClickAsync();

        var dialog = Page.GetByRole(AriaRole.Dialog);
        await Expect(dialog).ToBeVisibleAsync();

        await dialog
            .GetByRole(AriaRole.Button, new() { Name = "Siparişi oluştur" })
            .ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("#orders$"));

        await Expect(
            Page.GetByRole(AriaRole.Heading, new() { Name = "Siparişlerim" }))
            .ToBeVisibleAsync();

        await Expect(Page.Locator("#page .data-table tbody")).ToContainTextAsync("U1-");
    
        });
}

    private async Task RunWithDiagnostics(BrowserTestApplication application, Func<Task> body)
    {
        await Context.Tracing.StartAsync(new()
        {
            Screenshots = true,
            ScreenSnapshots = true,
            Snapshots = true,
            Sources = true,
            Title = "U1 Business browser test"
        });

        try
        {
            await body();
            await Context.Tracing.StopAsync();
        }
        catch
        {
            await application.CaptureFailureArtifactsAsync(Page, Context);
            throw;
        }
    }

}

internal sealed class BrowserTestApplication : IAsyncDisposable
{
    private readonly Process process;
    private readonly ConcurrentQueue<string> output;
    private readonly string databaseName;
    private readonly string artifactDirectory;
    private readonly string serverLogPath;

    private BrowserTestApplication(
        Process process,
        ConcurrentQueue<string> output,
        string baseUrl,
        string databaseName,
        string artifactDirectory,
        string serverLogPath)
    {
        this.process = process;
        this.output = output;
        this.databaseName = databaseName;
        this.artifactDirectory = artifactDirectory;
        this.serverLogPath = serverLogPath;
        BaseUrl = baseUrl;
    }

    public string BaseUrl { get; }

    public static async Task<BrowserTestApplication> StartAsync(CancellationToken cancellationToken)
    {
        var repositoryRoot = FindRepositoryRoot();
        var applicationDirectory = Path.Combine(repositoryRoot, "src", "U1.Business");
        var applicationDll = Path.Combine(
            applicationDirectory,
            "bin",
            "Release",
            "net10.0",
            "U1.Business.dll");

        if (!File.Exists(applicationDll))
        {
            throw new FileNotFoundException(
                "Release uygulama çıktısı bulunamadı. Önce solution build çalıştırılmalı.",
                applicationDll);
        }

        var port = ReserveTcpPort();
        var baseUrl = $"http://127.0.0.1:{port}";
        var artifactDirectory = Environment.GetEnvironmentVariable("U1_TEST_ARTIFACTS");
        if (string.IsNullOrWhiteSpace(artifactDirectory))
            artifactDirectory = Path.Combine(repositoryRoot, "TestResults", "browser-artifacts");
        Directory.CreateDirectory(artifactDirectory);
        var artifactId = Guid.NewGuid().ToString("N");
        var serverLogPath = Path.Combine(artifactDirectory, $"server-{artifactId}.log");

        var databaseName = $"U1Business_E2E_{Guid.NewGuid():N}";
        var connectionString =
            $@"Server=(localdb)\MSSQLLocalDB;Database={databaseName};Integrated Security=true;TrustServerCertificate=true;Connect Timeout=30";

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = applicationDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add(applicationDll);
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add(baseUrl);
        startInfo.ArgumentList.Add("--environment");
        startInfo.ArgumentList.Add("Development");

        startInfo.Environment["ConnectionStrings__SqlServer"] = connectionString;
        startInfo.Environment["U1_TEST_DATABASE"] = databaseName;

        var output = new ConcurrentQueue<string>();
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Tarayıcı testi için uygulama başlatılamadı.");

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
                output.Enqueue(args.Data);
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
                output.Enqueue(args.Data);
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var application = new BrowserTestApplication(process, output, baseUrl, databaseName, artifactDirectory, serverLogPath);

        try
        {
            await application.WaitUntilReady(cancellationToken);
            return application;
        }
        catch
        {
            await application.DisposeAsync();
            throw;
        }
    }

    private async Task WaitUntilReady(CancellationToken cancellationToken)
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(2)
        };

        for (var attempt = 0; attempt < 120; attempt++)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"Uygulama beklenmeden kapandı.{Environment.NewLine}{RecentOutput()}");
            }

            try
            {
                using var response = await client.GetAsync($"{BaseUrl}/api/test-environment", cancellationToken);
                if (response.StatusCode == HttpStatusCode.OK)
                    return;
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new TimeoutException(
            $"Tarayıcı test uygulaması 60 saniye içinde hazır olmadı.{Environment.NewLine}{RecentOutput()}");
    }


    public async Task CaptureFailureArtifactsAsync(IPage page, IBrowserContext context)
    {
        Directory.CreateDirectory(artifactDirectory);
        var suffix = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        var screenshotPath = Path.Combine(artifactDirectory, $"failure-{suffix}.png");
        var tracePath = Path.Combine(artifactDirectory, $"trace-{suffix}.zip");

        try
        {
            await page.ScreenshotAsync(new() { Path = screenshotPath, FullPage = true });
        }
        catch
        {
        }

        try
        {
            await context.Tracing.StopAsync(new() { Path = tracePath });
        }
        catch
        {
        }
    }


    private string RecentOutput() =>
        string.Join(
            Environment.NewLine,
            output.Reverse().Take(40).Reverse());

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }
        finally
        {
            try
            {
                Directory.CreateDirectory(artifactDirectory);
                File.WriteAllLines(serverLogPath, output);
                process.Dispose();
            }
            finally
            {
                await TestDatabaseLifecycle.DropUncancellableAsync(databaseName, "U1Business_E2E_");
            }
        }
    }

    private static int ReserveTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "U1.Business.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "U1.Business.sln üst dizinlerde bulunamadı.");
    }
}
