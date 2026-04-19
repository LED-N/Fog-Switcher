namespace FogSwitcher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, args) => ShowFatalError(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ShowFatalError(args.ExceptionObject as Exception ?? new Exception("Unknown startup error."));

        try
        {
            if (ElevatedHostsOperation.TryHandleCommand(args, out var exitCode))
            {
                return exitCode;
            }

            Application.Run(new MainForm());
            return 0;
        }
        catch (Exception ex)
        {
            ShowFatalError(ex);
            return 1;
        }
    }

    private static void ShowFatalError(Exception ex)
    {
        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FogSwitcher");
            Directory.CreateDirectory(logDirectory);

            var logPath = Path.Combine(logDirectory, "startup-error.log");
            File.WriteAllText(
                logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]{Environment.NewLine}{ex}");

            MessageBox.Show(
                "Fog Switcher crashed during startup.\n\n" +
                ex.Message +
                "\n\nA log was written to:\n" +
                logPath,
                "Startup Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
            MessageBox.Show(
                "Fog Switcher crashed during startup.\n\n" + ex,
                "Startup Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
