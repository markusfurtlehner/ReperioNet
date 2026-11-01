using Microsoft.Maui.Controls;

namespace ReperioNet.Sample.Maui;

/// <summary>Application root: a single window hosting the search demo page.</summary>
public class App : Application
{
    /// <summary>Creates the app's one window with the search demo page.</summary>
    protected override Window CreateWindow(Microsoft.Maui.IActivationState? activationState) =>
        new(new MainPage());
}
