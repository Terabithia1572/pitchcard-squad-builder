using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using FiveKor.Web.Data;
using FiveKor.Web.Models;
using FiveKor.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var dataDirectory = Environment.GetEnvironmentVariable("FIVEKOR_DATA_DIR") is { Length: > 0 } configured
    ? Path.GetFullPath(configured) : Path.Combine(builder.Environment.ContentRootPath, "App_Data");
var mediaDirectory = Path.Combine(dataDirectory, "media");
Directory.CreateDirectory(mediaDirectory);
Directory.CreateDirectory(Path.Combine(dataDirectory, "keys"));
builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = 52 * 1024 * 1024);
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={Path.Combine(dataDirectory, "fivekor.db")}"));
builder.Services.AddScoped<StateService>();
builder.Services.AddScoped<IPasswordHasher<AdminAccount>, PasswordHasher<AdminAccount>>();
builder.Services.AddHttpClient<GalleryService>(client => client.Timeout = TimeSpan.FromSeconds(20))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
    .SetHandlerLifetime(TimeSpan.FromMinutes(5));
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys")));
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "FiveKor.Admin";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.LoginPath = "/login";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(options => { options.HeaderName = "X-CSRF-TOKEN"; options.Cookie.Name = "FiveKor.Csrf"; options.Cookie.SameSite = SameSiteMode.Strict; });
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 10, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
});

var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    if (!await db.States.AnyAsync())
    {
        var seed = await File.ReadAllTextAsync(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "seed.json"));
        db.States.Add(new SiteState { Id = "main", Payload = seed, Revision = 0 });
    }
    if (!await db.Admins.AnyAsync())
    {
        var email = builder.Configuration["Admin:Email"]?.Trim();
        var password = builder.Configuration["Admin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password) || password.Length < 12)
            throw new InvalidOperationException("İlk çalıştırmada Admin__Email ve en az 12 karakterlik Admin__Password ortam değişkenlerini gir.");
        var account = new AdminAccount { Email = email.ToLowerInvariant() };
        account.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AdminAccount>>().HashPassword(account, password);
        db.Admins.Add(account);
    }
    await db.SaveChangesAsync();
}

if (!app.Environment.IsDevelopment()) { app.UseHsts(); app.UseHttpsRedirection(); }
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; img-src 'self' blob: data: https://www.fifarosters.com https://fifarosters.com https://i.imgur.com https://i.ibb.co https://i.postimg.cc https://images2.imgbox.com https://images.imgbox.com; media-src 'self' blob:; script-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'self' https://chatgpt.com https://*.chatgpt.com";
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
    {
        if (context.User.Identity?.IsAuthenticated != true) { context.Response.StatusCode = 403; await context.Response.WriteAsJsonAsync(new { error = "Yönetici girişi gerekli." }); return; }
        var sameSite = context.Request.Headers.Origin.ToString() == $"{context.Request.Scheme}://{context.Request.Host}" &&
                       context.Request.Headers["Sec-Fetch-Site"].ToString() != "cross-site";
        if (!sameSite || !await context.RequestServices.GetRequiredService<IAntiforgery>().IsRequestValidAsync(context))
        { context.Response.StatusCode = 403; await context.Response.WriteAsJsonAsync(new { error = "İstek güvenlik doğrulamasından geçmedi." }); return; }
    }
    await next();
});

static bool Admin(HttpContext context) => context.User.Identity?.IsAuthenticated == true;
static IResult Problem(string text, int status) => Results.Json(new { error = text }, statusCode: status);

app.MapGet("/login", (HttpContext ctx, IAntiforgery tokens) =>
{
    if (Admin(ctx)) return (IResult)Results.Redirect("/admin");
    var token = tokens.GetAndStoreTokens(ctx).RequestToken;
    var html = """
        <!doctype html><html lang="tr"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
        <title>5 Kor Yönetici Girişi</title><style>body{font:16px Arial;background:#071020;color:#fff;min-height:100vh;display:grid;place-items:center}
        form{width:min(360px,90vw);padding:35px;border:1px solid #ffd35b;border-radius:15px;background:#0c1b32;display:grid;gap:16px}
        input,button{font:inherit;padding:12px;border-radius:7px}button{background:#ffd35b;border:0;font-weight:700;cursor:pointer}</style></head>
        <body><form method="post" action="/login"><h1>5 KOR · Yönetici</h1><label>E-posta<input name="email" type="email" autocomplete="username" required></label>
        <label>Şifre<input name="password" type="password" autocomplete="current-password" required></label><input type="hidden" name="__RequestVerificationToken" value="TOKEN">
        <button type="submit">Giriş Yap</button></form></body></html>
        """.Replace("TOKEN", System.Net.WebUtility.HtmlEncode(token));
    return Results.Content(html, "text/html; charset=utf-8");
});

