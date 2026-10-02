using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;
using U1.Business.Services;
using U1.Business.Testing;

namespace U1.Business.BrowserTests;

public sealed class DealerCheckoutFlowTests : PageTest
{
    [Fact]
    public async Task Admin_can_preview_apply_and_export_product_csv_in_browser()
    {
        await using var application =
            await BrowserTestApplication.StartAsync(TestContext.Current.CancellationToken);

        await RunWithDiagnostics(application, async () =>
        {
            await LoginAsync(application, "admin@u1.local", "U1Admin!2026");
            await Page.GotoAsync($"{application.BaseUrl}/#admin-products");

            var code = "CSV-BR-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            var csv = "code,name,description,brand,manufacturerCode,specialCode1,specialCode2,imageUrl,stock,criticalStock,price,category,rowVersion,stockReason\n"
                + $"{code},Tarayıcı CSV ürünü,Açıklama,CI,CI-M,,,/images/product.svg,3,1,12.50,Diagnostik cihazlar,,İlk stok";
            await Page.GetByRole(AriaRole.Button, new() { Name = "CSV içe aktar" }).ClickAsync();
            var dialog = Page.GetByRole(AriaRole.Dialog);
            await dialog.Locator("input[type=file]").SetInputFilesAsync(new FilePayload
            {
                Name = "products.csv",
                MimeType = "text/csv",
                Buffer = Encoding.UTF8.GetBytes(csv)
            });
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Değişiklikleri önizle" })
                .ClickAsync();

            dialog = Page.GetByRole(AriaRole.Dialog);
            await Expect(dialog).ToContainTextAsync(code);
            await Expect(dialog).ToContainTextAsync("Tarayıcı CSV ürünü");
            await Expect(dialog).ToContainTextAsync("12.5");
            await dialog.GetByRole(AriaRole.Button,
                new() { Name = "Önizlenen değişiklikleri uygula" }).ClickAsync();
            await Expect(Page.GetByRole(AriaRole.Dialog)).Not.ToBeVisibleAsync();

            await Page.GotoAsync($"{application.BaseUrl}/#admin-products?q={code}");
            await Expect(Page.Locator("tbody tr").Filter(new() { HasText = code }))
                .ToContainTextAsync("Tarayıcı CSV ürünü");

            var downloadTask = Page.WaitForDownloadAsync();
            await Page.GetByRole(AriaRole.Link, new() { Name = "CSV dışa aktar" }).ClickAsync();
            var download = await downloadTask;
            Assert.Equal("u1-products.csv", download.SuggestedFilename);
        });
    }

