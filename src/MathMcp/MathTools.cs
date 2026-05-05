using System.ComponentModel;
using ModelContextProtocol.Server;

namespace MathMcp;

[McpServerToolType]
public static class MathTools
{
    [McpServerTool, Description("Returns a + b.")]
    public static double Add(double a, double b) => a + b;

    [McpServerTool, Description("Returns a - b.")]
    public static double Subtract(double a, double b) => a - b;

    [McpServerTool, Description("Returns a * b.")]
    public static double Multiply(double a, double b) => a * b;

    [McpServerTool, Description("Returns a / b. Throws if b is zero.")]
    public static double Divide(double a, double b) =>
        b == 0
            ? throw new ArgumentException("Division by zero", nameof(b))
            : a / b;
}
