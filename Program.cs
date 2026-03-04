using System.Diagnostics;
using System.Text;
using Spectre.Console;

namespace pick;

internal static class Program
{
	private const string ConfigFileName = "pick.ini";
	private const string RecentCategoryName = "Recently launched";
	private const int MaxRecent = 10;

	private const string RecentCommentPrefixV2 = "pick:recent:v2:";
	private const string RecentCommentPrefixV1 = "pick:recent:v1:";
	private const string RecentHeaderComment = "; Recent history (auto-generated)";

	private static void Main()
	{
		Console.OutputEncoding = Encoding.UTF8;
		Console.InputEncoding = Encoding.UTF8;

		var configPath = FindConfigPath(ConfigFileName);
		if (configPath is null)
		{
			var suggested = Path.Combine(Environment.CurrentDirectory, ConfigFileName);
			File.WriteAllText(suggested, DefaultIniTemplate());
			configPath = suggested;
		}

		PickConfig config;
		try
		{
			config = ParseIni(configPath);
		}
		catch (Exception ex)
		{
			AnsiConsole.MarkupLine(
				$"[red]Failed to parse config:[/] {Markup.Escape(ex.Message)}");
			return;
		}

		var recent = LoadRecentFromIniComments(configPath, MaxRecent);
		PruneRecent(recent, config);
		_ = TrySaveRecentToIniComments(configPath, recent, out _);

		var selectedCategory = 0;
		var focus = FocusPane.Categories;
		var selectedTaskByCategory = new Dictionary<string, int>(
			StringComparer.CurrentCultureIgnoreCase);
		var status = "Ready";

		while (true)
		{
			PruneRecent(recent, config);

			var realCategories = config.Sections.Keys
				.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
				.ToList();

			var categories = new List<string>();
			if (recent.Count > 0)
				categories.Add(RecentCategoryName);
			categories.AddRange(realCategories);

			if (categories.Count == 0)
			{
				AnsiConsole.Clear();
				AnsiConsole.MarkupLine("[yellow]No categories found in config.[/]");
				AnsiConsole.MarkupLine("Press E to edit, R to reload, Q to quit.");

				var k = Console.ReadKey(intercept: true).Key;
				if (k == ConsoleKey.Q || k == ConsoleKey.Escape)
					break;

				if (k == ConsoleKey.E)
				{
					if (TryOpenInEditor(configPath))
					{
						TryReload(configPath, ref config, ref recent, ref status);
					}
					else
					{
						status = "Could not open editor";
					}
				}

				if (k == ConsoleKey.R)
					TryReload(configPath, ref config, ref recent, ref status);

				continue;
			}

			selectedCategory = Math.Clamp(selectedCategory, 0, categories.Count - 1);
			var categoryName = categories[selectedCategory];

			var launchItems = BuildLaunchItemsForCategory(categoryName, config, recent);

			if (!selectedTaskByCategory.ContainsKey(categoryName))
				selectedTaskByCategory[categoryName] = 0;

			selectedTaskByCategory[categoryName] = Math.Clamp(
				selectedTaskByCategory[categoryName],
				0,
				Math.Max(0, launchItems.Count - 1));

			RenderUi(
				configPath,
				categories,
				selectedCategory,
				launchItems.Select(x => x.DisplayName).ToList(),
				selectedTaskByCategory[categoryName],
				focus,
				status);

			var keyInfo = Console.ReadKey(intercept: true);
			var key = keyInfo.Key;

			if (key == ConsoleKey.Q || key == ConsoleKey.Escape)
				break;

			if (key == ConsoleKey.Tab || key == ConsoleKey.LeftArrow ||
			    key == ConsoleKey.RightArrow)
			{
				focus = focus == FocusPane.Categories
					? FocusPane.Tasks
					: FocusPane.Categories;
				continue;
			}

			if (key == ConsoleKey.E)
			{
				if (TryOpenInEditor(configPath))
				{
					TryReload(configPath, ref config, ref recent, ref status);
				}
				else
				{
					status = "Could not open editor";
				}

				continue;
			}

			if (key == ConsoleKey.R)
			{
				TryReload(configPath, ref config, ref recent, ref status);
				continue;
			}

			if (key == ConsoleKey.UpArrow)
			{
				if (focus == FocusPane.Categories)
				{
					selectedCategory =
						(selectedCategory - 1 + categories.Count) % categories.Count;
				}
				else if (launchItems.Count > 0)
				{
					selectedTaskByCategory[categoryName] =
						(selectedTaskByCategory[categoryName] - 1 + launchItems.Count) %
						launchItems.Count;
				}

				continue;
			}

			if (key == ConsoleKey.DownArrow)
			{
				if (focus == FocusPane.Categories)
				{
					selectedCategory = (selectedCategory + 1) % categories.Count;
				}
				else if (launchItems.Count > 0)
				{
					selectedTaskByCategory[categoryName] =
						(selectedTaskByCategory[categoryName] + 1) %
						launchItems.Count;
				}

				continue;
			}

			if (key == ConsoleKey.Enter)
			{
				if (launchItems.Count == 0)
				{
					status = "No task in selected category";
					continue;
				}

				var taskIndex = selectedTaskByCategory[categoryName];
				var item = launchItems[taskIndex];

				try
				{
					var workingDir = Path.GetDirectoryName(configPath) ??
					                 Environment.CurrentDirectory;

					var proc = StartCommand(item.Command, workingDir);

					AddRecent(recent, item, MaxRecent);
					PruneRecent(recent, config);

					if (!TrySaveRecentToIniComments(configPath, recent, out var saveError))
					{
						status = $"Launched {item.OriginalName} (PID {proc.Id}), " +
						         $"but failed to save recents: {saveError}";
					}
					else
					{
						status = $"Launched {item.OriginalName} (PID {proc.Id})";
					}
				}
				catch (Exception ex)
				{
					status = $"Launch failed: {ex.Message}";
				}
			}
		}
	}

