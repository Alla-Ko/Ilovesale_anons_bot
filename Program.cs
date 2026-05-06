using Announcement.Authorization;
using Announcement.Data;
using Announcement.Models;
using Announcement.Options;
using Announcement.Services;
using DotNetEnv;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http;

var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (File.Exists(envPath))
    Env.Load(envPath);

var builder = WebApplication.CreateBuilder(args);

// Render / Docker: слухати порт із змінної PORT (інакше контейнер не приймає трафік).
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        // Railway/хмарні БД можуть коротко обривати TCP/TLS сесію.
        // Retry зменшує кількість 500 на коротких мережевих збоях.
        npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorCodesToAdd: null);
        npgsqlOptions.CommandTimeout(30);
    });
});

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = false;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 6;
        options.SignIn.RequireConfirmedEmail = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.Configure<FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 500L * 1024 * 1024;
});

builder.Services.Configure<KestrelServerOptions>(k =>
{
    k.Limits.MaxRequestBodySize = 500L * 1024 * 1024;
});

builder.Services.Configure<ImgBbOptions>(builder.Configuration.GetSection(ImgBbOptions.SectionName));
builder.Services.Configure<TempClipOptions>(builder.Configuration.GetSection(TempClipOptions.SectionName));
builder.Services.Configure<TelegraphOptions>(builder.Configuration.GetSection(TelegraphOptions.SectionName));
builder.Services.Configure<TelegramOptions>(builder.Configuration.GetSection(TelegramOptions.SectionName));

builder.Services.AddHttpClient<IImgBbUploadService, ImgBbUploadService>();
// tmpfile.link (Cloudflare): як у Postman — UA, Accept, без Expect: 100-continue (інакше часто 500 на великих multipart).
builder.Services.AddTransient<NoExpectContinueHandler>();
builder.Services.AddHttpClient<ITempClipUploadService, TempClipUploadService>(c =>
{
    c.Timeout = TimeSpan.FromMinutes(15);
    c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "PostmanRuntime/7.51.1");
    c.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    //Expect100ContinueTimeout = TimeSpan.Zero,
    AutomaticDecompression = DecompressionMethods.All
})
.AddHttpMessageHandler<NoExpectContinueHandler>();
builder.Services.AddHttpClient<ITelegraphPageService, TelegraphPageService>();

builder.Services.AddSingleton<ICaptionPublishFormatter, CaptionPublishFormatter>();
builder.Services.AddScoped<IMediaProcessingService, MediaProcessingService>();
builder.Services.AddScoped<ITelegramChannelService, TelegramChannelService>();

builder.Services.AddHostedService<AnnouncementCleanupBackgroundService>();

builder.Services.AddRazorPages();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();

app.Run();

