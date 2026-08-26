namespace LegionCoLab.Cli.Commands;

/// <summary>
/// Vuelta a la base en los 16 nucleos.
///
/// La base por defecto es -5, que es lo que el ajuste all-core de la BIOS
/// (Sign −, Magnitude 5) deja puesto en el POST en esta maquina. Es decir: lo
/// mismo a lo que vuelve el equipo solo con reiniciar.
/// </summary>
public static class ResetCommand
{
    public const int DefaultBaseline = -5;

    public static int Run(Args args)
    {
        var to = args.GetInt("to") ?? DefaultBaseline;

        Console.WriteLine();
        Console.WriteLine($"  Devolviendo los 16 nucleos a {to}.");

        var forwarded = new List<string> { "--margin", to.ToString() };
        if (args.Has("dry-run")) forwarded.Add("--dry-run");

        return ApplyCommand.Run(new Args(forwarded));
    }
}
