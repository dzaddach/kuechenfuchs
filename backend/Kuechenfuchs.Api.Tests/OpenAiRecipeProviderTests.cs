using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Kuechenfuchs.Api.Ai;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kuechenfuchs.Api.Tests;

public sealed class OpenAiRecipeProviderTests
{
    [Fact]
    public async Task RequestsSchemaConstrainedOutputAndParsesUsageWithoutLoggingContent()
    {
        string? capturedContent = null;
        AuthenticationHeaderValue? capturedAuthorization = null;
        long? capturedContentLength = null;
        bool? capturedChunked = null;
        var handler = new StubHandler(async (request, cancellationToken) =>
        {
            capturedAuthorization = request.Headers.Authorization;
            capturedContentLength = request.Content!.Headers.ContentLength;
            capturedChunked = request.Headers.TransferEncodingChunked;
            capturedContent = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse("""
                {"choices":[{"message":{"content":"{\"title\":\"Soup\"}"}}],"usage":{"prompt_tokens":500,"completion_tokens":300}}
                """);
        });
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);

        var result = await provider.GenerateAsync(
            new AiProviderInput("A vegetable soup", 2, ["potato"], [], []),
            CancellationToken.None);

        Assert.Equal(AiProviderFailure.None, result.Failure);
        Assert.Equal(500, result.PromptTokens);
        Assert.Equal(300, result.CompletionTokens);
        Assert.Contains("json_schema", capturedContent);
        Assert.Equal("Bearer", capturedAuthorization?.Scheme);
        Assert.Equal("unit-test-secret", capturedAuthorization?.Parameter);
        Assert.True(capturedContentLength > 0);
        Assert.NotEqual(true, capturedChunked);
    }

    [Fact]
    public async Task TreatsMalformedProviderPayloadAsInvalid()
    {
        using var client = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not-json", Encoding.UTF8, "application/json")
            })));
        var provider = CreateProvider(client);

        var result = await provider.GenerateAsync(new AiProviderInput("Soup", 2, [], [], []), CancellationToken.None);

        Assert.Equal(AiProviderFailure.InvalidResponse, result.Failure);
    }

    [Fact]
    public async Task MapsProviderRateLimitAndTimeoutToExplicitFailures()
    {
        using var rateLimitedClient = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests))));
        var rateLimitedProvider = CreateProvider(rateLimitedClient);
        var rateLimited = await rateLimitedProvider.GenerateAsync(
            new AiProviderInput("Soup", 2, [], [], []),
            CancellationToken.None);
        Assert.Equal(AiProviderFailure.RateLimited, rateLimited.Failure);
        Assert.True(rateLimited.DefinitelyNoCharge);

        using var timeoutClient = new HttpClient(new StubHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }))
        {
            Timeout = TimeSpan.FromMilliseconds(20)
        };
        var timeoutProvider = CreateProvider(timeoutClient);
        var timedOut = await timeoutProvider.GenerateAsync(
            new AiProviderInput("Soup", 2, [], [], []),
            CancellationToken.None);
        Assert.Equal(AiProviderFailure.Unavailable, timedOut.Failure);

        using var serverErrorClient = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError))));
        var serverErrorProvider = CreateProvider(serverErrorClient);
        var serverError = await serverErrorProvider.GenerateAsync(
            new AiProviderInput("Soup", 2, [], [], []),
            CancellationToken.None);
        Assert.Equal(AiProviderFailure.Unavailable, serverError.Failure);
        Assert.False(serverError.DefinitelyNoCharge);

        using var rejectedClient = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest))));
        var rejectedProvider = CreateProvider(rejectedClient);
        var rejected = await rejectedProvider.GenerateAsync(
            new AiProviderInput("Soup", 2, [], [], []),
            CancellationToken.None);
        Assert.Equal(AiProviderFailure.Unavailable, rejected.Failure);
        Assert.True(rejected.DefinitelyNoCharge);

        using var networkErrorClient = new HttpClient(new StubHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("private provider error"))));
        var networkErrorProvider = CreateProvider(networkErrorClient);
        var networkError = await networkErrorProvider.GenerateAsync(
            new AiProviderInput("Soup", 2, [], [], []),
            CancellationToken.None);
        Assert.Equal(AiProviderFailure.Unavailable, networkError.Failure);
    }

    private static OpenAiRecipeProvider CreateProvider(HttpClient client) =>
        new(client, Options.Create(new AiRecipeOptions
        {
            Enabled = true,
            MonthlyBudgetUsd = 1,
            InputUsdPerMillionTokens = 1,
            OutputUsdPerMillionTokens = 1,
            OpenAI = { ApiKey = "unit-test-secret", Model = "gpt-4.1-mini" }
        }), new TestEnvironment());

    private static HttpResponseMessage JsonResponse(string content) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Kuechenfuchs.Api.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = ".";
        public string EnvironmentName { get; set; } = Environments.Development;
        public string WebRootPath { get; set; } = ".";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
