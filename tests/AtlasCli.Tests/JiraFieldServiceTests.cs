using System.Net;
using System.Text;
using System.Text.Json;
using AtlasCli.Services;
using Xunit;

namespace AtlasCli.Tests;

public sealed class JiraFieldServiceTests
{
    [Fact]
    public async Task ListAsync_PaginatesAndMapsFields()
    {
        using var client = CreateClient(
            """{"startAt":0,"maxResults":1,"total":2,"values":[{"id":"summary","key":"summary","name":"Summary","custom":false,"schema":{"type":"string"}}]}""",
            """{"startAt":1,"maxResults":1,"total":2,"values":[{"id":"customfield_10100","key":"customfield_10100","name":"Customer tier","custom":true,"schema":{"type":"option"}}]}""");

        var fields = await JiraFieldService.ListAsync(client, "customer", "custom");

        Assert.Collection(fields,
            field =>
            {
                Assert.Equal("summary", field.Id);
                Assert.False(field.Custom);
            },
            field =>
            {
                Assert.Equal("customfield_10100", field.Id);
                Assert.Equal("Customer tier", field.Name);
                Assert.True(field.Custom);
            });
        Assert.Contains("query=customer", client.Requests[0].Query);
        Assert.Contains("type=custom", client.Requests[0].Query);
        Assert.Contains("startAt=1", client.Requests[1].Query);
    }

    [Fact]
    public async Task GetCreateMetadataAsync_ResolvesIssueTypeNameAndReturnsFields()
    {
        using var client = CreateClient(
            """{"startAt":0,"total":2,"issueTypes":[{"id":"10001","name":"Story","subtask":false},{"id":"10002","name":"Task","subtask":false}]}""",
            """{"startAt":0,"total":1,"fields":[{"fieldId":"customfield_10100","key":"customfield_10100","name":"Customer tier","required":true,"hasDefaultValue":false,"schema":{"type":"option"},"allowedValues":[{"id":"1","value":"Gold"}],"operations":["set"]}]}""");

        var metadata = await JiraFieldService.GetCreateMetadataAsync(client, "DEMO", "story");

        Assert.Equal("10001", metadata.IssueType.Id);
        var field = Assert.Single(metadata.Fields);
        Assert.Equal("customfield_10100", field.Id);
        Assert.True(field.Required);
        Assert.Equal("option", field.SchemaType);
        Assert.Equal(new[] { "set" }, field.Operations);
        Assert.Contains("issuetypes/10001", client.Requests[1].AbsolutePath);
    }

    [Fact]
    public async Task GetEditMetadataAsync_MapsFieldDictionary()
    {
        using var client = CreateClient(
            """{"fields":{"summary":{"name":"Summary","required":true,"schema":{"type":"string"},"operations":["set"]},"customfield_10200":{"name":"Target date","required":false,"schema":{"type":"date","custom":"com.example:date"},"operations":["set"]}}}""");

        var metadata = await JiraFieldService.GetEditMetadataAsync(client, "DEMO-7");

        Assert.Equal(2, metadata.Count);
        Assert.Equal("summary", metadata.Single(field => field.Name == "Summary").Id);
        Assert.Equal("date", metadata.Single(field => field.Name == "Target date").SchemaType);
        Assert.EndsWith("/issue/DEMO-7/editmeta", client.Requests[0].AbsolutePath);
    }

    [Fact]
    public void ResolveField_AcceptsIdOrUniqueNameAndRejectsAmbiguousName()
    {
        var metadata = new[]
        {
            Field("customfield_1", "Customer tier"),
            Field("customfield_2", "Duplicate"),
            Field("customfield_3", "Duplicate")
        };

        Assert.Equal("customfield_1", JiraFieldService.ResolveField(metadata, "Customer tier").Id);
        Assert.Equal("customfield_1", JiraFieldService.ResolveField(metadata, "CUSTOMFIELD_1").Id);
        var error = Assert.Throws<InvalidOperationException>(() =>
            JiraFieldService.ResolveField(metadata, "Duplicate"));
        Assert.Contains("ambiguous", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyAdditionalFields_MapsOptionNameToAllowedValueObject()
    {
        using var values = JsonDocument.Parse("""[{"id":"1","value":"Gold"},{"id":"2","value":"Silver"}]""");
        var metadata = new[]
        {
            Field("customfield_1", "Customer tier", "option", allowedValues: values.RootElement.Clone())
        };
        var additional = JiraFieldService.ParseAdditionalFields("""{"Customer tier":"gold"}""");
        var target = new Dictionary<string, object?>();

        JiraFieldService.ApplyAdditionalFields(target, additional, metadata);

        var normalized = Assert.IsType<JsonElement>(target["customfield_1"]);
        Assert.Equal("1", normalized.GetProperty("id").GetString());
        Assert.Equal("Gold", normalized.GetProperty("value").GetString());
    }

    [Fact]
    public void NormalizeValue_ConvertsPlainTextareaToAdf()
    {
        var value = JsonSerializer.SerializeToElement("Some details");
        var field = Field("customfield_1", "Details", "string", "com.atlassian.jira.plugin.system.customfieldtypes:textarea");

        var result = JiraFieldService.NormalizeValue(value, field);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));

        Assert.Equal("doc", json.RootElement.GetProperty("type").GetString());
        Assert.Equal("Some details", json.RootElement
            .GetProperty("content")[0]
            .GetProperty("content")[0]
            .GetProperty("text").GetString());
    }

    [Fact]
    public void ValidateRequiredFields_ReportsMissingContextFields()
    {
        var metadata = new[]
        {
            Field("project", "Project", required: true),
            Field("issuetype", "Issue Type", required: true),
            Field("summary", "Summary", required: true),
            Field("customfield_1", "Customer tier", required: true)
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            JiraFieldService.ValidateRequiredFields(metadata, new[] { "summary" }));

        Assert.Contains("customfield_1 (Customer tier)", error.Message);
        Assert.DoesNotContain("project", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("issuetype", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseAdditionalFields_RequiresJsonObject()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            JiraFieldService.ParseAdditionalFields("[]"));

        Assert.Contains("JSON object", error.Message);
    }

    private static TestHttpClient CreateClient(params string[] responses)
    {
        var handler = new QueueMessageHandler(responses);
        return new TestHttpClient(handler);
    }

    private static JiraFieldMetadata Field(
        string id,
        string name,
        string schemaType = "string",
        string schemaCustom = "",
        JsonElement? allowedValues = null,
        bool required = false)
    {
        using var schema = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            type = schemaType,
            custom = schemaCustom
        }));
        return new JiraFieldMetadata(
            id,
            id,
            name,
            required,
            false,
            schema.RootElement.Clone(),
            allowedValues,
            null,
            new[] { "set" });
    }

    private sealed class TestHttpClient : HttpClient
    {
        private readonly QueueMessageHandler _handler;

        public TestHttpClient(QueueMessageHandler handler) : base(handler)
        {
            _handler = handler;
            BaseAddress = new Uri("https://example.atlassian.net/rest/api/3/");
        }

        public IReadOnlyList<Uri> Requests => _handler.Requests;
    }

    private sealed class QueueMessageHandler(IEnumerable<string> responses) : HttpMessageHandler
    {
        private readonly Queue<string> _responses = new(responses);
        public List<Uri> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            if (_responses.Count == 0)
                throw new InvalidOperationException("No test response was queued for this request.");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responses.Dequeue(), Encoding.UTF8, "application/json")
            });
        }
    }
}