    [Fact]
    public async Task Order_print_readd_preview_and_private_admin_note_work_in_browser()
    {
        await using var application =
            await BrowserTestApplication.StartAsync(TestContext.Current.CancellationToken);

        await RunWithDiagnostics(application, async () =>
        {
            await LoginAsync(application, "bayi@u1.local", "U1Bayi!2026");
            await AddProductAsync(application, "DG-001");
            await Page.GotoAsync($"{application.BaseUrl}/#cart");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Siparişi gözden geçir" }).ClickAsync();
            await Page.GetByRole(AriaRole.Dialog)
                .GetByRole(AriaRole.Button, new() { Name = "Siparişi oluştur" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(new Regex("#orders$"));

            using (var adminApi = await LoginApiAsync(application, "admin@u1.local", "U1Admin!2026"))
            {
                var currentProduct = await GetProductAsync(adminApi, "DG-001");
                await UpdateProductAsync(adminApi, currentProduct, price: 25_500m);
            }

            var orderRow = Page.Locator("#page .data-table tbody tr").First;
            var orderButton = orderRow.Locator("[data-action='order']").First;
            var orderNumber = (await orderButton.InnerTextAsync()).Trim();
            await orderButton.ClickAsync();
            var dialog = Page.GetByRole(AriaRole.Dialog);
            await Expect(dialog).ToContainTextAsync(orderNumber);
            await Expect(dialog).ToContainTextAsync("24.900,00");
            await Expect(dialog.GetByRole(AriaRole.Heading, new() { Name = "Durum geçmişi" }))
                .ToBeVisibleAsync();
            await Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Yazdır / PDF" }))
                .ToBeVisibleAsync();

            await Page.EmulateMediaAsync(new() { Media = Media.Print });
            Assert.Equal("none", await Page.Locator("#app")
                .EvaluateAsync<string>("element => getComputedStyle(element).display"));
            Assert.Equal("block", await Page.Locator("#dialog")
                .EvaluateAsync<string>("element => getComputedStyle(element).display"));
            Assert.True((await Page.PdfAsync(new() { PrintBackground = true })).Length > 1000);
            await Page.EmulateMediaAsync(new() { Media = Media.Screen });

            await dialog.GetByRole(AriaRole.Button, new() { Name = "Yeniden sepete ekle" }).ClickAsync();
            await Expect(dialog).ToContainTextAsync("Güncel fiyat");
            await Expect(dialog).ToContainTextAsync("25.500,00");
            await Expect(dialog.GetByRole(AriaRole.Checkbox)).ToBeCheckedAsync();
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Seçilenleri sepete ekle" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(new Regex("#cart$"));
            await Expect(Page.Locator("#page .data-table tbody tr")).ToHaveCountAsync(1);
            await LogoutAsync();

            await LoginAsync(application, "admin@u1.local", "U1Admin!2026");
            await Page.GotoAsync($"{application.BaseUrl}/#admin-orders");
            var adminRow = Page.Locator("tbody tr").Filter(new() { HasText = orderNumber });
            await adminRow.GetByRole(AriaRole.Button, new() { Name = orderNumber }).ClickAsync();
            dialog = Page.GetByRole(AriaRole.Dialog);
            var privateNote = "Özel yönetici notu " + Guid.NewGuid().ToString("N")[..8];
            await dialog.GetByLabel("Yönetici notu (bayiye gösterilmez)").FillAsync(privateNote);
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Notu kaydet" }).ClickAsync();
            await Expect(dialog.GetByLabel("Yönetici notu (bayiye gösterilmez)"))
                .ToHaveValueAsync(privateNote);
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Pencereyi kapat" }).ClickAsync();
            await LogoutAsync();

            await LoginAsync(application, "bayi@u1.local", "U1Bayi!2026");
            await Page.GotoAsync($"{application.BaseUrl}/#orders");
            await Page.Locator("tbody tr").Filter(new() { HasText = orderNumber })
                .GetByRole(AriaRole.Button, new() { Name = orderNumber }).ClickAsync();
            dialog = Page.GetByRole(AriaRole.Dialog);
            await Expect(dialog).Not.ToContainTextAsync(privateNote);
            await Expect(dialog).ToContainTextAsync("Durum geçmişi");
        });
    }

    [Fact]
    public async Task Category_management_archive_and_dealer_cart_recovery_work_in_browser()
    {
        await using var application =
            await BrowserTestApplication.StartAsync(TestContext.Current.CancellationToken);

        await RunWithDiagnostics(application, async () =>
        {
            await LoginAsync(application, "admin@u1.local", "U1Admin!2026");
            await Page.GotoAsync($"{application.BaseUrl}/#admin-categories");
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var sourceName = "Tarayıcı kaynak " + suffix;
            var targetName = "Tarayıcı hedef " + suffix;
            var code = "R10-BR-" + suffix.ToUpperInvariant();
            ILocator CategoryRow(string name) =>
                Page.Locator($"tbody tr:has(td:first-child b:text-is(\"{name}\"))");

            foreach (var name in new[] { sourceName, targetName })
            {
                await Page.Locator("#new-category-name").FillAsync(name);
                await Page.GetByRole(AriaRole.Button, new() { Name = "Kategori ekle" }).ClickAsync();
                await Expect(CategoryRow(name)).ToBeVisibleAsync();
            }

            using var adminApi = await LoginApiAsync(application, "admin@u1.local", "U1Admin!2026");
            async Task<JsonElement> Categories() => await adminApi.Client.GetFromJsonAsync<JsonElement>(
                "/api/admin/categories", TestContext.Current.CancellationToken);
            var categories = await Categories();
            var source = categories.EnumerateArray().Single(x => x.GetProperty("name").GetString() == sourceName);
            var target = categories.EnumerateArray().Single(x => x.GetProperty("name").GetString() == targetName);
            var sourceId = source.GetProperty("id").GetInt32();
            var targetId = target.GetProperty("id").GetInt32();
            using var createProduct = await SendJsonAsync(adminApi, HttpMethod.Post, "/api/admin/products", new
            {
                code,
                name = "Tarayıcı yarış ürünü",
                description = "R10 browser",
                brand = "CI",
                manufacturerCode = "CI",
                imageUrl = "/images/product.svg",
                stock = 4,
                criticalStock = 1,
                price = 100m,
                categoryId = sourceId
            });
            createProduct.EnsureSuccessStatusCode();
            var created = await createProduct.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            var productId = created.GetProperty("id").GetInt32();

            var renamed = sourceName + " yeni";
            var sourceRow = CategoryRow(sourceName);
            await sourceRow.Locator("input[name=name]").FillAsync(renamed);
            await sourceRow.GetByRole(AriaRole.Button, new() { Name = "Kaydet" }).ClickAsync();
            sourceRow = CategoryRow(renamed);
            await Expect(sourceRow).ToBeVisibleAsync();

            var draft = renamed + " taslak";
            await sourceRow.Locator("input[name=name]").FillAsync(draft);
            categories = await Categories();
            source = categories.EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == sourceId);
            var remoteName = renamed + " uzaktan";
            using var remoteUpdate = await SendJsonAsync(adminApi, HttpMethod.Put,
                $"/api/admin/categories/{sourceId}", new
                {
                    name = remoteName,
                    rowVersion = source.GetProperty("rowVersion").GetString()
                });
            remoteUpdate.EnsureSuccessStatusCode();
            await sourceRow.GetByRole(AriaRole.Button, new() { Name = "Kaydet" }).ClickAsync();
            await Expect(Page.Locator(".toast.error")).ToContainTextAsync("Kategori başka bir işlemde değişti");
            await Expect(sourceRow.Locator("input[name=name]")).ToHaveValueAsync(draft);

            await Page.ReloadAsync();
            sourceRow = CategoryRow(remoteName);
            await Expect(sourceRow).ToBeVisibleAsync();
            await sourceRow.Locator("select[name=target]").SelectOptionAsync(
                new SelectOptionValue { Label = targetName });
            await sourceRow.GetByRole(AriaRole.Button, new() { Name = "Birleştir" }).ClickAsync();
            await Expect(CategoryRow(remoteName)).ToHaveCountAsync(0);
            await Expect(CategoryRow(targetName)).ToContainTextAsync("1");

            await LogoutAsync();
            await LoginAsync(application, "bayi@u1.local", "U1Bayi!2026");
            await AddProductAsync(application, code);
            await LogoutAsync();

            await LoginAsync(application, "admin@u1.local", "U1Admin!2026");
            await Page.GotoAsync($"{application.BaseUrl}/#admin-product?id={productId}");
            await Page.GetByLabel("Arşivleme nedeni").FillAsync("R10 tarayıcı testi");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Ürünü arşivle" }).ClickAsync();
            await Expect(Page.GetByText("Ürün arşivde.")).ToBeVisibleAsync();
            await LogoutAsync();

            await LoginAsync(application, "bayi@u1.local", "U1Bayi!2026");
            await Page.GotoAsync($"{application.BaseUrl}/#cart");
            await Expect(Page.GetByText("Artık satışta değil · Sepetten çıkarın")).ToBeVisibleAsync();
            await Page.GetByRole(AriaRole.Button, new() { Name = "Tarayıcı yarış ürünü sepetten çıkar" })
                .ClickAsync();
            await Expect(Page.GetByText("Sepetiniz henüz boş.")).ToBeVisibleAsync();
            await LogoutAsync();

            await LoginAsync(application, "admin@u1.local", "U1Admin!2026");
            await Page.GotoAsync($"{application.BaseUrl}/#admin-product?id={productId}");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Yeniden satışa aç" }).ClickAsync();
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Ürünü arşivle" })).ToBeVisibleAsync();

            categories = await Categories();
            Assert.DoesNotContain(categories.EnumerateArray(), x => x.GetProperty("id").GetInt32() == sourceId);
            Assert.Contains(categories.EnumerateArray(), x => x.GetProperty("id").GetInt32() == targetId);
        });
    }

    [Fact]
    public async Task Lost_checkout_response_keeps_the_same_request_id_after_reload()
    {
        await using var application =
            await BrowserTestApplication.StartAsync(TestContext.Current.CancellationToken);

        await RunWithDiagnostics(application, async () =>
        {
            await LoginAsync(application, "bayi@u1.local", "U1Bayi!2026");
            await AddProductAsync(application, "DG-001");

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
            await Page.GetByRole(AriaRole.Button, new() { Name = "Siparişi gözden geçir" })
                .ClickAsync();
            await Page.GetByRole(AriaRole.Dialog)
                .GetByRole(AriaRole.Button, new() { Name = "Siparişi oluştur" })
                .ClickAsync();

            var pendingDialog = Page.GetByRole(AriaRole.Dialog);
            await Expect(pendingDialog).ToContainTextAsync(
                "Önceki sipariş isteğinin sonucu bilinmiyor.");
            await Expect(pendingDialog.GetByRole(
                AriaRole.Button,
                new() { Name = "Önceki siparişi sorgula" }))
                .ToBeVisibleAsync();

            await Page.ReloadAsync();
            await Page.GotoAsync($"{application.BaseUrl}/#cart");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Bekleyen siparişi sorgula" })
                .ClickAsync();
            await Page.GetByRole(AriaRole.Dialog)
                .GetByRole(AriaRole.Button, new() { Name = "Önceki siparişi sorgula" })
                .ClickAsync();

            await Expect(Page).ToHaveURLAsync(new Regex("#orders$"));
            Assert.Equal(2, requestIds.Count);
            Assert.Equal(requestIds[0], requestIds[1]);

            var orderRow = Page.Locator("#page .data-table tbody tr").First;
            await Expect(orderRow).ToContainTextAsync("24.900,00");
            await Expect(orderRow).ToContainTextAsync("Bekliyor");
            await Expect(Page.Locator("#page .data-table tbody tr")).ToHaveCountAsync(1);
        });
    }

    [Fact]
    public async Task Dealer_can_login_add_product_to_cart_and_place_order()
    {
        await using var application =
            await BrowserTestApplication.StartAsync(TestContext.Current.CancellationToken);

        await RunWithDiagnostics(application, async () =>
        {
            await LoginAsync(application, "bayi@u1.local", "U1Bayi!2026");
            await AddProductAsync(application, "DG-001");

            await Page.GotoAsync($"{application.BaseUrl}/#cart");
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Sepetim" }))
                .ToBeVisibleAsync();

            await Page.GetByRole(AriaRole.Button, new() { Name = "Siparişi gözden geçir" })
                .ClickAsync();

            var dialog = Page.GetByRole(AriaRole.Dialog);
            await Expect(dialog).ToBeVisibleAsync();
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Siparişi oluştur" })
                .ClickAsync();

            await Expect(Page).ToHaveURLAsync(new Regex("#orders$"));
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Siparişlerim" }))
                .ToBeVisibleAsync();

            var row = Page.Locator("#page .data-table tbody tr").First;
            await Expect(row).ToContainTextAsync("U1-");
            await Expect(row).ToContainTextAsync("24.900,00");
            await Expect(row).ToContainTextAsync("Bekliyor");
        });
    }

    [Fact]
    public async Task Admin_approval_and_rejection_are_visible_to_the_dealer()
    {
        await using var application =
            await BrowserTestApplication.StartAsync(TestContext.Current.CancellationToken);

        await RunWithDiagnostics(application, async () =>
        {
            await LoginAsync(application, "bayi@u1.local", "U1Bayi!2026");
            await AddProductAsync(application, "DG-001");
            await Page.GotoAsync($"{application.BaseUrl}/#cart");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Siparişi gözden geçir" })
                .ClickAsync();
            await Page.GetByRole(AriaRole.Dialog)
                .GetByRole(AriaRole.Button, new() { Name = "Siparişi oluştur" })
                .ClickAsync();

            var dealerRow = Page.Locator("#page .data-table tbody tr").First;
            var numberButton = dealerRow.Locator("[data-action='order']").First;
            var orderNumber = (await numberButton.InnerTextAsync()).Trim();
            Assert.StartsWith("U1-", orderNumber);
            await Expect(dealerRow).ToContainTextAsync("24.900,00");

            await LogoutAsync();
            await LoginAsync(application, "admin@u1.local", "U1Admin!2026");
            await Page.GotoAsync($"{application.BaseUrl}/#admin-orders");

            var adminRow = Page.Locator("tbody tr").Filter(new() { HasText = orderNumber });
            await Expect(adminRow).ToContainTextAsync("Bekliyor");
            await adminRow.GetByRole(AriaRole.Button, new() { Name = orderNumber }).ClickAsync();

            var dialog = Page.GetByRole(AriaRole.Dialog);
            await dialog.GetByLabel("Sipariş durumu").SelectOptionAsync("Onaylandı");
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Durumu güncelle" })
                .ClickAsync();
            await Expect(Page.Locator("tbody tr").Filter(new() { HasText = orderNumber }))
                .ToContainTextAsync("Onaylandı");

            await LogoutAsync();
            await LoginAsync(application, "bayi@u1.local", "U1Bayi!2026");
            await Page.GotoAsync($"{application.BaseUrl}/#orders");
            dealerRow = Page.Locator("tbody tr").Filter(new() { HasText = orderNumber });
            await Expect(dealerRow).ToContainTextAsync("24.900,00");
            await Expect(dealerRow).ToContainTextAsync("Onaylandı");

            await LogoutAsync();
            await LoginAsync(application, "admin@u1.local", "U1Admin!2026");
            await Page.GotoAsync($"{application.BaseUrl}/#admin-orders");
            adminRow = Page.Locator("tbody tr").Filter(new() { HasText = orderNumber });
            await adminRow.GetByRole(AriaRole.Button, new() { Name = orderNumber }).ClickAsync();
            dialog = Page.GetByRole(AriaRole.Dialog);
            await dialog.GetByLabel("Sipariş durumu").SelectOptionAsync("Reddedildi");
            await dialog.GetByLabel("Ret veya iptal nedeni").FillAsync("Tarayıcı ret testi");
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Durumu güncelle" })
                .ClickAsync();

            await LogoutAsync();
            await LoginAsync(application, "bayi@u1.local", "U1Bayi!2026");
            await Page.GotoAsync($"{application.BaseUrl}/#orders");
            dealerRow = Page.Locator("tbody tr").Filter(new() { HasText = orderNumber });
            await Expect(dealerRow).ToContainTextAsync("24.900,00");
            await Expect(dealerRow).ToContainTextAsync("Reddedildi");
        });
    }

    [Fact]
    public async Task Stale_admin_product_form_preserves_input_and_offers_reload()
    {
        await using var application =
            await BrowserTestApplication.StartAsync(TestContext.Current.CancellationToken);

        await RunWithDiagnostics(application, async () =>
        {
            await LoginAsync(application, "admin@u1.local", "U1Admin!2026");
            await Page.GotoAsync($"{application.BaseUrl}/#admin-products");

            var productRow = Page.Locator("tbody tr").Filter(new() { HasText = "DG-001" });
            await productRow.GetByRole(AriaRole.Link, new() { Name = "Düzenle" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(new Regex(@"#admin-product\?id="));

            var description = Page.Locator("textarea[name='description']");
            var staleDraft = "Tarayıcıda korunacak stale form " + Guid.NewGuid().ToString("N")[..8];
            await description.FillAsync(staleDraft);

            using var api = await LoginApiAsync(
                application,
                "admin@u1.local",
                "U1Admin!2026");
            var product = await GetProductAsync(api, "DG-001");
            var remoteDescription = "Başka yönetici güncellemesi " + Guid.NewGuid().ToString("N")[..8];
            await UpdateProductAsync(api, product, description: remoteDescription);

            await Page.GetByRole(AriaRole.Button, new() { Name = "Ürünü kaydet" }).ClickAsync();

            var conflict = Page.Locator(".product-conflict");
            await Expect(conflict).ToContainTextAsync("Ürün başka bir işlemde değişti.");
            await Expect(description).ToHaveValueAsync(staleDraft);

            var reload = conflict.GetByRole(AriaRole.Button, new() { Name = "Yeniden yükle" });
            await Expect(reload).ToBeFocusedAsync();
            await reload.ClickAsync();
            await Expect(description).ToHaveValueAsync(remoteDescription);
        });
    }

    [Fact]
    public async Task Price_change_requires_checkout_reapproval_with_the_new_total()
    {
        await using var application =
            await BrowserTestApplication.StartAsync(TestContext.Current.CancellationToken);

        await RunWithDiagnostics(application, async () =>
        {
            await LoginAsync(application, "bayi@u1.local", "U1Bayi!2026");
            await AddProductAsync(application, "DG-001");
            await Page.GotoAsync($"{application.BaseUrl}/#cart");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Siparişi gözden geçir" })
                .ClickAsync();

            var dialog = Page.GetByRole(AriaRole.Dialog);
            await Expect(dialog).ToContainTextAsync("24.900,00");

            using var api = await LoginApiAsync(
                application,
                "admin@u1.local",
                "U1Admin!2026");
            var product = await GetProductAsync(api, "DG-001");
            await UpdateProductAsync(api, product, price: 25_000m);

            await dialog.GetByRole(AriaRole.Button, new() { Name = "Siparişi oluştur" })
                .ClickAsync();

            dialog = Page.GetByRole(AriaRole.Dialog);
            await Expect(dialog.GetByRole(
                AriaRole.Heading,
                new() { Name = "Sepet değişti, yeniden onaylayın" }))
                .ToBeVisibleAsync();
            await Expect(dialog).ToContainTextAsync("Önceki onay geçersiz.");
            await Expect(dialog).ToContainTextAsync("24.900,00");
            await Expect(dialog).ToContainTextAsync("25.000,00");

            await dialog.GetByRole(AriaRole.Button, new() { Name = "Siparişi oluştur" })
                .ClickAsync();

            await Expect(Page).ToHaveURLAsync(new Regex("#orders$"));
            var row = Page.Locator("#page .data-table tbody tr").First;
            await Expect(row).ToContainTextAsync("25.000,00");
            await Expect(row).ToContainTextAsync("Bekliyor");
        });
    }

    [Fact]
    public async Task Admin_can_update_a_banner_from_the_management_ui()
    {
        await using var application =
            await BrowserTestApplication.StartAsync(TestContext.Current.CancellationToken);

        await RunWithDiagnostics(application, async () =>
        {
            await LoginAsync(application, "admin@u1.local", "U1Admin!2026");
            await Page.GotoAsync($"{application.BaseUrl}/#admin-banners");
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Duyurular" }))
                .ToBeVisibleAsync();

            await Page.GetByRole(AriaRole.Button, new() { Name = "Düzenle" }).First.ClickAsync();
            var dialog = Page.GetByRole(AriaRole.Dialog);
            await Expect(dialog).ToBeVisibleAsync();

            var updatedSubtitle = "CI yönetim tarayıcı testi " + Guid.NewGuid().ToString("N")[..8];
            await dialog.GetByLabel("Alt metin").FillAsync(updatedSubtitle);
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Kaydet" }).ClickAsync();

            await Expect(Page.Locator("#page")).ToContainTextAsync(updatedSubtitle);

            await Page.GotoAsync($"{application.BaseUrl}/#admin-history");
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "İşlem geçmişi" }))
                .ToBeVisibleAsync();
            await Page.GetByLabel("İşlem").SelectOptionAsync("BannerUpdated");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Filtrele" }).ClickAsync();
            var historyRow = Page.Locator("#page .data-table tbody tr").First;
            await Expect(historyRow).ToContainTextAsync("Duyuru güncelleme");
            await Expect(historyRow).ToContainTextAsync("Duyuru güncellendi.");
        });
    }

