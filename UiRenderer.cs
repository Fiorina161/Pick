using Spectre.Console;

namespace pick;

/**
 * Responsible for rendering the terminal UI for the task picker application.
 * Uses Spectre.Console to create a two-pane interface where users can navigate
 * between categories and tasks, with visual feedback for selection and focus state.
 */
internal sealed class UiRenderer
{
	/**
	 * Renders the complete UI layout including title bar, dual-pane navigation,
	 * and status information. The display is cleared and redrawn on each call
	 * to ensure a consistent state regardless of previous terminal content.
	 * 
	 * Focus state determines which pane shows the highlight style (black on yellow)
	 * versus the subdued style (yellow text only), guiding the user's attention
	 * to where keyboard input will be directed.
	 */
	public void Render(string configPath, List<string> categories, int selectedCategory,
		List<string> tasks, int selectedTask, bool focusOnCategories, string status)
	{
		AnsiConsole.Clear();

		var title = new Grid();
		title.AddColumn().AddColumn();
		title.AddRow("[bold green]pick[/] [dim]-[/] [italic]Task Launcher[/]", $"[dim]{Markup.Escape(configPath)}[/]");

		AnsiConsole.Write(new Panel(title).Border(BoxBorder.Rounded));

		var columns = new Grid();
		columns.AddColumn(new GridColumn().NoWrap()).AddColumn(new GridColumn());
		columns.AddRow(
			new Panel(new Markup(BuildList(categories, selectedCategory, focusOnCategories)))
				.Header("Categories").Border(BoxBorder.Rounded).Expand(),
			new Panel(new Markup(BuildList(tasks, selectedTask, !focusOnCategories)))
				.Header("Tasks").Border(BoxBorder.Rounded).Expand());
		AnsiConsole.Write(columns);

		AnsiConsole.WriteLine();
		AnsiConsole.MarkupLine("[dim]Up/Down move • Left/Right/Tab switch pane • Enter launch • E edit • Q quit[/]");
		AnsiConsole.MarkupLine($"[yellow]Status:[/] {Markup.Escape(status)}");
	}

	/**
	 * Builds a formatted list string with visual indicators for the selected item.
	 */
	private static string BuildList(List<string> items, int selected, bool active)
	{
		if (items.Count == 0) return "[dim](empty)[/]";

		return string.Join(Environment.NewLine, items.Select((item, i) =>
		{
			var name = Markup.Escape(item);
			return i == selected
				? active ? $"[black on yellow]> {name}[/]" : $"[yellow]> {name}[/]"
				: $"  {name}";
		}));
	}
}