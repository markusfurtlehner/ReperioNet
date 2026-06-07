using Microsoft.Maui.Hosting;

namespace ReperioNet.Sample.Maui;

/// <summary>MAUI bootstrapper.</summary>
public static class MauiProgram
{
    /// <summary>Creates the MAUI app.</summary>
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        return builder.Build();
    }
}
