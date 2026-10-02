namespace CAHFS_Recharges.Models;

public class CaeiEmptyState
{
    public string Title { get; init; } = "No results";
    public string Message { get; init; } = "";
    public string? ActionHref { get; init; }
    public string ActionText { get; init; } = "Clear filters";
    public bool UseFilterNav { get; init; } = true;
}
