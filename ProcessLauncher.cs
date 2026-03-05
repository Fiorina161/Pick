using System.Diagnostics;

namespace pick;

internal static class ProcessLauncher
{
	public static Process? Run(string command)
	{
		var startInfo = new ProcessStartInfo
		{
			FileName = "cmd.exe",
			Arguments = $"/C {command}"
		};

		return Process.Start(startInfo) ?? throw new Exception("Unable to launch process");
	}
}