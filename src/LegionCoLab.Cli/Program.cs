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
        "apply" => ApplyCommand.Run(opts),
        "reset" => ResetCommand.Run(opts),
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
          apply      Escribe margenes (camina el paso, verifica cada parada)
          reset      Devuelve los 16 nucleos a la base
          sensors    Vuelca los sensores disponibles con su nombre exacto
          help       Esta ayuda

        probe
          --compare <ruta>   Comparar con un perfil JSON con campo CoreValues.
                             Por defecto usa el de Legion Toolkit.
          --no-compare       No comparar con ningun perfil.
          --json <ruta>      Guardar la lectura con marca de tiempo.
          --sensors          Anadir reloj efectivo y potencia por nucleo.

        apply
          --margin <n>       Margen objetivo. Solo valores <= 0.
          --core <n>         Un solo nucleo.       Sin --core ni --ccd: los 16.
          --ccd <1|2>        Un CCD completo.
          --profile <ruta>   Por nucleo, desde un JSON con CoreValues.
          --dry-run          Enseña el plan y no escribe nada.

        reset
          --to <n>           Base a la que volver. Por defecto -5.
          --dry-run          Enseña el plan y no escribe nada.

        SEGURIDAD
          Margen admitido: -30 a 0. Un valor positivo SUBE el voltaje y se
          rechaza siempre. Toda escritura se relee y se aborta si no coincide.
          Un movimiento grande se recorre en tramos de 3 cuentas como mucho,
          verificando en cada parada. Si el proceso muere a medias, los nucleos
          vuelven a como estaban; y un reinicio los devuelve a los de la BIOS.

        CODIGOS DE SALIDA
          0  correcto        2  perfil y hardware NO coinciden
          1  error           3  faltan privilegios       4  bloqueado por seguridad

        Requiere consola elevada.
        """);
}