app.MapPost("/login", async (HttpContext ctx, IAntiforgery antiforgery, AppDbContext db, IPasswordHasher<AdminAccount> hasher) =>
{
    if (!await antiforgery.IsRequestValidAsync(ctx)) return Problem("Form süresi doldu. Sayfayı yenile.", 403);
    var form = await ctx.Request.ReadFormAsync();
    var email = form["email"].ToString().Trim().ToLowerInvariant();
    var user = await db.Admins.AsNoTracking().SingleOrDefaultAsync(x => x.Email == email);
    if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, form["password"].ToString()) == PasswordVerificationResult.Failed)
        return Problem("E-posta veya şifre hatalı.", 401);
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Email, user.Email), new Claim(ClaimTypes.Role, "Admin") }, CookieAuthenticationDefaults.AuthenticationScheme)));
    return Results.Redirect("/admin");
}).RequireRateLimiting("login");
app.MapGet("/logout", (HttpContext ctx, IAntiforgery tokens) => Results.Content(
    "<html lang=\"tr\"><meta charset=\"utf-8\"><body style=\"font:18px Arial;background:#071020;color:white;padding:3rem\"><form method=\"post\" action=\"/logout\"><input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"" +
    System.Net.WebUtility.HtmlEncode(tokens.GetAndStoreTokens(ctx).RequestToken) +
    "\"><button style=\"padding:12px 25px\">Yönetici oturumunu kapat</button></form></body></html>", "text/html; charset=utf-8")).RequireAuthorization();
app.MapPost("/logout", async (HttpContext ctx, IAntiforgery tokens) =>
{
    if (!await tokens.IsRequestValidAsync(ctx)) return Problem("İstek doğrulanamadı.", 403);
    await ctx.SignOutAsync(); return Results.Redirect("/");
}).RequireAuthorization();

