namespace Sidekick;

internal sealed record LaunchItem(string DisplayName, string OriginalName, string Command, string SourceCategory);

internal sealed record RecentItems(string Section, string Key, DateTimeOffset LaunchedAt);

internal sealed record Configuration(Dictionary<string, List<TaskItem>> Sections, Dictionary<string, List<string>> Notes);

internal sealed record TaskItem(string Name, string Command);