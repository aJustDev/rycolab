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
        "watch" => WatchCommand.Run(opts),
        "apply" => ApplyCommand.Run(opts),
        "reset" => ResetCommand.Run(opts),
        "plan" => PlanCommand.Run(opts),
        "guard" => GuardCommand.Run(opts),
        "sweep" => SweepCommand.Run(opts),
        "report" => ReportCommand.Run(opts),
        "task" => TaskCommand.Run(opts),
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
          watch      Muestrea reloj, reloj efectivo, potencia y Tctl de un nucleo
          plan       Perfil por nucleo y parametros del barrido (plan.json)
          guard      Aplica el plan, lo reaplica al reanudar, vigila margen y WHEA
          sweep      Barrido por nucleos con y-cruncher: busca el limite de cada uno
          report     Limites, positivos, telemetria y eventos de una campana (colab.db)
          task       Tarea programada que lanza guard al iniciar sesion
          help       Esta ayuda

        report
          --campaign <n|dir>   runs/<n> (sweep) o runs/guard.
          --md [ruta]          Escribe markdown (por defecto report.md en la campana).
          --rebuild            Regenera colab.db desde los JSONL.

        sweep
          --campaign <n>     Carpeta runs/<n>. Por defecto sweep-<fecha>. Reanudable: salta
                             los nucleos con limite y trata un en-curso.json como cuelgue.
          --cores <spec>     0-15, 0,3,8-11 ...   Por defecto los 16.
          --start/--top/--step/--seconds   Sobrescriben el plan.
          --no-suspend       Sin la suspension periodica de 1 s cada 10 s.
          --plain            Sin panel.
          Exige el hardware en la base (cierra guard antes). Restaura la base tras cada prueba.

        plan
          init [--from-hardware] [--force]    Crea plan.json (perfil -5 o el que hay puesto)
          show                                 Enseña el plan
          set-core <n> <m>                     Cambia un nucleo
          set-profile a,b,...,p                Los 16 de golpe
          from-sweep <campana> [--margin 5]    Perfil = limite + margen desde runs/<campana>/limits.json
          --plan <ruta>                        Otro fichero. Por defecto plan.json en la raiz.

        guard
          --plan <ruta>      Plan a vigilar. Por defecto plan.json.
          --minutes <n>      Soak acotado; sin el, indefinido (Ctrl+C para salir).
          --interval <s>     Segundos entre muestras. Por defecto 60.
          --plain            Sin panel; una linea por muestra.
          Al salir (tiempo, Ctrl+C, WHEA o margen perdido) deja la base.
          Codigos: 0 limpio, 10 positivo (WHEA o margen perdido), 1 no pudo aplicar.

        task
          install [--plan <ruta>] | remove | status

        probe
          --compare <ruta>   Comparar con un perfil JSON con campo CoreValues.
                             Por defecto usa el de Legion Toolkit.
          --no-compare       No comparar con ningun perfil.
          --json <ruta>      Guardar la lectura con marca de tiempo.
          --sensors          Anadir reloj efectivo y potencia por nucleo.

        watch
          --core <n>         Nucleo a vigilar.
          --seconds <n>      Duracion. Por defecto 180.
          --interval <ms>    Intervalo entre muestras. Por defecto 1000.
          --jsonl <ruta>     Una linea JSON por muestra.
          --summary <ruta>   Medianas al terminar, en JSON.
          --raw              Anadir la tabla de potencia del SMU (floats crudos).

        apply
          --margin <n>       Margen objetivo. Solo valores <= 0.
          --core <n>         Un solo nucleo.       Sin --core ni --ccd: los 16.
          --ccd <0|1>        Un CCD completo (0 = nucleos 0-7).
          --profile <ruta>   Por nucleo, desde un JSON con CoreValues.
          --plan [ruta]      Por nucleo, desde plan.json.
          --dry-run          Enseña el plan y no escribe nada.

        reset
          --to <n>           Base a la que volver. Por defecto -5.
          --dry-run          Enseña el plan y no escribe nada.

        SEGURIDAD
          Margen admitido: -50 a 0. Un valor positivo SUBE el voltaje y se
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
