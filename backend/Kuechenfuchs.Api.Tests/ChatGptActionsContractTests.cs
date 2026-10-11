using System.Text.Json;
using System.Text.Json.Nodes;
using Kuechenfuchs.Api.ChatGpt;
using Kuechenfuchs.Api.Data;
using Kuechenfuchs.Api.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Xunit;

namespace Kuechenfuchs.Api.Tests;

public sealed class ChatGptActionsContractTests
{
    private static readonly string SpecPath = Path.Combine(AppContext.BaseDirectory, "kuechenfuchs-actions.openapi.json");
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly Dictionary<string, (string Method, string Path, string Scope, bool Consequential)> Expected = new()
    {
        ["listRecipes"] = ("get", "/api/v1/chatgpt/recipes", "recipes:read", false),
        ["getRecipe"] = ("get", "/api/v1/chatgpt/recipes/{recipeId}", "recipes:read", false),
        ["createRecipeDraft"] = ("post", "/api/v1/chatgpt/recipe-drafts", "recipes:write", false),
        ["requestRecipeSave"] = ("post", "/api/v1/chatgpt/recipe-save-requests", "recipes:write", false),
        ["confirmRecipeSave"] = ("post", "/api/v1/chatgpt/recipe-save-requests/{confirmationId}/confirm", "recipes:write", true),
    };

    [Fact]
    public void SpecificationIsValidOpenApi31WithoutDiagnostics()
    {
        var result = OpenApiDocument.Parse(File.ReadAllText(SpecPath), "json", new OpenApiReaderSettings());

        Assert.NotNull(result.Document);
        Assert.Empty(result.Diagnostic?.Errors ?? []);
        Assert.Equal("https://kuechenfuchs.zaddach.org", result.Document!.Servers!.Single().Url);
    }

    [Fact]
    public void ContainsExactlyTheNarrowSetOfOperationsWithScopesAndConsequentialFlags()
    {
        var operations = Operations().ToList();

        Assert.Equal(Expected.Keys.Order(), operations.Select(item => item.OperationId).Order());
        foreach (var (operationId, path, method, operation) in operations)
        {
            var expected = Expected[operationId];
            Assert.Equal(expected.Path, path);
            Assert.Equal(expected.Method, method);
            Assert.Equal(expected.Consequential, operation["x-openai-isConsequential"]!.GetValue<bool>());
            var scopes = operation["security"]!.AsArray().Single()!["keycloak"]!.AsArray().Select(scope => scope!.GetValue<string>());
            Assert.Equal([expected.Scope], scopes);
        }
    }

    [Fact]
    public void RespectsOpenAiDescriptionLimits()
    {
        // OpenAI production notes (11.10.2026): 300 characters per endpoint summary/description, 700 per parameter description.
        foreach (var (_, _, _, operation) in Operations())
        {
            Assert.True(operation["summary"]!.GetValue<string>().Length <= 300);
            Assert.True(operation["description"]!.GetValue<string>().Length <= 300);
            foreach (var parameter in operation["parameters"]?.AsArray() ?? [])
            {
                Assert.True(parameter!["description"]!.GetValue<string>().Length <= 700);
            }
        }
    }

    [Fact]
    public void InputSchemasForbidAdditionalPropertiesAndContainNoHouseholdOrIdFields()
    {
        var schemas = Spec()["components"]!["schemas"]!;
        foreach (var name in new[] { "RecipeInput", "DraftRequest", "ConfirmRequest" })
        {
            Assert.False(schemas[name]!["additionalProperties"]!.GetValue<bool>());
            var properties = schemas[name]!["properties"]!.AsObject().Select(property => property.Key).ToList();
            Assert.DoesNotContain(properties, key => key.Contains("household", StringComparison.OrdinalIgnoreCase)
                || key is "id" or "version" or "isArchived" or "foodId" or "userId");
        }

        Assert.DoesNotContain("householdId", Spec()["paths"]!.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void UnitCodeEnumMatchesSeededUnits()
    {
        var options = new DbContextOptionsBuilder<KuechenfuchsDbContext>().UseNpgsql("Host=unused").Options;
        using var database = new KuechenfuchsDbContext(options);
        var seeded = database.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model.FindEntityType(typeof(Unit))!.GetSeedData().Select(row => (string)row["Code"]!).Order();
        var enumValues = Spec()["components"]!["schemas"]!["RecipeInput"]!["properties"]!["ingredients"]!["items"]!["properties"]!["unitCode"]!["enum"]!
            .AsArray().Select(value => value!.GetValue<string>()).Order();

        Assert.Equal(seeded, enumValues);
    }

    [Theory]
    [InlineData("householdId", "\"00000000-0000-0000-0000-000000000001\"")]
    [InlineData("id", "\"00000000-0000-0000-0000-000000000001\"")]
    [InlineData("isArchived", "true")]
    [InlineData("version", "\"00000000-0000-0000-0000-000000000001\"")]
    public void RecipeInputRejectsMassAssignment(string property, string value)
    {
        var json = $$"""
            {"name":"Test","description":"","baseServings":2,"preparationMinutes":10,"category":"Abendessen","tags":[],
             "ingredients":[{"name":"Salz","amount":null,"unitCode":"pinch"}],"steps":["Kochen"],"{{property}}":{{value}}}
            """;

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ChatGptRecipeInput>(json, Web));
    }

    [Fact]
    public void NestedIngredientAndConfirmationRejectUnknownMembers()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ChatGptIngredientInput>(
            """{"name":"Salz","amount":1,"unitCode":"g","foodId":"00000000-0000-0000-0000-000000000001"}""", Web));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ChatGptConfirmSaveRequest>(
            """{"confirmed":true,"recipeId":"00000000-0000-0000-0000-000000000001"}""", Web));
        Assert.True(JsonSerializer.Deserialize<ChatGptConfirmSaveRequest>("""{"confirmed":true}""", Web)!.Confirmed);
    }

    private static JsonNode Spec() => JsonNode.Parse(File.ReadAllText(SpecPath))!;

    private static IEnumerable<(string OperationId, string Path, string Method, JsonObject Operation)> Operations()
    {
        foreach (var (path, item) in Spec()["paths"]!.AsObject())
        {
            foreach (var (method, operation) in item!.AsObject())
            {
                yield return (operation!["operationId"]!.GetValue<string>(), path, method, operation.AsObject());
            }
        }
    }
}
