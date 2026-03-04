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
	public static int Start(string command, string workingDirectory)
	{
		var (fileName, args) = OperatingSystem.IsWindows()
			? ("cmd.exe", ["/C", command])
			: (File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh", new[] { "-lc", $"\"{command}\"" });

		var pid = TryLaunch(useShellExecute: false, createNoWindow: false, workingDirectory, fileName, args);
		return pid != 0 ? pid : throw new InvalidOperationException("Process start failed.");
	}

	/**
     * Attempts to open a file in the user's preferred text editor.
     */
	public static bool TryOpenEditor(string filePath)
	{
		try
		{
			if (OperatingSystem.IsWindows())
				return TryLaunch(useShellExecute: false, createNoWindow: true, null, "cmd.exe", "/c", "start", "", filePath) != 0;

			if (OperatingSystem.IsMacOS())
				return TryLaunch(useShellExecute: true, createNoWindow: false, null, "open", "-e", filePath) != 0;

			var editor = Environment.GetEnvironmentVariable("EDITOR");
			return !string.IsNullOrWhiteSpace(editor) && TryLaunch(useShellExecute: true, createNoWindow: false, null, editor, filePath) != 0
				|| TryLaunch(useShellExecute: true, createNoWindow: false, null, "xdg-open", filePath) != 0;
		}
		catch { return false; }
	}

	/**
     * Safely attempts to start a process, waits for completion, and returns the process ID.
     */
	private static int TryLaunch(bool useShellExecute, bool createNoWindow, string? workingDirectory, string fileName, params string[] args)
	{
		var psi = new ProcessStartInfo(fileName)
		{
			UseShellExecute = useShellExecute,
			CreateNoWindow = createNoWindow
		};

		if (!string.IsNullOrWhiteSpace(workingDirectory))
			psi.WorkingDirectory = workingDirectory;

		foreach (var arg in args)
			psi.ArgumentList.Add(arg);

		using var process = Process.Start(psi);
		if (process != null)
		{
			process.WaitForExit();
			return process.Id;
		}
		return 0;
	}
}