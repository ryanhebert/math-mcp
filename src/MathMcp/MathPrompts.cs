using System.ComponentModel;
using ModelContextProtocol.Server;

namespace MathMcp;

[McpServerPromptType]
public static class MathPrompts
{
    [McpServerPrompt(Name = "solve-expression"),
     Description("Step-by-step walkthrough using the math tools to evaluate an arithmetic expression.")]
    public static string SolveExpression(
        [Description("The arithmetic expression to evaluate, e.g. \"(3 + 4) * 2\"")] string expression)
        => $"""
            Evaluate the following expression step by step using the available math tools
            (add, subtract, multiply, divide). Show each intermediate calculation, then
            state the final result.

            Expression: {expression}
            """;

    [McpServerPrompt(Name = "compare-numbers"),
     Description("Compares two numbers, computes the difference via the subtract tool, and explains which is larger.")]
    public static string CompareNumbers(
        [Description("The first number")] double a,
        [Description("The second number")] double b)
        => $"""
            Compare these two numbers: {a} and {b}.

            1. Use the subtract tool to compute the absolute difference.
            2. State which number is larger and by how much.
            3. If they are equal, say so.
            """;
}
