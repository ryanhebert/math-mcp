using System.ComponentModel;
using ModelContextProtocol.Server;

namespace MathMcp;

[McpServerResourceType]
public static class MathResources
{
    [McpServerResource(Name = "Mathematical Constants", UriTemplate = "math://constants", MimeType = "text/plain"),
     Description("Common mathematical constants to high precision.")]
    public static string Constants() => """
        π (pi)        = 3.14159265358979323846
        e (Euler's #) = 2.71828182845904523536
        φ (golden)    = 1.61803398874989484820
        √2            = 1.41421356237309504880
        √3            = 1.73205080756887729353
        ln(2)         = 0.69314718055994530941
        γ (Euler-     = 0.57721566490153286061
           Mascheroni)
        """;

    [McpServerResource(Name = "Algebraic Identities", UriTemplate = "math://identities", MimeType = "text/plain"),
     Description("A short reference card of common algebraic and trigonometric identities.")]
    public static string Identities() => """
        Algebraic
          (a + b)²  = a² + 2ab + b²
          (a - b)²  = a² - 2ab + b²
          a² - b²   = (a + b)(a - b)
          a³ ± b³   = (a ± b)(a² ∓ ab + b²)

        Trigonometric
          sin²θ + cos²θ = 1
          sin(2θ)        = 2 sinθ cosθ
          cos(2θ)        = cos²θ − sin²θ
          tan(θ)         = sinθ / cosθ

        Famous
          e^(iπ) + 1 = 0       (Euler's identity)
          a² + b² = c²         (Pythagoras)
        """;

    [McpServerResource(Name = "First 25 Prime Numbers", UriTemplate = "math://primes", MimeType = "text/plain"),
     Description("The first twenty-five prime numbers, separated by spaces.")]
    public static string Primes() =>
        "2 3 5 7 11 13 17 19 23 29 31 37 41 43 47 53 59 61 67 71 73 79 83 89 97";
}
