using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
var builder = WebApplication.CreateBuilder(args);
// Suppress framework request/exception logging: never log credentials, email addresses or SQL parameters.
builder.Logging.ClearProviders();
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 4096);
builder.Services.AddSingleton<ICodeMailer,SmtpCodeMailer>();
builder.Services.AddSingleton<AccountService>();
builder.Services.AddSingleton<MailQueue>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MailQueue>());
builder.Services.AddRateLimiter(o => {
    o.RejectionStatusCode = 429;
    o.AddPolicy("account", c => RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit=12,Window=TimeSpan.FromMinutes(15),QueueLimit=0 }));
});
var app = builder.Build();
foreach (var setting in new[]{"AUTH_DATABASE","CODE_HMAC_KEY","SMTP_USERNAME","SMTP_PASSWORD","WORLD_HOST"})
    if (string.IsNullOrWhiteSpace(app.Configuration[setting])) throw new InvalidOperationException("Missing required service configuration: " + setting);
if (app.Configuration["CODE_HMAC_KEY"]!.Length < 32) throw new InvalidOperationException("CODE_HMAC_KEY must contain at least 32 random characters.");
await app.Services.GetRequiredService<AccountService>().ValidateStorage();
app.Use(async (ctx,next) => {
    ctx.Response.Headers.CacheControl="no-store";
    try { await next(ctx); }
    catch (ArgumentException e) { ctx.Response.StatusCode=400; await ctx.Response.WriteAsJsonAsync(new {message=e.Message}); }
    catch { ctx.Response.StatusCode=503; await ctx.Response.WriteAsJsonAsync(new {message="Service temporarily unavailable. Please try again later."}); }
});
app.UseRateLimiter();
app.MapGet("/health", () => Results.Ok(new {status="healthy",service="Frostbound Plus"}));
app.MapGet("/realm/status", async (AccountService service,IConfiguration cfg) => {
    try {
        using var client = new TcpClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await client.ConnectAsync(cfg["WORLD_HOST"]!,int.Parse(cfg["WORLD_PORT"] ?? "8085"),timeout.Token);
        var (players,bots) = await service.Counts();
        return Results.Ok(new {online=true,realmName="Frostbound Plus",playersOnline=players,botsOnline=bots,clientBuild=5875,message="Classic Azeroth · Level cap 60"});
    } catch { return Results.Ok(new {online=false,realmName="Frostbound Plus",playersOnline=0,botsOnline=0,clientBuild=5875,message="Realm currently unavailable."}); }
});
const string Sent = "If the details are eligible, a verification code will arrive by email. Check your spam folder and wait at least 60 seconds before requesting another.";
app.MapPost("/verification/send", (SendRequest r,MailQueue q) => {
    if (r.Purpose != "register") return Results.BadRequest(new {message="Invalid verification purpose."});
    q.Enqueue(new(Credentials.User(r.Username),Credentials.Email(r.Email),"register"));
    return Results.Ok(new {message=Sent});
}).RequireRateLimiting("account");
app.MapPost("/register", async (CompleteRequest r, AccountService s) => {
    var ok=await s.Complete(Credentials.User(r.Username),Credentials.Email(r.Email),Credentials.Password(r.Password),r.VerificationCode ?? "","register");
    return ok ? Results.Ok(new {success=true,message="Account created. You can now sign in."}) : Results.BadRequest(new {message="Registration could not be completed. Check your details and verification code."});
}).RequireRateLimiting("account");
app.MapPost("/recovery/request", (RecoveryRequest r,MailQueue q) => {
    // Identical response for unknown accounts, mismatches, cooldowns and SMTP failures.
    try { q.Enqueue(new(Credentials.User(r.Username),Credentials.Email(r.Email),"recover")); } catch (ArgumentException) { }
    return Results.Ok(new {message=Sent});
}).RequireRateLimiting("account");
app.MapPost("/recovery/complete", async (CompleteRequest r,AccountService s) => {
    var ok=await s.Complete(Credentials.User(r.Username),Credentials.Email(r.Email),Credentials.Password(r.Password),r.VerificationCode ?? "","recover");
    return ok ? Results.Ok(new {success=true,message="Password updated. Sign in with your new password."}) : Results.BadRequest(new {message="Recovery could not be completed. Check your details and verification code."});
}).RequireRateLimiting("account");
app.Run();
public record SendRequest(string? Email,string? Purpose,string? Username);
public record RecoveryRequest(string? Username,string? Email);
public record CompleteRequest(string? Username,string? Email,string? Password,string? VerificationCode);