	private static void RenderUi(
		string configPath,
		List<string> categories,
		int selectedCategory,
		List<string> taskDisplayNames,
		int selectedTask,
		FocusPane focus,
		string status)
	{
		AnsiConsole.Clear();

		var title = new Grid();
		title.AddColumn();
		title.AddColumn();
		title.AddRow("[bold green]pick[/]", $"[dim]{Markup.Escape(configPath)}[/]");
		title.AddRow(
			$"Categories: [blue]{categories.Count}[/]",
			$"Tasks in selected: [blue]{taskDisplayNames.Count}[/]");

		AnsiConsole.Write(
			new Panel(title)
				.Border(BoxBorder.Rounded));

		var catLines = BuildLines(
			categories,
			selectedCategory,
			focus == FocusPane.Categories);

		var taskLines = BuildLines(
			taskDisplayNames,
			selectedTask,
			focus == FocusPane.Tasks);

		var columns = new Grid();
		columns.AddColumn(new GridColumn().NoWrap());
		columns.AddColumn(new GridColumn());

		columns.AddRow(
			new Panel(new Markup(catLines))
				.Header("Categories")
				.Border(BoxBorder.Rounded)
				.Expand(),
			new Panel(new Markup(taskLines))
				.Header("Tasks")
				.Border(BoxBorder.Rounded)
				.Expand());

		AnsiConsole.Write(columns);

		AnsiConsole.WriteLine();
		AnsiConsole.MarkupLine(
			"[dim]Up/Down move • Left/Right/Tab switch pane • Enter launch • " +
			"E edit • R reload • Q quit[/]");
		AnsiConsole.MarkupLine($"[yellow]Status:[/] {Markup.Escape(status)}");
	}

	private static string BuildLines(
		List<string> items,
		int selectedIndex,
		bool isActivePane)
	{
		if (items.Count == 0)
			return "[dim](empty)[/]";

		var lines = new List<string>(items.Count);

		for (var i = 0; i < items.Count; i++)
		{
			var name = Markup.Escape(items[i]);

			if (i == selectedIndex)
			{
				if (isActivePane)
					lines.Add($"[black on yellow]> {name}[/]");
				else
					lines.Add($"[yellow]> {name}[/]");
			}
			else
			{
				lines.Add($"  {name}");
			}
		}

		return string.Join(Environment.NewLine, lines);
	}

