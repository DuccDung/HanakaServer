using HanakaServer.Data;
using HanakaServer.Services;
using mail_service.Internal;
using mail_service.service;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// MVC
builder.Services.AddControllersWithViews();

// DbContext
builder.Services.AddDbContext<PickleballDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("PickleballDb")));

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", p =>
        p.AllowAnyOrigin()
         .AllowAnyHeader()
         .AllowAnyMethod());
});

// JWT config
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection["Key"] ?? throw new Exception("Jwt:Key is missing");

// Services
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<IOtpEmailService, OtpEmailService>();
builder.Services.AddScoped<IOtpGenerator, OtpGenerator>();
builder.Services.AddScoped<IUserOtpService, UserOtpService>();
builder.Services.AddScoped<IAppAuthService, AppAuthService>();
builder.Services.AddSingleton<IWebAuthCookieService, WebAuthCookieService>();

builder.Services.AddSingleton<RealtimeHub>();
builder.Services.AddScoped<WebSocketHandler>();

// Authentication
builder.Services.AddAuthentication(options =>
{
    // Web MVC / Referee Portal dùng Cookie mặc định
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    // Login mặc định cho web
    options.LoginPath = "/RefereePortal/Login";
    options.AccessDeniedPath = "/RefereePortal/Login";

    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;

    options.Cookie.Name = "Hanaka.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
})
.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSection["Issuer"],
        ValidAudience = jwtSection["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.FromSeconds(30)
    };

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];

            if (!string.IsNullOrWhiteSpace(accessToken)
                && context.HttpContext.WebSockets.IsWebSocketRequest)
            {
                context.Token = accessToken;
                return Task.CompletedTask;
            }

            if (string.IsNullOrWhiteSpace(context.Token)
                && context.Request.Cookies.TryGetValue(WebAuthCookieService.AccessTokenCookieName, out var cookieToken)
                && !string.IsNullOrWhiteSpace(cookieToken))
            {
                context.Token = cookieToken;
            }

            return Task.CompletedTask;
        }
    };
});

// Authorization
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RefereeOnly", policy =>
        policy.RequireAuthenticatedUser()
              .RequireRole("REFEREE", "Admin"));

    options.AddPolicy("AdminOnly", policy =>
        policy.RequireAuthenticatedUser()
              .RequireRole("Admin"));
});

var app = builder.Build();

// Error / HSTS
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseCors("AllowAll");
app.UseWebSockets();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "pickleball_web_home",
    pattern: "",
    defaults: new { controller = "PickleballWeb", action = "Index" });

// Route mặc định
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Login}/{id?}");

// Portal trọng tài
app.MapControllerRoute(
    name: "referee_portal",
    pattern: "RefereePortal/{action=Login}/{id?}",
    defaults: new { controller = "RefereePortal" });

// WebSocket endpoint
app.Map("/ws", async httpContext =>
{
    if (!httpContext.WebSockets.IsWebSocketRequest)
    {
        httpContext.Response.StatusCode = 400;
        await httpContext.Response.WriteAsync("WebSocket request required");
        return;
    }

    // Authenticate bằng JWT cho websocket
    var authResult = await httpContext.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);

    if (!authResult.Succeeded || authResult.Principal == null)
    {
        httpContext.Response.StatusCode = 401;
        await httpContext.Response.WriteAsync("Unauthorized");
        return;
    }

    httpContext.User = authResult.Principal;

    var userId =
        httpContext.User.FindFirstValue("uid") ??
        httpContext.User.FindFirstValue("UserId") ??
        httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

    if (string.IsNullOrWhiteSpace(userId))
    {
        httpContext.Response.StatusCode = 401;
        await httpContext.Response.WriteAsync("Missing uid");
        return;
    }

    var ws = await httpContext.WebSockets.AcceptWebSocketAsync();
    var handler = httpContext.RequestServices.GetRequiredService<WebSocketHandler>();

    await handler.HandleAsync(ws, userId, httpContext.RequestAborted);
});

app.Run();
