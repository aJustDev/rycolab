using System.Security.Principal;
using LegionCoLab.Cli;
using LegionCoLab.Cli.Commands;
using LegionCoLab.Core;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var argv = Environment.GetCommandLineArgs().Skip(1).ToArray();
var command = argv.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))?.ToLowerInvariant();
var opts = new Args(argv.Where(a => !string.Equals(a, command, StringComparison.OrdinalIgnoreCase)));

if (command is null or "help" or "-h" or "--help")
{
    PrintHelp();
    return 0;
}

if (!IsElevated())
{
    Console.Error.WriteLine("colab necesita privilegios de administrador para hablar con el SMU.");
    Console.Error.WriteLine("Abre una consola elevada y vuelve a intentarlo.");
    return 3;
}

try
{
    return command switch
    {
        "probe" => ProbeCommand.Run(opts),
        "sensors" => SensorsCommand.Run(opts),
        _ => Unknown(command),
    };
}
catch (SafetyViolationException ex)
{
    Console.Error.WriteLine($"BLOQUEADO POR SEGURIDAD: {ex.Message}");
    return 4;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 1;
}

static int Unknown(string c)
{
    Console.Error.WriteLine($"Orden desconocida: {c}");
    PrintHelp();
    return 2;
}

static bool IsElevated()
{
    try
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }
    catch { return false; }
}

static void PrintHelp()
{
    Console.WriteLine("""
        colab — banco de pruebas de Curve Optimizer para Ryzen

        USO
          colab <orden> [opciones]

        ORDENES
          probe      Lee el margen PSM aplicado en cada nucleo
          sensors    Vuelca los sensores disponibles con su nombre exacto
          help       Esta ayuda

        probe
          --compare <ruta>   Comparar con un perfil JSON con campo CoreValues.
                             Por defecto usa el de Legion Toolkit.
          --no-compare       No comparar con ningun perfil.
          --json <ruta>      Guardar la lectura con marca de tiempo.
          --sensors          Anadir una instantanea de telemetria.

        CODIGOS DE SALIDA
          0  correcto        2  perfil y hardware NO coinciden
          1  error           3  faltan privilegios       4  bloqueado por seguridad

        Requiere consola elevada.
        """);
}
