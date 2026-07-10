# Guidance — C# Port (MVP)

This directory contains an idiomatic C# MVP port of the [Guidance](https://github.com/guidance-ai/guidance) library.

## Scope

This MVP targets **remote-model support** (OpenAI / Azure OpenAI) and provides:

| Feature | Status |
|---------|--------|
| Immutable `Model` with copy-on-write semantics | ✅ |
| Grammar AST (`Gen`, `Select`, `Json`, `Regex`, `Repeat`, …) | ✅ |
| Role blocks (`WithSystem` / `WithUser` / `WithAssistant`) | ✅ |
| Capture variables (`model["name"]`) | ✅ |
| `MockInterpreter` for offline unit-testing | ✅ |
| OpenAI Chat Completions adapter | ✅ |
| Local constrained decoding (llguidance binding) | ❌ future work |
| Jupyter / notebook visualisation | ❌ future work |

## Requirements

- .NET 8 SDK or later
- For OpenAI tests: a valid `OPENAI_API_KEY` environment variable

## Quick start

```bash
cd csharp
dotnet build
dotnet test
```

### Basic usage

```csharp
using Guidance.Grammar;
using Guidance.Models;

// --- Offline (Mock) ---
var lm = MockModel.Create(mockResponse: "Paris");

var result = lm
    .WithSystem("You are a geography expert.")
    .WithUser("What is the capital of France?")
    .WithAssistant(m => m + GrammarFunctions.Gen("capital", maxTokens: 10));

Console.WriteLine(result["capital"]); // Paris

// --- OpenAI ---
// var lm = OpenAIModel.Create("gpt-4o", apiKey: Environment.GetEnvironmentVariable("OPENAI_API_KEY")!);
// var result = lm
//     .WithSystem("You are a helpful assistant.")
//     .WithUser("Name a color.")
//     .WithAssistant(m => m + GrammarFunctions.Select(["red", "green", "blue"], name: "color"));
// Console.WriteLine(result["color"]);
```

## Architecture

```
Guidance/
├── Grammar/
│   ├── GrammarNode.cs        ← Abstract base + all concrete node types
│   └── GrammarFunctions.cs   ← Gen(), Select(), Json(), Regex(), …
├── Models/
│   ├── CaptureValue.cs       ← Captured variable value + log-prob
│   ├── IInterpreter.cs       ← Interpreter contract
│   ├── Model.cs              ← Immutable model (copy-on-write)
│   └── MockInterpreter.cs    ← Offline stub for unit tests
└── Adapters/OpenAI/
    ├── OpenAIInterpreter.cs  ← HTTP adapter calling /v1/chat/completions
    └── OpenAIModel.cs        ← Convenience factory
```

### Correspondence with the Python library

| Python | C# |
|--------|----|
| `guidance/_ast.py` — `GrammarNode`, `LiteralNode`, … | `Grammar/GrammarNode.cs` |
| `guidance/_grammar.py` — `gen()`, `select()`, … | `Grammar/GrammarFunctions.cs` |
| `guidance/models/_base/_model.py` — `Model` | `Models/Model.cs` |
| `guidance/models/_base/_interpreter.py` — `Interpreter` | `Models/IInterpreter.cs` |
| `guidance/models/_mock.py` — `Mock` | `Models/MockInterpreter.cs` |
| `guidance/models/_openai_base.py` — `BaseOpenAIInterpreter` | `Adapters/OpenAI/OpenAIInterpreter.cs` |
| `guidance/library/_role.py` — `system()`, `user()`, `assistant()` | `Model.WithSystem/User/Assistant` |

## Design decisions

### Immutability
Like the Python library, `Model` is immutable.  `model += x` / `model.Append(x)` always
returns a **new** `Model` instance; the original is unchanged.  This allows safe "branching":

```csharp
var base = lm.WithSystem("You are helpful").WithUser("Hello");
var branch1 = base.WithAssistant(m => m + Gen("a", maxTokens: 10));
var branch2 = base.WithAssistant(m => m + Gen("b", maxTokens: 50));
```

### Role blocks
Python uses `with system():` context managers backed by `ContextVar`.  C# uses a fluent
callback API instead, which avoids `AsyncLocal` side-effects and is more readable:

```csharp
lm = lm.WithSystem("prompt")
       .WithUser("question")
       .WithAssistant(m => m + Gen("answer"));
```

### Async / blocking
The MVP uses synchronous (`GetAwaiter().GetResult()`) blocking for OpenAI HTTP calls.
This mirrors the Python library's behaviour (which is also synchronous / thread-blocking).
A future `AppendAsync` / `IAsyncInterpreter` interface can replace this.

## Porting gaps (future work)

1. **Local constrained decoding** — requires either a native .NET binding to `llguidance`
   (Rust crate) or a managed re-implementation of the token-mask parser loop.
2. **Transformers / llama.cpp / ONNX backends** — each needs its own .NET integration.
3. **Async-first API** — `IAsyncInterpreter` + `Task<Model>` returning methods.
4. **Pydantic-style schema generation** — use `System.Text.Json.Schema` (available in
   .NET 9) or a third-party library.
5. **Notebook / Jupyter visualisation** — the Python `stitch` widget has no .NET
   equivalent; a console or web-based renderer would need to be built from scratch.
