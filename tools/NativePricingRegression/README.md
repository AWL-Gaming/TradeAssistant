This suite executes the compiled, woven TradeAssistant calculator against native Eco 1099 classes. It creates in-memory store offers and crafting fixtures in its own process. It does not connect to a server or read or write saves, configuration, databases, or real stores.

Use an immutable copy of the matching Eco runtime DLLs and their dependencies. The paths can be supplied through `ECO_NATIVE_DIR` and, if dependencies are stored separately, `ECO_NATIVE_DEPENDENCY_DIR`.

```powershell
dotnet run --project tools\NativePricingRegression\NativePricingRegression.csproj -c Release
```

Checks cover tagged and concrete offers, frozen prices, missing orders, actual price-change notifications, shared-order pricing independent of item order, incomplete or invalid costs, overlapping caches, and native labor, resource, yield and ingredient-refund behavior. Full server command execution and live shop acceptance still require a test server.

For CI, configure BUILD_RUNNER_LABELS as a JSON array selecting a Windows runner. Supply the runtime directories through the ECO_NATIVE_DIR and ECO_NATIVE_DEPENDENCY_DIR repository secrets.
