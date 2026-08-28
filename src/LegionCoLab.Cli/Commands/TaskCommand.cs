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
                // Sin ventana: powershell oculto y guard en modo plano. 'colab status' es la ventana.
                var tr = $"powershell -NoProfile -WindowStyle Hidden -Command \\\"& '{exe}' guard --plain --plan '{plan}'\\\"";
                var code = Schtasks($"/Create /TN {TaskName} /TR \"{tr}\" /SC ONLOGON /RL HIGHEST /IT /F");
                if (code == 0) Console.WriteLine($"  Tarea {TaskName} creada: guard oculto con {plan} al iniciar sesion. 'colab status' para verlo.");
                return code;
            }
            case "remove":
                return Schtasks($"/Delete /TN {TaskName} /F");
            case "run":
            {
                if (Process.GetProcessesByName("colab").Any(p => p.Id != Environment.ProcessId))
                {
                    Console.Error.WriteLine("Ya hay un colab en marcha (guard o sweep). Paralo antes.");
                    return 1;
                }
                var code = Schtasks($"/Run /TN {TaskName}");
                if (code == 0) Console.WriteLine("  guard lanzado oculto por la tarea. 'colab status' lo enseña; 'colab task stop' lo para.");
                return code;
            }
            case "stop":
            {
                var runs = new GuardOptions().RunsDir;
                var others = Process.GetProcessesByName("colab").Where(p => p.Id != Environment.ProcessId).ToList();
                if (others.Count == 0) { Console.WriteLine("  No hay ningun guard en marcha."); return 0; }
                File.WriteAllText(Guard.StopFile(runs), DateTime.Now.ToString("o"));
                Console.WriteLine("  Parada pedida; guard la ve en su siguiente muestra (hasta 1 min) y restaura la base.");
                foreach (var p in others) p.WaitForExit(90_000);
                var still = Process.GetProcessesByName("colab").Any(p => p.Id != Environment.ProcessId);
                Console.WriteLine(still ? "  Sigue vivo; revisa la ventana." : "  guard cerrado.");
                return still ? 1 : 0;
            }
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
