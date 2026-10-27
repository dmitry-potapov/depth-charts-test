using DepthCharts.Api;
using DepthCharts.Application;
using DepthCharts.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainValidationExceptionHandler>();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();
app.UseExceptionHandler();
app.MapDepthChartEndpoints();
await app.Services.EnsureDatabaseCreatedAsync();
app.Run();

public partial class Program;
