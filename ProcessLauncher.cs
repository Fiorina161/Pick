using System.Diagnostics;

namespace pick;

/**
 * Provides cross-platform process launching capabilities, abstracting platform-specific
 * shell invocation and editor handling to enable consistent external command execution.
 */
internal static class ProcessLauncher
{
	/**
	 * Launches a shell command, ensuring commands execute as they would in an
	 * interactive terminal session with proper environment context.
	 */
	public static Process Start(string command, string workingDirectory)
	{
		var psi = OperatingSystem.IsWindows()
			? new ProcessStartInfo("cmd.exe") { ArgumentList = { "/c", command } }
			: new ProcessStartInfo(File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh") { ArgumentList = { "-lc", command } };

		psi.WorkingDirectory = workingDirectory;
		psi.UseShellExecute = false;

		return Process.Start(psi) ?? throw new InvalidOperationException("Process start failed.");
	}

	/**
	 * Attempts to open a file in the user's preferred text editor.
	 */
	public static bool TryOpenEditor(string filePath)
	{
		try
		{
			if (OperatingSystem.IsWindows())
				return TryLaunch("start", filePath);

			if (OperatingSystem.IsMacOS())
				return TryLaunch("open", "-e", filePath);

			var editor = Environment.GetEnvironmentVariable("EDITOR");
			return !string.IsNullOrWhiteSpace(editor) && TryLaunch(editor, filePath) || TryLaunch("xdg-open", filePath);
		}
		catch { return false; }
	}

	/**
	 * Safely attempts to start a process without throwing exceptions.
	 */
	private static bool TryLaunch(string fileName, params string[] args)
	{
		var psi = new ProcessStartInfo(fileName)
		{
			UseShellExecute = true
		};

		foreach (var arg in args)
			psi.ArgumentList.Add(arg);

		return Process.Start(psi) is not null;
	}
}