	private static List<LaunchItem> BuildLaunchItemsForCategory(
		string categoryName,
		PickConfig config,
		List<RecentTaskRef> recent)
	{
		if (string.Equals(
			    categoryName,
			    RecentCategoryName,
			    StringComparison.CurrentCultureIgnoreCase))
		{
			var list = new List<LaunchItem>();

			foreach (var r in recent.OrderByDescending(x => x.LaunchedAt))
			{
				if (!TryResolveTask(config, r.Section, r.Key, out var task))
					continue;

				list.Add(new LaunchItem(
					DisplayName: $"{task.Name} — {r.Section}",
					OriginalName: task.Name,
					Command: task.Command,
					SourceCategory: r.Section));
			}

			return list;
		}

		if (!config.Sections.TryGetValue(categoryName, out var tasks))
			return new List<LaunchItem>();

		return tasks
			.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
			.Select(t => new LaunchItem(
				DisplayName: t.Name,
				OriginalName: t.Name,
				Command: t.Command,
				SourceCategory: categoryName))
			.ToList();
	}

	private static void AddRecent(
		List<RecentTaskRef> recent,
		LaunchItem item,
		int maxRecent)
	{
		recent.RemoveAll(r =>
			string.Equals(
				r.Section,
				item.SourceCategory,
				StringComparison.CurrentCultureIgnoreCase) &&
			string.Equals(
				r.Key,
				item.OriginalName,
				StringComparison.CurrentCultureIgnoreCase));

		recent.Insert(0, new RecentTaskRef(
			Section: item.SourceCategory,
			Key: item.OriginalName,
			LaunchedAt: DateTimeOffset.Now));

		if (recent.Count > maxRecent)
			recent.RemoveRange(maxRecent, recent.Count - maxRecent);
	}

	private static void PruneRecent(List<RecentTaskRef> recent, PickConfig config)
	{
		recent.RemoveAll(r => !TaskExists(config, r.Section, r.Key));
	}

	private static bool TaskExists(PickConfig config, string section, string key)
	{
		if (!config.Sections.TryGetValue(section, out var tasks))
			return false;

		return tasks.Any(t =>
			string.Equals(t.Name, key, StringComparison.CurrentCultureIgnoreCase));
	}

	private static bool TryResolveTask(
		PickConfig config,
		string section,
		string key,
		out TaskEntry task)
	{
		task = default!;

		if (!config.Sections.TryGetValue(section, out var tasks))
			return false;

		var found = tasks.FirstOrDefault(t =>
			string.Equals(t.Name, key, StringComparison.CurrentCultureIgnoreCase));

		if (found is null)
			return false;

		task = found;
		return true;
	}

	private static void TryReload(
		string configPath,
		ref PickConfig config,
		ref List<RecentTaskRef> recent,
		ref string status)
	{
		try
		{
			config = ParseIni(configPath);
			recent = LoadRecentFromIniComments(configPath, MaxRecent);
			PruneRecent(recent, config);

			if (!TrySaveRecentToIniComments(configPath, recent, out var saveError))
			{
				status = $"Configuration reloaded, but recent cleanup failed: {saveError}";
			}
			else
			{
				status = "Configuration reloaded";
			}
		}
		catch (Exception ex)
		{
			status = $"Reload failed: {ex.Message}";
		}
	}

	private static List<RecentTaskRef> LoadRecentFromIniComments(string path, int maxRecent)
	{
		var items = new List<RecentTaskRef>();

		if (!File.Exists(path))
			return items;

		foreach (var raw in File.ReadLines(path))
		{
			if (TryParseRecentV2Line(raw, out var recent))
				items.Add(recent);
		}

		var dedup = items
			.GroupBy(
				x => $"{x.Section}\u001f{x.Key}",
				StringComparer.CurrentCultureIgnoreCase)
			.Select(g => g.OrderByDescending(x => x.LaunchedAt).First())
			.OrderByDescending(x => x.LaunchedAt)
			.Take(maxRecent)
			.ToList();

		return dedup;
	}

