using System.Diagnostics;

namespace AC_Controller.Tests;

internal static class WpfTestProcess
{
    // Application.Shutdown prevents any later loading of WPF BAML in that process.
    public static async Task RunAsync(Type suite, string test, Func<Task> action)
    {
        const string marker = "AC_CONTROLLER_ISOLATED_WPF_TEST";
        var name = $"{suite.FullName}.{test}";
        if (Environment.GetEnvironmentVariable(marker) == name)
        {
            await action();
            return;
        }

        var info = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add("vstest");
        info.ArgumentList.Add(suite.Assembly.Location);
        info.ArgumentList.Add("--Tests:" + name);
        info.Environment[marker] = name;
        using var child = Process.Start(info) ?? throw new InvalidOperationException("Não foi possível iniciar o processo de teste WPF.");
        var output = child.StandardOutput.ReadToEndAsync();
        var errors = child.StandardError.ReadToEndAsync();
        try
        {
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(child.ExitCode == 0, (await output) + (await errors));
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync();
            }
        }
    }
}