    [Fact]
    public async Task Expired_session_returns_the_user_to_login()
    {
        await using var application =
            await BrowserTestApplication.StartAsync(TestContext.Current.CancellationToken);

        await RunWithDiagnostics(application, async () =>
        {
            await LoginAsync(application, "bayi@u1.local", "U1Bayi!2026");
            await Context.ClearCookiesAsync();
            await Page.GotoAsync($"{application.BaseUrl}/#orders");

            await Expect(Page).ToHaveURLAsync(new Regex("#login$"));
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Bayi girişi" }))
                .ToBeVisibleAsync();
        });
    }

    [Fact]
    public async Task Dealer_groups_create_update_assign_and_preserve_stale_drafts()
    {
        await using var application = await BrowserTestApplication.StartAsync(TestContext.Current.CancellationToken);
        await RunWithDiagnostics(application, async () =>
        {
            await LoginAsync(application, "admin@u1.local", "U1Admin!2026");
            await Page.GotoAsync($"{application.BaseUrl}/#admin-users");
            var name = "Tarayıcı grup " + Guid.NewGuid().ToString("N")[..6];
            var create = Page.Locator("[data-form='dealer-group-create']");
            await create.Locator("[name='name']").FillAsync(name);
            await create.Locator("[name='discountPercent']").FillAsync("7.5");
            await create.GetByRole(AriaRole.Button, new() { Name = "Grup ekle" }).ClickAsync();
            var group = Page.Locator("[data-form='dealer-group-update']").Filter(new()
            {
                Has = Page.Locator($"input[value='{name}']")
            });
            await Expect(group).ToHaveCountAsync(1);
            var id = await group.GetAttributeAsync("data-id");
            group = Page.Locator($"[data-form='dealer-group-update'][data-id='{id}']");
            await group.Locator("[name='discountPercent']").FillAsync("12.5");
            await group.GetByRole(AriaRole.Button, new() { Name = "Kaydet", Exact = true }).ClickAsync();
            await Expect(group.Locator("[name='discountPercent']")).ToHaveValueAsync("12.5");

            using var api = await LoginApiAsync(application, "admin@u1.local", "U1Admin!2026");
            var groups = await api.Client.GetFromJsonAsync<JsonElement>("/api/admin/dealer-groups", TestContext.Current.CancellationToken);
            var current = groups.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetInt32().ToString() == id);
            using (var response = await SendJsonAsync(api, HttpMethod.Put, $"/api/admin/dealer-groups/{id}", new
            {
                name = name + " güncel", discountPercent = 15,
                rowVersion = current.GetProperty("rowVersion").GetString()
            })) response.EnsureSuccessStatusCode();
            await group.Locator("[name='name']").FillAsync(name + " taslak");
            await group.Locator("[name='discountPercent']").FillAsync("20");
            await group.GetByRole(AriaRole.Button, new() { Name = "Kaydet", Exact = true }).ClickAsync();
            await Expect(group.GetByRole(AriaRole.Alert)).ToContainTextAsync("Girdiğiniz alanlar korunuyor");
            await Expect(group.Locator("[name='name']")).ToHaveValueAsync(name + " taslak");
            await Expect(group.Locator("[name='discountPercent']")).ToHaveValueAsync("20");
            await group.GetByRole(AriaRole.Button, new() { Name = "Güncel bilgileri yükle" }).ClickAsync();
            await Expect(group.Locator("[name='name']")).ToHaveValueAsync(name + " güncel");
            await Expect(group.Locator("[name='discountPercent']")).ToHaveValueAsync("15");

            var userRow = Page.Locator("tbody tr").Filter(new() { HasText = "bayi@u1.local" });
            await userRow.GetByRole(AriaRole.Button, new() { Name = "Düzenle" }).ClickAsync();
            var dialog = Page.GetByRole(AriaRole.Dialog);
            await dialog.GetByLabel("Bayi grubu", new() { Exact = true }).SelectOptionAsync(id!);
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Bilgileri kaydet" }).ClickAsync();
            await Expect(userRow).ToContainTextAsync(name + " güncel");
            await userRow.GetByRole(AriaRole.Button, new() { Name = "Düzenle" }).ClickAsync();
            var users = await api.Client.GetFromJsonAsync<JsonElement>("/api/admin/users", TestContext.Current.CancellationToken);
            var user = users.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("email").GetString() == "bayi@u1.local");
            using (var response = await SendJsonAsync(api, HttpMethod.Put, $"/api/admin/users/{user.GetProperty("id").GetInt32()}", new
            {
                firstName = "Güncel bayi", lastName = user.GetProperty("lastName").GetString(),
                email = user.GetProperty("email").GetString(), phone = user.GetProperty("phone").GetString(),
                company = user.GetProperty("company").GetString(), isActive = true,
                dealerGroupId = int.Parse(id!), version = user.GetProperty("version").GetInt32()
            })) response.EnsureSuccessStatusCode();
            await dialog.GetByLabel("Ad", new() { Exact = true }).FillAsync("Taslak bayi");
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Bilgileri kaydet" }).ClickAsync();
            await Expect(dialog.GetByRole(AriaRole.Alert)).ToContainTextAsync("Girdiğiniz alanlar korunuyor");
            await Expect(dialog.GetByLabel("Ad", new() { Exact = true })).ToHaveValueAsync("Taslak bayi");
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Güncel bilgileri yükle" }).ClickAsync();
            await Expect(dialog.GetByLabel("Ad", new() { Exact = true })).ToHaveValueAsync("Güncel bayi");
            await Page.Keyboard.PressAsync("Escape");

            await Page.GotoAsync($"{application.BaseUrl}/#admin-history");
            foreach (var action in new[] { ("DealerGroupCreated", "Bayi grubu oluşturma"), ("DealerGroupUpdated", "Bayi grubu güncelleme"), ("DealerGroupAssigned", "Bayi grubu atama") })
            {
                await Page.GetByLabel("İşlem", new() { Exact = true }).SelectOptionAsync(action.Item1);
                await Page.GetByRole(AriaRole.Button, new() { Name = "Filtrele" }).ClickAsync();
                await Expect(Page.Locator("tbody .status-badge").First).ToHaveTextAsync(action.Item2);
            }
        });
    }

    [Fact]
    public async Task Pending_image_cleanup_without_audit_can_be_restored_in_browser()
    {
        await using var application = await BrowserTestApplication.StartAsync(TestContext.Current.CancellationToken);
        var operation = Guid.NewGuid().ToString("N");
        var uploads = Path.Combine(application.WebRootPath, "uploads");
        var pendingFolder = Path.Combine(uploads, ".pending-cleanup", operation);
        var fileName = "browser-recovery-" + operation + ".png";
        var pendingPath = Path.Combine(pendingFolder, fileName);
        var restoredPath = Path.Combine(uploads, fileName);
        var contents = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jXioAAAAASUVORK5CYII=");
        try
        {
            Directory.CreateDirectory(pendingFolder);
            await File.WriteAllBytesAsync(pendingPath, contents, TestContext.Current.CancellationToken);
            await RunWithDiagnostics(application, async () =>
            {
                await LoginAsync(application, "admin@u1.local", "U1Admin!2026");
                await Page.GotoAsync($"{application.BaseUrl}/#admin-products");
                // Simulate a partial cleanup response to verify its recovery entry point.
                await Page.RouteAsync("**/api/admin/images/cleanup-preview", route => route.FulfillAsync(new()
                {
                    ContentType = "application/json",
                    Body = JsonSerializer.Serialize(new { items = new[] { new { url = "/uploads/" + fileName, sizeBytes = contents.Length, canDelete = true } } })
                }));
                await Page.RouteAsync("**/api/admin/images/cleanup", route => route.FulfillAsync(new()
                {
                    ContentType = "application/json", Body = "{\"deleted\":0,\"pending\":1}"
                }));
                await Page.Locator("[data-action='preview-image-cleanup']").ClickAsync();
                await Page.Locator("[data-cleanup-image]").CheckAsync();
                await Page.Locator("[data-action='cleanup-images']").ClickAsync();
                await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("1 dosyanın temizliği bekliyor");
                await Page.GetByRole(AriaRole.Button, new() { Name = "Bekleyen işlemleri incele" }).ClickAsync();
                var dialog = Page.GetByRole(AriaRole.Dialog);
                var item = dialog.Locator("section").Filter(new() { HasText = operation });
                await Expect(item).ToContainTextAsync("Temizlik geçmiş kaydı yok");
                await Expect(item.Locator("[data-action='finalize-pending-images']")).ToBeDisabledAsync();
                await Page.RouteAsync("**/api/admin/images/cleanup-pending/*/restore", async route =>
                {
                    var response = await route.FetchAsync();
                    Assert.Equal(200, response.Status);
                    var result = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(await response.TextAsync())!;
                    result["auditWarning"] = JsonSerializer.SerializeToElement("Dosya işlemi uygulandı; sonuç geçmiş kaydı doğrulanamadı. İşlem kimliğiyle kontrol edin.");
                    await route.FulfillAsync(new() { Response = response, Body = JsonSerializer.Serialize(result) });
                });
                var restore = item.Locator("[data-action='restore-pending-images']");
                await restore.FocusAsync();
                await Page.Keyboard.PressAsync("Enter");
                await Expect(dialog.Locator("section").Filter(new() { HasText = operation })).ToHaveCountAsync(0);
                await Expect(dialog.GetByRole(AriaRole.Alert)).ToContainTextAsync("sonuç geçmiş kaydı doğrulanamadı");
                await Expect(dialog.GetByRole(AriaRole.Alert)).ToContainTextAsync(operation);
                Assert.False(File.Exists(pendingPath));
                Assert.Equal(contents, await File.ReadAllBytesAsync(restoredPath, TestContext.Current.CancellationToken));
            });
        }
        finally
        {
            if (File.Exists(pendingPath)) File.Delete(pendingPath);
            if (File.Exists(restoredPath)) File.Delete(restoredPath);
            if (Directory.Exists(pendingFolder) && !Directory.EnumerateFileSystemEntries(pendingFolder).Any())
                Directory.Delete(pendingFolder);
        }
    }

    [Fact]
    public async Task Mobile_catalog_and_cart_keep_the_required_purchase_controls()
    {
        await Page.SetViewportSizeAsync(390, 844);
        await using var application =
            await BrowserTestApplication.StartAsync(TestContext.Current.CancellationToken);

        await RunWithDiagnostics(application, async () =>
        {
            await LoginAsync(application, "bayi@u1.local", "U1Bayi!2026");
            await Page.GotoAsync($"{application.BaseUrl}/#catalog");
            await Page.GetByLabel("Ürün ara").FillAsync("DG-001");

            await Expect(Page.Locator("th").Filter(new() { HasText = "Ürün adı" }))
                .ToBeVisibleAsync();
            await Expect(Page.Locator("th").Filter(new() { HasText = "Birim fiyat" }))
                .ToBeVisibleAsync();
            await Expect(Page.Locator("th").Filter(new() { HasText = "Adet / Sepete ekle" }))
                .ToBeVisibleAsync();
            await Expect(Page.Locator("th").Filter(new() { HasText = "Ürün kodu" }))
                .ToBeHiddenAsync();

            var productRow = Page.Locator("tbody tr").Filter(new() { HasText = "DG-001" });
            await productRow.Locator("[data-action='add']").ClickAsync();
            await Expect(Page.Locator("[data-cart-count]").First).ToHaveTextAsync("1");
            await Page.GotoAsync($"{application.BaseUrl}/#cart");

            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Sepetim" }))
                .ToBeVisibleAsync();
            await Expect(Page.Locator("input[data-context='cart']")).ToBeVisibleAsync();
            var remove = Page.Locator(".cart-table [data-action='remove']");
            var total = Page.Locator(".cart-table td[data-label='Toplam']");
            await Expect(remove).ToBeInViewportAsync();
            await Expect(total).ToBeInViewportAsync();
            Assert.True(await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
            await remove.FocusAsync();
            await Page.Keyboard.PressAsync("Enter");
            await Expect(Page.GetByText("Sepetiniz henüz boş.", new() { Exact = true })).ToBeVisibleAsync();
            await AddProductAsync(application, "DG-001");
            await Page.GotoAsync($"{application.BaseUrl}/#cart");
            await Expect(Page.GetByRole(
                AriaRole.Button,
                new() { Name = "Siparişi gözden geçir" }))
                .ToBeVisibleAsync();
            await Page.GetByRole(AriaRole.Button, new() { Name = "Siparişi gözden geçir" }).ClickAsync();
            var approval = Page.GetByRole(AriaRole.Dialog);
            await Expect(approval).ToBeVisibleAsync();
            await approval.GetByRole(AriaRole.Button, new() { Name = "Siparişi oluştur", Exact = true }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(new Regex("#orders$"));
        });
    }

    [Fact]
    public async Task Keyboard_opened_product_modal_returns_focus_after_escape()
    {
        await using var application =
            await BrowserTestApplication.StartAsync(TestContext.Current.CancellationToken);

        await RunWithDiagnostics(application, async () =>
        {
            await LoginAsync(application, "bayi@u1.local", "U1Bayi!2026");
            await Page.GotoAsync($"{application.BaseUrl}/#catalog");
            await Page.GetByLabel("Ürün ara").FillAsync("DG-001");
            await Expect(Page.Locator("tbody tr")).ToHaveCountAsync(1);

            var productButton = Page.Locator("tbody tr")
                .Filter(new() { HasText = "DG-001" })
                .Locator(".product-name")
                .First;
            await productButton.FocusAsync();
            await Page.Keyboard.PressAsync("Enter");

            var dialog = Page.GetByRole(AriaRole.Dialog);
            await Expect(dialog).ToBeVisibleAsync();
            await Expect(dialog.GetByRole(
                AriaRole.Heading,
                new() { Name = "Ürün detayları" }))
                .ToBeVisibleAsync();

            await Page.Keyboard.PressAsync("Escape");
            await Expect(dialog).ToBeHiddenAsync();
            await Expect(productButton).ToBeFocusedAsync();
        });
    }

    [Fact]
    public async Task Account_profile_stale_form_keeps_draft_and_reloads_remote_value()
    {
        await using var application =
            await BrowserTestApplication.StartAsync(TestContext.Current.CancellationToken);

        await RunWithDiagnostics(application, async () =>
        {
            await LoginAsync(application, "bayi@u1.local", "U1Bayi!2026");
            await Page.GotoAsync($"{application.BaseUrl}/#account");
            var firstName = Page.GetByLabel("Ad", new() { Exact = true });
            var originalName = await firstName.InputValueAsync();
            var originalLastName = await Page.GetByLabel("Soyad").InputValueAsync();
            var originalCompany = await Page.GetByLabel("Firma adı").InputValueAsync();
            var originalPhone = await Page.GetByLabel("Telefon").InputValueAsync();
            var draftName = "Taslak-" + Guid.NewGuid().ToString("N")[..6];
            var remoteName = "Uzaktan-" + Guid.NewGuid().ToString("N")[..6];

            using var api = await LoginApiAsync(application, "bayi@u1.local", "U1Bayi!2026");
            var before = await api.Client.GetFromJsonAsync<JsonElement>(
                "/api/auth/me", TestContext.Current.CancellationToken);
            using (var remoteUpdate = await SendJsonAsync(api, HttpMethod.Put, "/api/auth/profile", new
            {
                firstName = remoteName,
                lastName = originalLastName,
                phone = originalPhone,
                company = originalCompany,
                rowVersion = before.GetProperty("rowVersion").GetString()
            }))
            {
                remoteUpdate.EnsureSuccessStatusCode();
            }

            await firstName.FillAsync(draftName);
            await Page.GetByRole(AriaRole.Button, new() { Name = "Bilgileri kaydet" }).ClickAsync();
            await Expect(Page.Locator(".toast.error"))
                .ToContainTextAsync("Profil bilgileriniz başka bir işlemde değişti");
            await Expect(firstName).ToHaveValueAsync(draftName);

            await Page.ReloadAsync();
            await Expect(Page.GetByLabel("Ad", new() { Exact = true })).ToHaveValueAsync(remoteName);

            var latest = await api.Client.GetFromJsonAsync<JsonElement>(
                "/api/auth/me", TestContext.Current.CancellationToken);
            using var restore = await SendJsonAsync(api, HttpMethod.Put, "/api/auth/profile", new
            {
                firstName = originalName,
                lastName = originalLastName,
                phone = originalPhone,
                company = originalCompany,
                rowVersion = latest.GetProperty("rowVersion").GetString()
            });
            restore.EnsureSuccessStatusCode();
        });
    }

    [Fact]
    public async Task Catalog_network_failure_shows_a_recoverable_error()
    {
        await using var application =
            await BrowserTestApplication.StartAsync(TestContext.Current.CancellationToken);

        await RunWithDiagnostics(application, async () =>
        {
            await LoginAsync(application, "bayi@u1.local", "U1Bayi!2026");
            await Page.RouteAsync("**/api/products**", route => route.AbortAsync());
            await Page.GotoAsync($"{application.BaseUrl}/#catalog");

            var error = Page.Locator(".content-error");
            await Expect(error).ToBeVisibleAsync();
            await Expect(error).ToContainTextAsync(
                "Sunucuya ulaşılamadı. Bağlantınızı kontrol edip tekrar deneyin.");
            await Expect(error.GetByRole(AriaRole.Button, new() { Name = "Tekrar dene" }))
                .ToBeVisibleAsync();
        });
    }

    private async Task LoginAsync(
        BrowserTestApplication application,
        string email,
        string password)
    {
        await Page.GotoAsync($"{application.BaseUrl}/#login");
        await Page.GetByLabel("E-posta adresi").FillAsync(email);
        await Page.GetByLabel("Şifre").FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Giriş yap" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("#home$"));
    }

    private async Task LogoutAsync()
    {
        var menu = Page.Locator(".account-menu");
        await menu.Locator("summary").ClickAsync();
        await menu.GetByRole(AriaRole.Button, new() { Name = "Çıkış yap" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("#login$"));
    }

    private async Task AddProductAsync(
        BrowserTestApplication application,
        string code)
    {
        await Page.GotoAsync($"{application.BaseUrl}/#catalog");
        await Page.GetByLabel("Ürün ara").FillAsync(code);
        var productRow = Page.Locator("tbody tr").Filter(new() { HasText = code }).First;
        await Expect(productRow).ToBeVisibleAsync();
        await productRow.Locator("[data-action='add']").ClickAsync();
        await Expect(Page.Locator("[data-cart-count]").First).ToHaveTextAsync("1");
    }

    private static async Task<ApiSession> LoginApiAsync(
        BrowserTestApplication application,
        string email,
        string password)
    {
        var client = new HttpClient(new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true
        })
        {
            BaseAddress = new Uri(application.BaseUrl)
        };

        try
        {
            var csrf = await ReadCsrfAsync(client);
            using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new { email, password })
            };
            loginRequest.Headers.Add("X-CSRF-TOKEN", csrf);
            using var login = await client.SendAsync(
                loginRequest,
                TestContext.Current.CancellationToken);
            login.EnsureSuccessStatusCode();

            return new ApiSession(client, await ReadCsrfAsync(client));
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task<string> ReadCsrfAsync(HttpClient client)
    {
        var body = await client.GetFromJsonAsync<JsonElement>(
            "/api/csrf",
            TestContext.Current.CancellationToken);
        return body.GetProperty("token").GetString()!;
    }

    private static async Task<JsonElement> GetProductAsync(
        ApiSession session,
        string code)
    {
        var list = await session.Client.GetFromJsonAsync<JsonElement>(
            "/api/products?q=" + Uri.EscapeDataString(code),
            TestContext.Current.CancellationToken);
        var id = list.GetProperty("items")[0].GetProperty("id").GetInt32();
        return await session.Client.GetFromJsonAsync<JsonElement>(
            $"/api/products/{id}",
            TestContext.Current.CancellationToken);
    }

    private static async Task UpdateProductAsync(
        ApiSession session,
        JsonElement product,
        decimal? price = null,
        string? description = null)
    {
        var id = product.GetProperty("id").GetInt32();
        var response = await SendJsonAsync(
            session,
            HttpMethod.Put,
            $"/api/admin/products/{id}",
            new
            {
                code = product.GetProperty("code").GetString(),
                name = product.GetProperty("name").GetString(),
                description = description ?? product.GetProperty("description").GetString(),
                brand = product.GetProperty("brand").GetString(),
                manufacturerCode = product.GetProperty("manufacturerCode").GetString(),
                specialCode1 = product.GetProperty("specialCode1").GetString(),
                specialCode2 = product.GetProperty("specialCode2").GetString(),
                imageUrl = product.GetProperty("imageUrl").GetString(),
                stock = product.GetProperty("stock").GetInt32(),
                criticalStock = product.GetProperty("criticalStock").GetInt32(),
                price = price ?? product.GetProperty("price").GetDecimal(),
                categoryId = product.GetProperty("categoryId").GetInt32(),
                version = product.GetProperty("rowVersion").GetString()
            });
        using (response)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    private static Task<HttpResponseMessage> SendJsonAsync(
        ApiSession session,
        HttpMethod method,
        string path,
        object body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-CSRF-TOKEN", session.Csrf);
        return session.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task RunWithDiagnostics(
        BrowserTestApplication application,
        Func<Task> body)
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

    private sealed class ApiSession(HttpClient client, string csrf) : IDisposable
    {
        public HttpClient Client { get; } = client;
        public string Csrf { get; } = csrf;

        public void Dispose() => Client.Dispose();
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
    public string WebRootPath => Path.Combine(FindRepositoryRoot(), "src", "U1.Business", "wwwroot");

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