	private static bool TrySaveRecentToIniComments(
		string path,
		List<RecentTaskRef> recent,
		out string error)
	{
		try
		{
			var lines = File.Exists(path)
				? File.ReadAllLines(path).ToList()
				: new List<string>();

			lines = lines
				.Where(line =>
					!IsRecentCommentLine(line) &&
					!string.Equals(
						line.Trim(),
						RecentHeaderComment,
						StringComparison.Ordinal))
				.ToList();

			while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
				lines.RemoveAt(lines.Count - 1);

			var toWrite = recent
				.OrderByDescending(x => x.LaunchedAt)
				.Take(MaxRecent)
				.ToList();

			if (toWrite.Count > 0)
			{
				if (lines.Count > 0)
					lines.Add(string.Empty);

				lines.Add(RecentHeaderComment);

				foreach (var r in toWrite)
					lines.Add(SerializeRecentV2Line(r));
			}

			File.WriteAllLines(path, lines);
			error = string.Empty;
			return true;
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
	}

	private static bool IsRecentCommentLine(string line)
	{
		var body = GetCommentBody(line);
		if (body is null)
			return false;

		return body.StartsWith(RecentCommentPrefixV2, StringComparison.Ordinal) ||
		       body.StartsWith(RecentCommentPrefixV1, StringComparison.Ordinal);
	}

	private static string SerializeRecentV2Line(RecentTaskRef r)
	{
		var ts = r.LaunchedAt.ToUnixTimeSeconds();
		var section = EscapeField(r.Section);
		var key = EscapeField(r.Key);

		return $"; {RecentCommentPrefixV2}{ts}|{section}|{key}";
	}

	private static bool TryParseRecentV2Line(string line, out RecentTaskRef recent)
	{
		recent = default!;

		var body = GetCommentBody(line);
		if (body is null ||
		    !body.StartsWith(RecentCommentPrefixV2, StringComparison.Ordinal))
		{
			return false;
		}

		var payload = body[RecentCommentPrefixV2.Length..];
		var parts = SplitEscaped(payload);
		if (parts.Count != 3)
			return false;

		if (!long.TryParse(parts[0], out var unix))
			return false;

		DateTimeOffset launchedAt;
		try
		{
			launchedAt = DateTimeOffset.FromUnixTimeSeconds(unix);
		}
		catch
		{
			return false;
		}

		var section = UnescapeField(parts[1]);
		var key = UnescapeField(parts[2]);

		if (string.IsNullOrWhiteSpace(section) || string.IsNullOrWhiteSpace(key))
			return false;

		recent = new RecentTaskRef(section, key, launchedAt);
		return true;
	}

	private static string? GetCommentBody(string line)
	{
		var trimmed = line.TrimStart();
		if (!trimmed.StartsWith(";"))
			return null;

		return trimmed[1..].TrimStart();
	}

	private static string EscapeField(string value) =>
		value.Replace("\\", "\\\\").Replace("|", "\\|");

	private static string UnescapeField(string value)
	{
		var sb = new StringBuilder(value.Length);
		var escaped = false;

		foreach (var ch in value)
		{
			if (escaped)
			{
				sb.Append(ch);
				escaped = false;
				continue;
			}

			if (ch == '\\')
			{
				escaped = true;
				continue;
			}

			sb.Append(ch);
		}

		if (escaped)
			sb.Append('\\');

		return sb.ToString();
	}

	private static List<string> SplitEscaped(string input)
	{
		var parts = new List<string>();
		var sb = new StringBuilder();
		var escaped = false;

		foreach (var ch in input)
		{
			if (escaped)
			{
				sb.Append(ch);
				escaped = false;
				continue;
			}

			if (ch == '\\')
			{
				escaped = true;
				continue;
			}

			if (ch == '|')
			{
				parts.Add(sb.ToString());
				sb.Clear();
				continue;
			}

			sb.Append(ch);
		}

		if (escaped)
			sb.Append('\\');

		parts.Add(sb.ToString());
		return parts;
	}

	private static Process StartCommand(string command, string workingDirectory)
	{
		if (OperatingSystem.IsWindows())
		{
			var psi = new ProcessStartInfo("cmd.exe")
			{
				WorkingDirectory = workingDirectory,
				UseShellExecute = false
			};
			psi.ArgumentList.Add("/c");
			psi.ArgumentList.Add(command);

			return Process.Start(psi)
			       ?? throw new InvalidOperationException("Process start failed.");
		}

		var shell = File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";
		var unix = new ProcessStartInfo(shell)
		{
			WorkingDirectory = workingDirectory,
			UseShellExecute = false
		};
		unix.ArgumentList.Add("-lc");
		unix.ArgumentList.Add(command);

		return Process.Start(unix)
		       ?? throw new InvalidOperationException("Process start failed.");
	}

	private static bool TryOpenInEditor(string filePath)
	{
		try
		{
			if (OperatingSystem.IsWindows())
				return StartWithShellExecute("notepad.exe", filePath);

			if (OperatingSystem.IsMacOS())
				return StartWithShellExecute("open", "-e", filePath);

			var editor = Environment.GetEnvironmentVariable("EDITOR");
			if (!string.IsNullOrWhiteSpace(editor) &&
			    StartWithShellExecute(editor, filePath))
			{
				return true;
			}

			return StartWithShellExecute("xdg-open", filePath);
		}
		catch
		{
			return false;
		}
	}

	private static bool StartWithShellExecute(string fileName, params string[] args)
	{
		var psi = new ProcessStartInfo(fileName)
		{
			UseShellExecute = true
		};

		foreach (var arg in args)
			psi.ArgumentList.Add(arg);

		return Process.Start(psi) is not null;
	}

	private static string? FindConfigPath(string configFileName)
	{
		var dir = new DirectoryInfo(Environment.CurrentDirectory);

		while (dir is not null)
		{
			var candidate = Path.Combine(dir.FullName, configFileName);
			if (File.Exists(candidate))
				return candidate;

			dir = dir.Parent;
		}

		return null;
	}

	private static PickConfig ParseIni(string path)
	{
		var sections = new Dictionary<string, List<TaskEntry>>(
			StringComparer.CurrentCultureIgnoreCase);

		string? currentSection = null;
		var lines = File.ReadAllLines(path);

		for (var i = 0; i < lines.Length; i++)
		{
			var raw = lines[i];
			var line = raw.Trim();

			if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#"))
				continue;

			if (line.StartsWith("[") && line.EndsWith("]"))
			{
				var section = line[1..^1].Trim();
				if (section.Length == 0)
					throw new FormatException($"Empty section at line {i + 1}");

				currentSection = section;
				if (!sections.ContainsKey(section))
					sections[section] = new List<TaskEntry>();

				continue;
			}

			var eq = line.IndexOf('=');
			if (eq <= 0)
				throw new FormatException($"Expected key=value at line {i + 1}");

			if (currentSection is null)
				throw new FormatException(
					$"Task outside section at line {i + 1}");

			var name = line[..eq].Trim();
			var command = line[(eq + 1)..].Trim();

			if (name.Length == 0 || command.Length == 0)
				throw new FormatException($"Invalid task at line {i + 1}");

			sections[currentSection].Add(new TaskEntry(name, command));
		}

		return new PickConfig(sections);
	}

	private static string DefaultIniTemplate() =>
		"""
		[Build]
		Restore=dotnet restore
		Build=dotnet build
		Test=dotnet test

		[Run]
		Api=dotnet run --project src/MyApi
		Ui=npm --prefix src/ui start
		""";

	private enum FocusPane
	{
		Categories,
		Tasks
	}

	private sealed record TaskEntry(string Name, string Command);

	private sealed record PickConfig(Dictionary<string, List<TaskEntry>> Sections);

	private sealed record LaunchItem(
		string DisplayName,
		string OriginalName,
		string Command,
		string SourceCategory);

	private sealed record RecentTaskRef(
		string Section,
		string Key,
		DateTimeOffset LaunchedAt);
}