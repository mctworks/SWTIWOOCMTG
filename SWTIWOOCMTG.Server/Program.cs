using System.Text.Json.Serialization;
using SWTIWOOCMTG;
using SWTIWOOCMTG.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<GameRoomRegistry>();
builder.Services.AddHostedService<RoomCleanupService>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { status = "SWTIWOOCMTG server running" }));

app.MapRoomEndpoints();

app.Run();