app.MapGet("/admin", (HttpContext ctx) => Admin(ctx) ? (IResult)Results.File(Path.Combine(app.Environment.ContentRootPath, "private", "admin.html"), "text/html") : Results.Redirect("/login"));
app.MapGet("/admin.html", (HttpContext ctx) => Admin(ctx) ? (IResult)Results.File(Path.Combine(app.Environment.ContentRootPath, "private", "admin.html"), "text/html") : Results.Redirect("/login"));
app.MapGet("/api/state", async (HttpContext ctx, StateService service, IAntiforgery tokens, CancellationToken ct) =>
{
    var (state, revision) = await service.GetAsync(ct);
    return Results.Json(new { state, revision, admin = Admin(ctx), csrfToken = Admin(ctx) ? tokens.GetAndStoreTokens(ctx).RequestToken : null });
});
app.MapPut("/api/state", async (HttpContext ctx, StateService service, CancellationToken ct) =>
{
    if (!ctx.Request.HasJsonContentType()) return Problem("JSON gerekli.", 415);
    if (ctx.Request.ContentLength > 500_000) return Problem("Veri çok büyük.", 413);
    using var buffer = new MemoryStream();
    await ctx.Request.Body.CopyToAsync(buffer, ct);
    if (buffer.Length > 500_000) return Problem("Veri çok büyük.", 413);
    try
    {
        var dto = JsonSerializer.Deserialize<SaveStateRequest>(buffer.ToArray(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (dto is null) return Problem("Veri eksik.", 400);
        if (!await service.SaveAsync(dto.State, dto.Revision, ct)) return Problem("Başka bir sekmede değişiklik yapıldı. Taslağını indirip sayfayı yenile.", 409);
        return Results.Json(new { revision = dto.Revision + 1 });
    }
    catch (Exception e) when (e is ArgumentException or JsonException) { return Problem(e.Message, 400); }
});

async Task<string> StoreMedia(byte[] bytes, string type, string name, AppDbContext db, CancellationToken ct)
{
    var id = Guid.NewGuid().ToString();
    var path = Path.Combine(mediaDirectory, id);
    await File.WriteAllBytesAsync(path, bytes, ct);
    db.Media.Add(new MediaFile { Id = id, ContentType = type, Size = bytes.Length, Name = name[..Math.Min(150, name.Length)], CreatedAtUtc = DateTime.UtcNow });
    try { await db.SaveChangesAsync(ct); }
    catch { File.Delete(path); throw; }
    return id;
}
app.MapPost("/api/upload", async (HttpContext ctx, AppDbContext db, CancellationToken ct) =>
{
    var type = ctx.Request.ContentType?.Split(';')[0] ?? "";
    var image = type is "image/png" or "image/jpeg" or "image/webp";
    var video = type is "video/mp4" or "video/webm";
    if (!image && !video) return Problem("JPG, PNG, WebP, MP4 veya WebM seç.", 415);
    var max = image ? 8 * 1024 * 1024 : 50 * 1024 * 1024;
    if (ctx.Request.ContentLength > max) return Problem("Dosya boyutu sınırı aşıldı.", 413);
    var input = ctx.Request.Body;
    using var output = new MemoryStream();
    var block = new byte[65536];
    while (true) { var n = await input.ReadAsync(block, ct); if (n == 0) break; if (output.Length + n > max) return Problem("Dosya boyutu sınırı aşıldı.", 413); output.Write(block, 0, n); }
    var bytes = output.ToArray();
    if (GalleryService.DetectType(bytes) != type) return Problem("Dosya içeriği biçimle uyuşmuyor.", 415);
    var id = await StoreMedia(bytes, type, ctx.Request.Query["name"].ToString(), db, ct);
    return Results.Json(new { url = "/media/" + id, type, size = bytes.Length }, statusCode: 201);
});
app.MapMethods("/media/{id}", new[] { "GET", "HEAD" }, async (string id, AppDbContext db, CancellationToken ct) =>
{
    if (!Guid.TryParse(id, out _)) return Results.NotFound();
    var item = await db.Media.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
    var path = Path.Combine(mediaDirectory, id);
    return item is null || !File.Exists(path) ? Results.NotFound() : Results.File(path, item.ContentType, enableRangeProcessing: true);
});

app.MapGet("/api/template-gallery", async (int? year, int? page, string? keyword, GalleryService gallery, CancellationToken ct) =>
{
    try { return Results.Json(await gallery.GetAsync(year ?? 27, page ?? 1, keyword ?? "", ct)); }
    catch (ArgumentException e) { return Problem(e.Message, 400); }
    catch (Exception) { return Problem("Çevrimiçi galeriye ulaşılamıyor. Hazır şablonlar kullanılabilir.", 503); }
}).RequireAuthorization();
app.MapPost("/api/template-import", async (ImportTemplateRequest input, GalleryService gallery, AppDbContext db, CancellationToken ct) =>
{
    if (!Regex.IsMatch(input.Id ?? "", @"^\d{1,12}$")) return Problem("Şablon numarası geçersiz.", 400);
    try
    {
        var page = await gallery.GetAsync(input.Year, input.Page, input.Keyword ?? "", ct);
        var item = page.items.SingleOrDefault(x => x.id == input.Id);
        if (item?.image is null) return Problem("Şablon görseli bulunamadı.", 400);
        var bytes = await gallery.DownloadAsync(item.image, 8 * 1024 * 1024, ct);
        var type = GalleryService.DetectType(bytes);
        if (!type.StartsWith("image/")) return Problem("Geçersiz şablon görseli.", 415);
        var id = await StoreMedia(bytes, type, item.name, db, ct);
        return Results.Json(new { template = new { id = "import-" + id, name = item.name, image = "/media/" + id,
            color = item.color, layout = item.layout, year = item.year, author = item.author, source = item.source } }, statusCode: 201);
    }
    catch (ArgumentException e) { return Problem(e.Message, 400); }
    catch (Exception) { return Problem("Şablon şu anda aktarılamıyor.", 503); }
}).RequireAuthorization();

app.Run();
