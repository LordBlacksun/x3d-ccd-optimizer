using System.Reflection;
using Xunit;

namespace X3DCcdInspector.Tests;

/// <summary>
/// Tests for the VdfParser internal class in X3DCcdInspector.Core.
/// Uses reflection to access the internal static Parse method.
/// </summary>
public class VdfParserTests
{
    private static readonly Type VdfParserType;
    private static readonly MethodInfo ParseMethod;

    static VdfParserTests()
    {
        var assembly = typeof(X3DCcdInspector.Core.GameLibraryScanner).Assembly;
        VdfParserType = assembly.GetType("X3DCcdInspector.Core.VdfParser")!;
        ParseMethod = VdfParserType.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static)!;
    }

    private static Dictionary<string, object> Parse(string content)
    {
        return (Dictionary<string, object>)ParseMethod.Invoke(null, [content])!;
    }

    [Fact]
    public void Parse_SimpleKeyValuePair()
    {
        var result = Parse("\"key\"\t\t\"value\"");
        Assert.Single(result);
        Assert.Equal("value", result["key"]);
    }

    [Fact]
    public void Parse_NestedBlocks()
    {
        var input = """
            "outer"
            {
                "inner"		"value"
            }
            """;
        var result = Parse(input);
        Assert.Single(result);
        Assert.IsType<Dictionary<string, object>>(result["outer"]);
        var nested = (Dictionary<string, object>)result["outer"];
        Assert.Equal("value", nested["inner"]);
    }

    [Fact]
    public void Parse_MultipleKeys()
    {
        var input = """
            "key1"		"value1"
            "key2"		"value2"
            "key3"		"value3"
            """;
        var result = Parse(input);
        Assert.Equal(3, result.Count);
        Assert.Equal("value1", result["key1"]);
        Assert.Equal("value2", result["key2"]);
        Assert.Equal("value3", result["key3"]);
    }

    [Fact]
    public void Parse_EscapedBackslashesInPaths()
    {
        var input = "\"path\"\t\t\"C:\\\\Program Files\\\\Steam\"";
        var result = Parse(input);
        Assert.Equal("C:\\Program Files\\Steam", result["path"]);
    }

    [Fact]
    public void Parse_EscapedQuotes()
    {
        var input = "\"key\"\t\t\"value with \\\"quotes\\\"\"";
        var result = Parse(input);
        Assert.Equal("value with \"quotes\"", result["key"]);
    }

    [Fact]
    public void Parse_SkipsLineComments()
    {
        var input = """
            // This is a comment
            "key"		"value"
            // Another comment
            """;
        var result = Parse(input);
        Assert.Single(result);
        Assert.Equal("value", result["key"]);
    }

    [Fact]
    public void Parse_EmptyInput_ReturnsEmptyDictionary()
    {
        var result = Parse("");
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_WhitespaceOnlyInput_ReturnsEmptyDictionary()
    {
        var result = Parse("   \n\t\n   ");
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_MalformedInput_DoesNotCrash()
    {
        // Missing closing quote
        var result = Parse("\"key");
        Assert.NotNull(result);

        // Random braces
        result = Parse("}{{}");
        Assert.NotNull(result);

        // Just garbage
        result = Parse("asdf!@#$");
        Assert.NotNull(result);
    }

    [Fact]
    public void Parse_RealAcfContent()
    {
        var content = """
            "AppState"
            {
            	"appid"		"220"
            	"name"		"Half-Life 2"
            	"installdir"		"Half-Life 2"
            	"StateFlags"		"4"
            }
            """;
        var result = Parse(content);
        Assert.Single(result);
        Assert.True(result.ContainsKey("AppState"));

        var appState = (Dictionary<string, object>)result["AppState"];
        Assert.Equal("220", appState["appid"]);
        Assert.Equal("Half-Life 2", appState["name"]);
        Assert.Equal("Half-Life 2", appState["installdir"]);
        Assert.Equal("4", appState["StateFlags"]);
    }

    [Fact]
    public void Parse_RealLibraryFoldersVdf()
    {
        var content = """
            "libraryfolders"
            {
            	"0"
            	{
            		"path"		"C:\\Program Files (x86)\\Steam"
            	}
            	"1"
            	{
            		"path"		"D:\\SteamLibrary"
            	}
            }
            """;
        var result = Parse(content);
        Assert.Single(result);
        Assert.True(result.ContainsKey("libraryfolders"));

        var folders = (Dictionary<string, object>)result["libraryfolders"];
        Assert.Equal(2, folders.Count);

        var folder0 = (Dictionary<string, object>)folders["0"];
        Assert.Equal("C:\\Program Files (x86)\\Steam", folder0["path"]);

        var folder1 = (Dictionary<string, object>)folders["1"];
        Assert.Equal("D:\\SteamLibrary", folder1["path"]);
    }

    [Fact]
    public void Parse_DeeplyNestedBlocks()
    {
        var input = """
            "level1"
            {
                "level2"
                {
                    "level3"
                    {
                        "deep"		"value"
                    }
                }
            }
            """;
        var result = Parse(input);
        var l1 = (Dictionary<string, object>)result["level1"];
        var l2 = (Dictionary<string, object>)l1["level2"];
        var l3 = (Dictionary<string, object>)l2["level3"];
        Assert.Equal("value", l3["deep"]);
    }

    [Fact]
    public void Parse_IsCaseInsensitive_ForKeys()
    {
        var input = "\"MyKey\"\t\t\"value\"";
        var result = Parse(input);
        // VdfParser uses StringComparer.OrdinalIgnoreCase
        Assert.True(result.ContainsKey("mykey"));
        Assert.True(result.ContainsKey("MYKEY"));
        Assert.True(result.ContainsKey("MyKey"));
    }

    [Fact]
    public void Parse_MixedKeyValueAndNestedBlocks()
    {
        var input = """
            "simple"		"value"
            "nested"
            {
                "child"		"childvalue"
            }
            "another"		"value2"
            """;
        var result = Parse(input);
        Assert.Equal(3, result.Count);
        Assert.Equal("value", result["simple"]);
        Assert.Equal("value2", result["another"]);
        var nested = (Dictionary<string, object>)result["nested"];
        Assert.Equal("childvalue", nested["child"]);
    }
}
