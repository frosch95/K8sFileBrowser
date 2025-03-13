using Avalonia;
using Avalonia.ReactiveUI;
using System;
using Avalonia.Logging;
using Serilog;

namespace K8sFileBrowser;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) {
        try
        {
            Log.Logger = new LoggerConfiguration()
                //.Filter.ByIncludingOnly(Matching.WithProperty("Area", LogArea.Control))
                .MinimumLevel.Information()
                .WriteTo.Async(a => a.File("app.log"))
                //.WriteTo.Console()
                .CreateLogger();

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception e)
        {
            // here we can work with the exception, for example add it to our log file
            Log.Fatal(e, "Something very bad happened");
        }
        finally
        {
            // This block is optional.
            // Use the finally-block if you need to clean things up or similar
            Log.CloseAndFlush();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            //.LogToTrace(LogEventLevel.Verbose)
            .UseReactiveUI();
}