# Guidance — C# Port

This directory contains an idiomatic C# port of the [Guidance](https://github.com/guidance-ai/guidance) library.

## Scope

This port targets **remote-model support** (OpenAI / Azure OpenAI) and provides:

| Feature | Status |
|---------|--------|
| Immutable `Model` with copy-on-write semantics | ✅ |
| Full grammar AST (`Gen`, `Select`, `Json`, `Regex`, `Repeat`, `Substring`, `Subgrammar`, `SpecialToken`, …) | ✅ |
| Role blocks (`WithSystem` / `WithUser` / `WithAssistant`) | ✅ |
| Capture variables (`model["name"]`) | ✅ |
| `MockInterpreter` for offline unit-testing | ✅ |
| OpenAI Chat Completions adapter (sync + async) | ✅ |
| `IAsyncInterpreter` — async-first API (`AppendAsync`, `WithAssistantAsync`, …) | ✅ |
| `Json<T>()` / `Json(Type)` schema generation via `System.Text.Json` | ✅ |
| `TokenLimit`, `WithTemperature`, `Capture`, `QuoteRegex` helpers | ✅ |
| `Substring`, `Subgrammar`, `SpecialToken`, `RuleRefNode`, `LarkNode` grammar nodes | ✅ |
| Local constrained decoding (llguidance binding) | ❌ future work |
| Jupyter / notebook visualisation | ❌ future work |

## Requirements

- .NET 9 SDK or later
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

## Usage

### Creating a model

#### Offline / unit-testing — `MockModel`

`MockModel.Create` returns a `Model` that never calls a real LLM.  Every
generation rule returns the string passed as `mockResponse`.

```csharp
using Guidance.Models;

// Model that always produces "hello"
var lm = MockModel.Create(mockResponse: "hello");

// Empty mock (produces "" for every generation rule)
var lm = MockModel.Create();
```

You can also construct a `MockInterpreter` directly and wrap it yourself:

```csharp
var interpreter = new MockInterpreter("hello world");
var lm = Model.From(interpreter);
```

#### OpenAI / Azure OpenAI — `OpenAIModel`

```csharp
using Guidance.Adapters.OpenAI;

// Standard OpenAI
var lm = OpenAIModel.Create("gpt-4o", apiKey: "sk-...");

// Azure OpenAI — point baseUrl at your deployment endpoint
var lm = OpenAIModel.Create(
    "gpt-4o",
    apiKey: Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY"),
    baseUrl: "https://<resource>.openai.azure.com/openai/deployments/<deployment>");
```

#### OpenAI-compatible servers (Ollama, LM Studio, vLLM)

Pass `null` (or omit) `apiKey` and set `baseUrl` to the server's base URL.
The SDK sends a placeholder bearer token that locally-running servers ignore.

```csharp
// Ollama
var lm = OpenAIModel.Create("llama3", baseUrl: "http://localhost:11434/v1");

// LM Studio (default port 1234)
var lm = OpenAIModel.Create("local-model", baseUrl: "http://localhost:1234/v1");

// vLLM
var lm = OpenAIModel.Create("mistral-7b", baseUrl: "http://localhost:8000/v1");
```

---

### Grammar functions

All grammar functions are static methods on `GrammarFunctions` in the
`Guidance.Grammar` namespace.

#### `Gen` — free-form text generation

```csharp
using Guidance.Grammar;

// Unconstrained generation, captured as "answer"
GrammarFunctions.Gen("answer")

// Constrained by a regex pattern
GrammarFunctions.Gen("digits", regex: @"\d+")

// Stop generation at a literal string (exclusive — stop text is not captured)
GrammarFunctions.Gen("line", stop: "\n")

// Stop generation at a regex match
GrammarFunctions.Gen("sentence", stopRegex: @"[.!?]")

// Append a literal suffix after generation (not captured)
GrammarFunctions.Gen("word", suffix: "\n")

// Control sampling temperature (0 = deterministic, 1 = default)
GrammarFunctions.Gen("creative", temperature: 0.8f)

// Limit the number of output tokens
GrammarFunctions.Gen("snippet", maxTokens: 50)

// Append to a list instead of overwriting (see "List-append captures" below)
GrammarFunctions.Gen("items", listAppend: true)

// Combine parameters freely
GrammarFunctions.Gen(
    name: "answer",
    regex: @"\w+",
    stop: ".",
    maxTokens: 20,
    temperature: 0.5f)
```

> `stop` and `stopRegex` are mutually exclusive; passing both throws
> `ArgumentException`.

