using Microsoft.EntityFrameworkCore;
using PrintApp.Data;
using PrintApp.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddScoped<ZplService>();

// ── EF Core — cùng SQL Server với SVN_Tools ───────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("ProdConnectionString")));

// ── Toast services ─────────────────────────────────────────────────────────────
builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<ToastSerialService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseForwardedHeaders();
app.UsePathBase("/quangprint");
app.Use((context, next) =>
{
    context.Request.PathBase = "/quangprint";
    return next();
});
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Print}/{action=Index}/{id?}");

app.MapControllers();

app.Run();