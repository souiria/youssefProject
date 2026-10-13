using HarnessTraceability.Web.Components;
using HarnessTraceability.Web.Services;
using MudBlazor.Services;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddHttpClient("authentication", client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddScoped<SessionState>();
builder.Services.AddScoped<TraceabilityDataService>();
var app = builder.Build();
if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/error"); app.UseHsts(); }
app.UseHttpsRedirection(); app.UseStaticFiles(); app.UseAntiforgery();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