#### `Select` — constrained to a set of options

```csharp
// Choose from string literals — model output is snapped to the best match
GrammarFunctions.Select(["red", "green", "blue"], name: "color")

// Without a capture name
GrammarFunctions.Select(["yes", "no"])

// Append to a list
GrammarFunctions.Select(["A", "B", "C"], name: "choices", listAppend: true)

// Choose from grammar nodes (e.g. mix literals with regex patterns)
GrammarFunctions.Select(
    new GrammarNode[]
    {
        new LiteralNode("yes"),
        new LiteralNode("no"),
        new RegexNode(@"\d+"),
    },
    name: "answer")
```

#### `Json` — structured JSON output

```csharp
// Any valid JSON object (uses json_object response format on OpenAI)
GrammarFunctions.Json("data")

// JSON constrained to a schema (uses json_schema response format on OpenAI)
const string schema = """
    {
      "type": "object",
      "properties": {
        "name": { "type": "string" },
        "age":  { "type": "integer" }
      },
      "required": ["name", "age"]
    }
    """;

GrammarFunctions.Json("person", schemaJson: schema)

// With token / temperature limits
GrammarFunctions.Json("result", maxTokens: 512, temperature: 0.0f)
```

#### `String` / `Regex` — primitive nodes

```csharp
// Exact string literal
GrammarFunctions.String("Hello, world!")   // → LiteralNode

// Regular expression constraint
GrammarFunctions.Regex(@"\d{4}-\d{2}-\d{2}")  // → RegexNode
```

These are thin wrappers around the `LiteralNode` and `RegexNode` constructors
and are useful when composing nodes with the `+` operator.

#### Repeat helpers

```csharp
var digit = GrammarFunctions.Regex(@"\d");

// Repeat between min and max times (max = null means unbounded)
GrammarFunctions.Repeat(digit, min: 2, max: 5)

// Zero or more repetitions
GrammarFunctions.ZeroOrMore(digit)

// One or more repetitions
GrammarFunctions.OneOrMore(digit)

// Zero or one repetition (optional)
GrammarFunctions.Optional(digit)

// Exactly N repetitions
GrammarFunctions.ExactlyNRepeats(digit, nRepeats: 4)

// At most N repetitions (0 to N)
GrammarFunctions.AtMostNRepeats(digit, nRepeats: 4)

// Alias for Repeat with named arguments
GrammarFunctions.Sequence(digit, minLength: 1, maxLength: 5)
```

#### `Substring` — generate a subsequence of a known string

```csharp
// Allow the model to output any contiguous word-level sub-sequence of the target.
GrammarFunctions.Substring("quick brown fox", name: "excerpt")

// Character-level chunking
GrammarFunctions.Substring("abc", chunk: "character", name: "chars")
```

#### `Subgrammar` — embed a grammar as a self-contained unit

```csharp
var inner = GrammarFunctions.Gen("inner", regex: @"\d+");

// Wrap as a subgrammar (no capture)
GrammarFunctions.Subgrammar(inner)

// With capture, token limit and temperature
GrammarFunctions.Subgrammar(inner, name: "result", maxTokens: 32, temperature: 0.5f)

// With skip-whitespace between tokens
GrammarFunctions.Subgrammar(inner, skipRegex: @"\s+")
```

#### `SpecialToken` — insert a special vocabulary token

```csharp
// Parses "<|endoftext|>" and wraps it in a SpecialTokenNode.
GrammarFunctions.SpecialToken("<|endoftext|>")
GrammarFunctions.SpecialToken("<|im_start|>")
```

#### `TokenLimit` / `WithTemperature` / `Capture` — modifiers

```csharp
var gen = GrammarFunctions.Gen("x");

// Apply a token budget to an existing rule
GrammarFunctions.TokenLimit(gen, 64)

// Set / override the sampling temperature
GrammarFunctions.WithTemperature(gen, 0.7f)

// Attach a capture name to any grammar node
GrammarFunctions.Capture(new RegexNode(@"\d+"), name: "digits")
```

#### `QuoteRegex` — escape special regex characters

```csharp
// Escape "2 + 2" so it can be used literally inside a regex pattern.
string escaped = GrammarFunctions.QuoteRegex("2 + 2");  // "2\ \+\ 2"
GrammarFunctions.Gen(regex: escaped + "=4")
```

---

### Model operations

All operations on `Model` return a **new** `Model` instance — the original
is never modified.

#### Appending content with `+`

