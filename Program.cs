namespace PufferHelper;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // --demo fills the overlay with a sample round, no API call: handy for placing the window.
        bool demo = args.Any(a => a.Equals("--demo", StringComparison.OrdinalIgnoreCase));

        // Two copies would fight over the same global hotkeys.
        using var mutex = new Mutex(true, @"Local\PufferHelper-single-instance", out bool firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("Puffer Helper is already running. Look for its small overlay, or press Ctrl+Alt+H to show it.",
                "Puffer Helper", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        var settings = AppSettings.Load(out string? loadError);
        Application.Run(new OverlayForm(settings, loadError, demo));
    }
}
