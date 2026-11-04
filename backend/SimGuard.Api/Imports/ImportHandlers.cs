namespace SimGuard.Api.Imports;

// The import types the API knows, by type name.
public static class ImportHandlers
{
    private static readonly IImportHandler[] All = [];

    public static readonly IReadOnlyDictionary<string, IImportHandler> ByType =
        All.ToDictionary(h => h.Type, StringComparer.Ordinal);
}
