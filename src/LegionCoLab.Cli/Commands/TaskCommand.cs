using System.Diagnostics;
using LegionCoLab.Core;

namespace LegionCoLab.Cli.Commands;

/// <summary>
/// Tarea programada que lanza guard al iniciar sesion, elevada, en ventana
/// minimizada. colab task install|remove|status [--plan ruta]
/// </summary>
public static class TaskCommand
{
    private const string TaskName = "LegionCoLab-Guard";

    public static int Run(Args args)
    {
        var sub = args.Positional.FirstOrDefault() ?? "status";
        switch (sub)
        {
            case "install":
            {
                var plan = Path.GetFullPath(args.Get("plan") ?? Plan.DefaultPath);
                if (!File.Exists(plan)) { Console.Error.WriteLine($"No existe el plan {plan}"); return 1; }
                var exe = Environment.ProcessPath!;
                // powershell abre la ventana minimizada; schtasks no sabe hacerlo solo.
                var tr = $"powershell -NoProfile -WindowStyle Minimized -Command \\\"& '{exe}' guard --plan '{plan}'\\\"";
                var code = Schtasks($"/Create /TN {TaskName} /TR \"{tr}\" /SC ONLOGON /RL HIGHEST /IT /F");
                if (code == 0) Console.WriteLine($"  Tarea {TaskName} creada: guard con {plan} al iniciar sesion.");
                return code;
            }
            case "remove":
                return Schtasks($"/Delete /TN {TaskName} /F");
            case "status":
                return Schtasks($"/Query /TN {TaskName} /V /FO LIST");
            default:
                Console.Error.WriteLine($"Suborden desconocida: {sub}");
                return 2;
        }
    }

    private static int Schtasks(string arguments)
    {
        var p = Process.Start(new ProcessStartInfo("schtasks.exe", arguments) { UseShellExecute = false })!;
        p.WaitForExit();
        return p.ExitCode;
    }
}
