using System.Text.Json;
using Deckhand.Agent.Steps;

namespace Deckhand.Agent.Tests.Steps;

public class StepParserTests
{
    [Fact]
    public void Parses_a_tool_step()
    {
        Assert.True(StepParser.TryParse("""{"thought":"check","action":"net_ping","args":{"host":"1.1.1.1","count":2}}""", out var step, out _));

        Assert.Equal("check", step!.Thought);
        Assert.Equal("net_ping", step.Action);
        Assert.Equal("1.1.1.1", ((JsonElement)step.Args["host"]!).GetString());
        Assert.Equal(2, ((JsonElement)step.Args["count"]!).GetInt32());
        Assert.False(step.IsFinal);
    }

    [Fact]
    public void Parses_a_final_answer()
    {
        Assert.True(StepParser.TryParse("""{"thought":"done","action":"final_answer","answer":"Your gateway is down."}""", out var step, out _));

        Assert.True(step!.IsFinal);
        Assert.Equal("Your gateway is down.", step.Answer);
        Assert.Empty(step.Args);
    }

    [Theory]
    [InlineData("```json\n{\"thought\":\"t\",\"action\":\"system_get_info\",\"args\":{}}\n```")]
    [InlineData("Here is my step:\n{\"thought\":\"t\",\"action\":\"system_get_info\",\"args\":{}}\nThanks.")]
    [InlineData("  {\"thought\":\"a } in text\",\"action\":\"system_get_info\",\"args\":{}}  ")]
    public void Extracts_the_json_object_from_surrounding_text(string text)
    {
        Assert.True(StepParser.TryParse(text, out var step, out var error), error);
        Assert.Equal("system_get_info", step!.Action);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"thought\":\"t\",\"action\":")]
    [InlineData("{\"thought\":\"t\"}")]
    [InlineData("{\"thought\":\"t\",\"action\":\"final_answer\"}")]
    [InlineData("{\"thought\":\"t\",\"action\":\"net_ping\",\"args\":\"host=1.1.1.1\"}")]
    public void Rejects_invalid_steps_with_a_reason(string text)
    {
        Assert.False(StepParser.TryParse(text, out var step, out var error));
        Assert.Null(step);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Missing_thought_and_args_default_to_empty()
    {
        Assert.True(StepParser.TryParse("""{"action":"system_get_info"}""", out var step, out _));

        Assert.Equal("", step!.Thought);
        Assert.Empty(step.Args);
    }
}
