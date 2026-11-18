using Avalonia;
using System;

namespace UChat.App;

sealed class Program
{
    // Код ініціалізації. Не використовуйте Avalonia, сторонні API або код,
    // що залежить від SynchronizationContext, до виклику AppMain: усе ще не ініціалізовано,
    // і це може призвести до помилок.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Конфігурація Avalonia; не видаляйте, вона також використовується візуальним дизайнером.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
