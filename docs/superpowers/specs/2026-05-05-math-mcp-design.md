# math-mcp Design

## Purpose

A minimal Model Context Protocol (MCP) server exposing the four basic arithmetic operations as tools: `add`, `subtract`, `multiply`, and `divide`. Intended as a small, working reference for an MCP server in Python.

## Stack

- **Language:** Python (3.10+)
- **Framework:** [`mcp`](https://pypi.org/project/mcp/) Python SDK, using `FastMCP` (high-level decorator API)
- **Transport:** stdio (default — compatible with Claude Desktop, Claude Code, and other MCP clients)
- **Dependency manager:** `uv` recommended; `pip` also works

## File layout

```
math-mcp/
├── server.py        # FastMCP server with four @mcp.tool() functions
├── pyproject.toml   # project metadata; depends on mcp[cli]
└── README.md        # how to install, run, and connect a client
```

## Tools

Each tool accepts two `float` arguments and returns a `float`. The MCP SDK derives the input schema from Python type hints, and the docstring becomes the tool description.

| Tool       | Signature                          | Behavior                                |
|------------|------------------------------------|-----------------------------------------|
| `add`      | `add(a: float, b: float) -> float` | Returns `a + b`                         |
| `subtract` | `subtract(a: float, b: float) -> float` | Returns `a - b`                    |
| `multiply` | `multiply(a: float, b: float) -> float` | Returns `a * b`                    |
| `divide`   | `divide(a: float, b: float) -> float`   | Returns `a / b`; raises `ValueError("Division by zero")` when `b == 0` |

## Error handling

Only `divide` has a documented failure mode. When `b == 0`, the function raises `ValueError`. FastMCP converts the exception into an MCP tool error response that the client surfaces to the user. No other validation is performed; type coercion is handled by the SDK based on the function signature.

## Running

- **Direct:** `uv run server.py` or `python server.py`
- **Client config example (Claude Desktop / Claude Code):**
  ```json
  {
    "mcpServers": {
      "math": {
        "command": "uv",
        "args": ["run", "/absolute/path/to/server.py"]
      }
    }
  }
  ```

## Out of scope

- Additional operators (modulo, power, roots, etc.)
- Vector or matrix math
- Arbitrary-precision arithmetic
- HTTP/SSE transport
- Authentication
- Persistent state or history
