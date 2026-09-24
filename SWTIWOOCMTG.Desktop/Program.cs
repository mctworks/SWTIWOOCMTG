using Microsoft.Extensions.DependencyInjection;
using Photino.Blazor;
using SWTIWOOCMTG;
using SWTIWOOCMTG.UI;   // App

var appBuilder = PhotinoBlazorAppBuilder.CreateDefault(args);

appBuilder.RootComponents.Add<App>("app");

// Game class registered as a singleton service so any Razor component
// can inject it and call into the existing logic.
appBuilder.Services.AddSingleton<Game>();
appBuilder.Services.AddLogging();

var app = appBuilder.Build();

app.MainWindow
    .SetTitle("SWTIWOOCMTG")
    .SetResizable(true)
    .SetUseOsDefaultSize(false)
    .SetSize(1100, 800);

AppDomain.CurrentDomain.UnhandledException += (sender, error) =>
{
    app.MainWindow.ShowMessage("Fatal exception", error.ExceptionObject.ToString() ?? "Unknown error");
};

app.Run();