```csharp
// Append a string literal
lm = lm + "Hello, ";

// Append a grammar node
lm = lm + GrammarFunctions.Gen("name");

// Chain with method syntax
lm = lm.Append("Hello, ").Append(GrammarFunctions.Gen("name"));
```

#### Reading captured values

```csharp
// Index operator — throws KeyNotFoundException if not present
string answer = result["answer"];

// Get with a default (returns null when not captured)
string? answer = result.Get("answer");
string answer = result.Get("answer", defaultValue: "unknown")!;

// Check existence before reading
if (result.Contains("answer"))
    Console.WriteLine(result["answer"]);

// Iterate all captures
foreach (var (name, capture) in result.Captures)
    Console.WriteLine($"{name} = {capture.Value}");
```

#### `Model.Text` — the full conversation text

```csharp
Console.WriteLine(result.Text);
// <|system|>You are helpful.<|/system|>
// <|user|>What is 2 + 2?<|/user|>
// <|assistant|>4<|/assistant|>
```

---

### Role blocks

Role blocks group content into chat turns.  Each `With*` method is a
copy-on-write operation that returns a new `Model`.

```csharp
// Static text content
lm = lm.WithSystem("You are a helpful assistant.");
lm = lm.WithUser("What is 2 + 2?");
lm = lm.WithAssistant("The answer is ");

// Dynamic content via callback — the callback receives the model
// with the role already open and must return the updated model
lm = lm.WithAssistant(m => m + GrammarFunctions.Gen("answer", maxTokens: 10));

// Custom role name (use WithRole for non-standard roles)
lm = lm.WithRole("tool", m => m + "{\"result\": 4}");
```

Full conversation example:

```csharp
var lm = MockModel.Create(mockResponse: "4");

var result = lm
    .WithSystem("You are a calculator.")
    .WithUser("What is 2 + 2?")
    .WithAssistant(m => m + GrammarFunctions.Gen("answer", regex: @"\d+"));

Console.WriteLine(result["answer"]); // 4
```

#### Async role blocks

All role helpers have `*Async` counterparts that accept
`Func<Model, CancellationToken, Task<Model>>` callbacks:

```csharp
var lm = OpenAIModel.Create("gpt-4o", apiKey: "sk-...");

var result = await (await (await lm
    .WithSystemAsync("You are a helpful assistant."))
    .WithUserAsync("What is the capital of France?"))
    .WithAssistantAsync(async (m, ct) =>
        await m.AppendAsync(GrammarFunctions.Gen("capital", maxTokens: 5), ct));

Console.WriteLine(result["capital"]);
```

---

### JSON schema generation

`GrammarFunctions.Json<T>()` derives a JSON schema from a .NET type using
`System.Text.Json.Schema.JsonSchemaExporter` and passes it to the model as
a structured-output constraint.

```csharp
public sealed record Person(string Name, int Age);

// Using a generic type parameter
var rule = GrammarFunctions.Json<Person>(name: "person");

// Using a Type object
var rule2 = GrammarFunctions.Json(typeof(Person), name: "person");

// With custom JsonSerializerOptions
var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
var rule3 = GrammarFunctions.Json<Person>(name: "p", options: opts);

// In a model pipeline
var lm = OpenAIModel.Create("gpt-4o", apiKey: "sk-...");
var result = lm
    .WithUser("Describe a person.")
    .WithAssistant(m => m + GrammarFunctions.Json<Person>("person"));

var json = result["person"];
var person = JsonSerializer.Deserialize<Person>(json)!;
Console.WriteLine($"{person.Name}, age {person.Age}");
```

---

### List-append captures

When `listAppend: true` is passed to `Gen` or `Select`, each generation is
appended to a list rather than overwriting the previous value.

```csharp
var lm = MockModel.Create(mockResponse: "item");
var rule = GrammarFunctions.Gen("items", listAppend: true);

// Apply the same rule multiple times
var result = (lm + rule) + rule;

CaptureValue capture = result.Captures["items"];
Console.WriteLine(capture.Values.Count); // 2
Console.WriteLine(capture.Value);        // "item" (most recent)
Console.WriteLine(capture.Values[0]);    // "item"
Console.WriteLine(capture.Values[1]);    // "item"
```

---

### Branching (copy-on-write semantics)

Because every operation returns a new `Model`, you can branch from any point
without affecting earlier states:

