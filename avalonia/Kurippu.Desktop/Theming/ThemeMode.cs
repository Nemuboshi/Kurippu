using Avalonia.Styling;

namespace Kurippu.Desktop.Theming;

public static class ThemeMode
{
    public static ThemeVariant? Resolve(string? mode) => mode switch
    {
        "Light" => ThemeVariant.Light,
        "Dark" => ThemeVariant.Dark,
        _ => null
    };
}
