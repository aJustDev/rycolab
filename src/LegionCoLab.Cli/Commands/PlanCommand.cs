using LegionCoLab.Core;

namespace LegionCoLab.Cli.Commands;

/// <summary>
/// colab plan init [--from-hardware] | show | set-core N M | set-profile a,b,...,p
/// [--plan ruta]
/// </summary>
public static class PlanCommand
{
    public static int Run(Args args)
    {
        var path = args.Get("plan") ?? Plan.DefaultPath;
        var sub = args.Positional.FirstOrDefault() ?? "show";

        switch (sub)
        {
            case "init":
            {
                if (File.Exists(path) && !args.Has("force"))
                {
                    Console.Error.WriteLine($"Ya existe {path}. Usa --force para sobrescribirlo.");
                    return 1;
                }
                var plan = new Plan();
                if (args.Has("from-hardware"))
                {
                    using var co = new CoController();
                    plan.Profile = co.ReadAll().Select(r => r.Margin ?? plan.Base).ToArray();
                }
                plan.Save(path);
                Console.WriteLine($"  Plan escrito en {path}");
                return Show(plan);
            }
            case "show":
                return Show(Plan.Load(path));
            case "set-core":
            {
                if (args.Positional.Count < 3 || !int.TryParse(args.Positional[1], out var core) || !int.TryParse(args.Positional[2], out var margin))
                {
                    Console.Error.WriteLine("Uso: colab plan set-core <nucleo> <margen>");
                    return 1;
                }
                var plan = Plan.Load(path);
                if (core < 0 || core >= plan.Profile.Length) { Console.Error.WriteLine($"nucleo {core} fuera de rango"); return 1; }
                plan.Profile[core] = margin;
                plan.Save(path);
                return Show(plan);
            }
            case "set-profile":
            {
                if (args.Positional.Count < 2)
                {
                    Console.Error.WriteLine("Uso: colab plan set-profile -35,-35,...   (16 valores)");
                    return 1;
                }
                var plan = Plan.Load(path);
                plan.Profile = args.Positional[1].Split(',').Select(s => int.Parse(s.Trim())).ToArray();
                plan.Save(path);
                return Show(plan);
            }
            default:
                Console.Error.WriteLine($"Suborden desconocida: {sub}");
                return 2;
        }
    }

    private static int Show(Plan plan)
    {
        Console.WriteLine();
        Console.WriteLine($"  CCD0  {string.Join("  ", plan.Profile.Take(8).Select((m, i) => $"{i}:{m}"))}");
        Console.WriteLine($"  CCD1  {string.Join("  ", plan.Profile.Skip(8).Select((m, i) => $"{i + 8}:{m}"))}");
        Console.WriteLine();
        Console.WriteLine($"  base {plan.Base}   barrido {plan.Start} -> {plan.Top} de {plan.Step} en {plan.Step}, {plan.Seconds} s   margen de seguridad {plan.SafetyMargin}");
        Console.WriteLine($"  motores {string.Join(" | ", plan.Engines)}   tests {string.Join(",", plan.Tests)}");
        Console.WriteLine($"  y-cruncher {plan.YCruncherDir}{(Directory.Exists(plan.YCruncherDir) ? "" : "   (NO EXISTE)")}");
        Console.WriteLine();
        return 0;
    }
}
