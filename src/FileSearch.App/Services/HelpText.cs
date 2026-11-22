namespace FileSearch.App.Services;

/// <summary>The search syntax help shown from the "?" button and the Tools menu.</summary>
public static class HelpText
{
    public const string SearchSyntaxTitle = "Search syntax";

    public const string SearchSyntax =
        "File name\n" +
        "  DOS expression: * and ? wildcards. Separate several patterns with ; or ,  (e.g. *.cs;*.xaml)\n" +
        "  Regular expression: switch the mode on the Options tab.\n\n" +
        "Containing text\n" +
        "  Plain text: searches for the exact text.\n" +
        "  Boolean: AND, OR, NOT (upper case), parentheses and \"quoted phrases\".\n" +
        "      Words next to each other mean AND.   e.g.  alias AND (select OR use) NOT \"for test\"\n" +
        "  Regular expression: .NET syntax, ^ and $ match at line start/end.\n\n" +
        "Look in\n" +
        "  Several folders can be separated with ;\n\n" +
        "Aa toggles case sensitivity.";
}
