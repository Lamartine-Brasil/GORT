using Avalonia;
using System;
using System.Diagnostics;
using System.IO;
using Gort.Config;
using Gort.Lifecycle;

namespace Gort;

class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // RF-003: pasta do executável como diretório corrente.
        try { Directory.SetCurrentDirectory(AppContext.BaseDirectory); } catch { }

        // Auxiliares (fora da instância única): worker de tradução (Etapa 15).
        if (args.Length >= 2 && args[0] == "--translate-worker")
            return Translate.LocalWorker.RunWorkerAsync(args[1],
                args.Length >= 3 ? args[2] : "").GetAwaiter().GetResult();
        // Ajudante de atualização em processo separado (RF-425, Etapa 18).
        if (args.Length >= 2 && args[0] == "--do-update")
            return Update.UpdateHelper.RunAsync(args, s =>
            {
                try { Console.WriteLine(s); } catch { }
            }).GetAwaiter().GetResult();

        // RF-001/RF-002: instância única, salvo marcador.
        using var single = new SingleInstance();
        if (!single.TryAcquire())
        {
            Console.Error.WriteLine("Já há uma instância do GORT em execução.");
            return 1;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Fatal(e.ExceptionObject as Exception);   // RF-006

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            // RF-016: remover bandeja / soltar hook acontecem no App.ShutdownTray.
            return 0;
        }
        catch (Exception ex) { Fatal(ex); return 2; }
    }

    /// <summary>RF-006: erro não tratado → descrição + ajuda + encerrar.</summary>
    private static void Fatal(Exception? ex)
    {
        try { Console.Error.WriteLine("GORT: erro fatal: " + ex?.Message); } catch { }
        try
        {
            Process.Start(new ProcessStartInfo(Catalogs.Links.KnownErrors)
                { UseShellExecute = true });
        }
        catch { }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
