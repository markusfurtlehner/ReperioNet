using Android.App;
using Android.Runtime;
using Microsoft.Maui.Hosting;

namespace ReperioNet.Sample.Maui;

/// <summary>Android application bootstrap.</summary>
[Application]
public class MainApplication : MauiApplication
{
    /// <summary>Standard Android application constructor.</summary>
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    /// <inheritdoc />
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
