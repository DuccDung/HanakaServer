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

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", p =>
        p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

// JWT config
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection["Key"] ?? throw new Exception("Jwt:Key is missing");

builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<IOtpEmailService, OtpEmailService>();
builder.Services.AddScoped<IOtpGenerator, OtpGenerator>();
builder.Services.AddScoped<IUserOtpService, UserOtpService>();

builder.Services.AddSingleton<RealtimeHub>();
builder.Services.AddScoped<WebSocketHandler>();

// Authentication: giữ nguyên logic Cookie (MVC) + JWT (API)
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    options.LoginPath = "/Home/Login";
    options.AccessDeniedPath = "/Home/Login";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
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

    // FIX 1: đọc access_token từ query string cho websocket
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];

            if (!string.IsNullOrWhiteSpace(accessToken)
                && context.HttpContext.WebSockets.IsWebSocketRequest)
            {
                context.Token = accessToken;
            }

            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

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

// Route
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Login}/{id?}");

app.Map("/ws", async httpContext =>
{
    if (!httpContext.WebSockets.IsWebSocketRequest)
    {
        httpContext.Response.StatusCode = 400;
        await httpContext.Response.WriteAsync("WebSocket request required");
        return;
    }

    // FIX 2: ép authenticate bằng JWT thay vì dùng httpContext.User mặc định
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