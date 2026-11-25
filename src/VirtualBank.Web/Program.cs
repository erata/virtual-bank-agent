using VirtualBank.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddVirtualBank(builder.Configuration);

var app = builder.Build();
app.UseVirtualBank();
app.Run();
