namespace FrostboundPlus;
static class Program
{
    [STAThread] static void Main(string[] args)
    {
        if (args.Contains("--self-test")) { try { SelfTest.Run().GetAwaiter().GetResult(); Environment.Exit(0); } catch(Exception e) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"self-test-error.txt"), e.ToString()); Environment.Exit(1); } return; }
        ApplicationConfiguration.Initialize(); Application.Run(new LauncherForm());
    }
}
