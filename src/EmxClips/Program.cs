namespace EmxClips;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, "Local\\EMXClips", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("EMX Clips is already running.", "EMX Clips", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayAppContext());
    }
}