```csharp
var base_ = lm
    .WithSystem("You are helpful.")
    .WithUser("Name a primary color.");

// Two independent completions from the same prompt
var branch1 = base_.WithAssistant(m => m + GrammarFunctions.Gen("color", maxTokens: 5));
var branch2 = base_.WithAssistant(m => m + GrammarFunctions.Gen("color", maxTokens: 20));

// base_ is completely unchanged
Console.WriteLine(base_.Text);           // contains system + user turns only
Console.WriteLine(branch1["color"]);
Console.WriteLine(branch2["color"]);
```

## Architecture

```
Guidance/
├── Grammar/
│   ├── GrammarNode.cs        ← Abstract base + all concrete node types
│   └── GrammarFunctions.cs   ← Gen(), Select(), Json(), Regex(), Substring(), …
├── Models/
│   ├── CaptureValue.cs       ← Captured variable value + log-prob
│   ├── IInterpreter.cs       ← Synchronous interpreter contract
│   ├── IAsyncInterpreter.cs  ← Async interpreter contract (extends IInterpreter)
│   ├── Model.cs              ← Immutable model (copy-on-write, sync + async)
│   └── MockInterpreter.cs    ← Offline stub for unit tests (implements IAsyncInterpreter)
└── Adapters/OpenAI/
    ├── OpenAIInterpreter.cs  ← HTTP adapter calling /v1/chat/completions (sync + async)
    └── OpenAIModel.cs        ← Convenience factory
```

### Correspondence with the Python library

| Python | C# |
|--------|----|
| `guidance/_ast.py` — `GrammarNode`, `LiteralNode`, `SubstringNode`, `SubgrammarNode`, … | `Grammar/GrammarNode.cs` |
| `guidance/_grammar.py` — `gen()`, `select()`, `token_limit()`, `with_temperature()`, … | `Grammar/GrammarFunctions.cs` |
| `guidance/library/_gen.py` — `gen()` with `save_stop_text`, `lazy` | `GrammarFunctions.Gen()` |
| `guidance/library/_json.py` — `json()` with Pydantic schema | `GrammarFunctions.Json<T>()` |
| `guidance/library/_sequences.py` — `exactly_n_repeats()`, `at_most_n_repeats()`, `sequence()` | `GrammarFunctions.ExactlyNRepeats/AtMostNRepeats/Sequence` |
| `guidance/library/_substring.py` — `substring()` | `GrammarFunctions.Substring()` |
| `guidance/models/_base/_model.py` — `Model` | `Models/Model.cs` |
| `guidance/models/_base/_interpreter.py` — `Interpreter` | `Models/IInterpreter.cs` + `IAsyncInterpreter.cs` |
| `guidance/models/_mock.py` — `Mock` | `Models/MockInterpreter.cs` |
| `guidance/models/_openai_base.py` — `BaseOpenAIInterpreter` | `Adapters/OpenAI/OpenAIInterpreter.cs` |
| `guidance/library/_role.py` — `system()`, `user()`, `assistant()` | `Model.WithSystem/User/Assistant` (sync + async) |

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
// Synchronous
lm = lm.WithSystem("prompt")
       .WithUser("question")
       .WithAssistant(m => m + Gen("answer"));

// Asynchronous
lm = await (await (await lm
    .WithSystemAsync("prompt"))
    .WithUserAsync("question"))
    .WithAssistantAsync(async (m, ct) => await m.AppendAsync(Gen("answer"), ct));
```

### Async API
`IAsyncInterpreter` extends `IInterpreter` with `AppendLiteralAsync` and `ApplyRuleAsync`.
`Model` exposes `AppendAsync`, `WithSystemAsync`, `WithUserAsync`, `WithAssistantAsync`, and
`WithRoleAsync`.  Both `MockInterpreter` and `OpenAIInterpreter` implement `IAsyncInterpreter`;
the OpenAI adapter uses `CompleteChatAsync` on the async path to avoid blocking threads.

### JSON schema generation
`GrammarFunctions.Json<T>()` and `GrammarFunctions.Json(Type)` use
`System.Text.Json.Schema.JsonSchemaExporter` (available since .NET 9) to derive a JSON schema
from any `System.Text.Json`-serialisable .NET type, mirroring the Pydantic-based approach in
the Python library.

## Remaining gaps (future work)

1. **Local constrained decoding** — requires either a native .NET binding to `llguidance`
   (Rust crate) or a managed re-implementation of the token-mask parser loop.
2. **Transformers / llama.cpp / ONNX backends** — each needs its own .NET integration.
3. **Notebook / Jupyter visualisation** — the Python `stitch` widget has no .NET
   equivalent; a console or web-based renderer would need to be built from scratch.
