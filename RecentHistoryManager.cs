// ReSharper disable GrammarMistakeInComment
namespace Sidekick;

/**
 * Manages a time-ordered history of recently launched tasks, persisting them
 * to the configuration file as comments to preserve the original file structure.
 */
internal sealed class RecentHistoryManager
{
	private const string PREFIX_V2 = "sidekick:recent:v2:";
	private const string HEADER_COMMENT = "; Recent history (auto-generated)";
	private readonly int _maxRecent;

	public List<RecentItems> Recent { get; private set; } = [];

	/**
	 * Creates a history manager with a configurable limit on tracked items.
	 * Prevents unbounded growth while keeping the most relevant recent launches.
	 */
	public RecentHistoryManager(int maxRecent = 10) => _maxRecent = maxRecent;

	/**
	 * Restores recent history from the configuration file, deduplicating entries
	 * by section+key and keeping only the most recent launch timestamp for each.
	 * Gotta love LINQ for this kind of job...
	 */
	public void Load(string configPath)
	{
		if (!File.Exists(configPath)) return;

		Recent = File.ReadLines(configPath)
			.Select(TryParseLine)
			.Where(r => r is not null)
			.GroupBy(r => $"{r!.Section}\u001f{r.Key}", StringComparer.CurrentCultureIgnoreCase)
			.Select(g => g.OrderByDescending(x => x!.LaunchedAt).First()!)
			.OrderByDescending(x => x.LaunchedAt)
			.Take(_maxRecent)
			.ToList();
	}

	/**
	 * Records a newly launched task at the top of the history, removing any
	 * previous occurrence to avoid duplicates and maintain chronological order.
	 */
	public void Add(LaunchItem item)
	{
		Recent.RemoveAll(r => r.Section.Equals(item.SourceCategory, StringComparison.CurrentCultureIgnoreCase)
							  && r.Key.Equals(item.OriginalName, StringComparison.CurrentCultureIgnoreCase));

		Recent.Insert(0, new RecentItems(item.SourceCategory, item.OriginalName, DateTimeOffset.Now));

		if (Recent.Count > _maxRecent)
			Recent.RemoveRange(_maxRecent, Recent.Count - _maxRecent);
	}

	/**
	 * Removes history entries for tasks that no longer exist in the configuration,
	 * preventing references to deleted or renamed tasks from accumulating.
	 */
	public void Prune(Configuration config) =>
		Recent.RemoveAll(r => !TaskExists(config, r.Section, r.Key));

	/**
	 * Persists recent history by appending it as comments to the configuration file,
	 * preserving all non-history content while replacing any existing history section.
	 */
	public bool Save(string configPath, out string error)
	{
		try
		{
			var lines = File.Exists(configPath)
				? File.ReadAllLines(configPath)
					.TakeWhile(l => !l.Equals(HEADER_COMMENT, StringComparison.Ordinal))
					.ToList()
				: [];

			while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
				lines.RemoveAt(lines.Count - 1);

			if (Recent.Count > 0)
			{
				if (lines.Count > 0) lines.Add(string.Empty);
				lines.Add(HEADER_COMMENT);
				lines.AddRange(Recent.OrderByDescending(r => r.LaunchedAt).Take(_maxRecent).Select(SerializeLine));
			}

			File.WriteAllLines(configPath, lines);
			error = string.Empty;
			return true;
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
	}

	/**
	 * Verifies that a task referenced in history still exists in the current configuration
	 * to avoid showing stale or invalid recent items.
	 */
	private static bool TaskExists(Configuration config, string section, string key) =>
		config.Sections.TryGetValue(section, out var tasks)
		&& tasks.Any(t => t.Name.Equals(key, StringComparison.CurrentCultureIgnoreCase));

	/**
	 * Encodes a recent item into a pipe-delimited comment line with timestamp,
	 * escaping special characters to prevent parsing ambiguity.
	 */
	private static string SerializeLine(RecentItems r) =>
		$"; {PREFIX_V2}{r.LaunchedAt.ToUnixTimeSeconds()}|{r.Section}|{r.Key}";

	/**
	 * Attempts to decode a recent item from a comment line, returning null if the format
	 * is invalid or from an incompatible version.
	 */
	private static RecentItems? TryParseLine(string line)
	{
		var body = GetCommentBody(line);
		if (body?.StartsWith(PREFIX_V2) != true)
			return null;

		var parts = Split(body[PREFIX_V2.Length..]);
		if (parts.Count != 3 || !long.TryParse(parts[0], out var unix))
			return null;

		try
		{
			var (section, key) = (parts[1], parts[2]);
			return string.IsNullOrWhiteSpace(section) || string.IsNullOrWhiteSpace(key)
				? null
				: new RecentItems(section, key, DateTimeOffset.FromUnixTimeSeconds(unix));
		}
		catch
		{
			return null;
		}
	}

	/**
	 * Extracts the content following a semicolon comment marker,
	 * or null if the line is not a comment.
	 */
	private static string? GetCommentBody(string line)
	{
		var trimmed = line.TrimStart();
		return trimmed.StartsWith(';') ? trimmed[1..].TrimStart() : null;
	}

	/**
     * Splits on pipe characters to parse the delimited recent history format.
     */
	private static List<string> Split(string input) => [.. input.Split('|')];
}