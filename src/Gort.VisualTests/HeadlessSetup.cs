using Avalonia;
using Avalonia.Headless;

namespace Gort.Tests;

/// <summary>Bootstrap Avalonia headless para os testes visuais.</summary>
public static class HeadlessSetup
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<Application>()
        // Sem UseHeadlessDrawing + com Skia: permite CaptureRenderedFrame.
        .UseHeadless(new AvaloniaHeadlessPlatformOptions())
        .UseSkia()
        .WithInterFont();

    private static HeadlessUnitTestSession? _session;

    // Uma sessão por assembly: recriar o Application a cada Dispatch
    // (PerTest) gerava churn de teardown do Skia/fontes e crashes nativos
    // intermitentes no host de teste.
    public static HeadlessUnitTestSession Session =>
        _session ??= HeadlessUnitTestSession.StartNew(typeof(HeadlessSetup),
            AvaloniaTestIsolationLevel.PerAssembly);
}
