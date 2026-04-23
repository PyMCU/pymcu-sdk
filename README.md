# pymcu-plugin-sdk

> Shared base classes and plugin protocol for PyMCU backend and toolchain packages.  
> Part of the [PyMCU](https://github.com/begeistert/pymcu) project.

`pymcu-sdk` is the **stable API surface** that all PyMCU backend and toolchain plugins depend on. It ships two parallel components that plugin authors reference in their own packages:

| Component | Artifact | Language | Description |
|---|---|---|---|
| `pymcu-plugin-sdk` | Python wheel | Python ≥ 3.11 | ABCs and entry-point protocol for backend & toolchain plugins |
| `PyMCU.Backend.SDK` | NuGet library | .NET 10 (AOT) | C# abstract types for codegen backend binaries bundled in backend plugins |

---

## Repository layout

```
pymcu-sdk/
├── src/
│   ├── python/
│   │   └── pymcu/
│   │       ├── backend/sdk/      # BackendPlugin ABC + LicenseStatus
│   │       └── toolchain/sdk/    # ToolchainPlugin, ExternalToolchain, HardwareProgrammer ABCs
│   └── csharp/
│       └── PyMCU.Backend.SDK/    # CodeGen, IBackendProvider, IR types, stack allocator
├── pyproject.toml                # Python build config (Hatchling)
├── PyMCU.SDK.slnx                # .NET solution (Rider / dotnet CLI)
└── .github/
    └── workflows/
        ├── build-python.yml      # CI: build Python wheel
        └── build-csharp.yml      # CI: build C# NuGet package
```

---

## Requirements

### Python component
- Python ≥ 3.11
- [Hatch](https://hatch.pypa.io/) ≥ 1.25

```bash
pip install hatch
```

### C# component
- [.NET SDK](https://dotnet.microsoft.com/download) 10.0

---

## Building locally

### Python wheel

```bash
hatch build
# Artifacts are written to dist/
#   dist/pymcu_plugin_sdk-<version>-py3-none-any.whl
#   dist/pymcu_plugin_sdk-<version>.tar.gz
```

### C# NuGet package

```bash
dotnet build src/csharp/PyMCU.Backend.SDK.csproj -c Release
dotnet pack  src/csharp/PyMCU.Backend.SDK.csproj -c Release --no-build -o artifacts/
# Artifact: artifacts/PyMCU.Backend.SDK.<version>.nupkg
```

---

## Plugin development

### Backend plugin (Python)

Implement `BackendPlugin` and register it under the `pymcu.backends` entry-point group:

```toml
# pyproject.toml of your backend package
[project.entry-points."pymcu.backends"]
avr = "pymcu.backend.avr:AvrBackendPlugin"
```

```python
from pymcu.backend.sdk import BackendPlugin, LicenseStatus
from pathlib import Path

class AvrBackendPlugin(BackendPlugin):
    family = "avr"
    description = "AVR backend for PyMCU"
    version = "1.0.0"
    supported_arches = ["atmega", "attiny", "at90"]

    @classmethod
    def get_backend_binary(cls) -> Path:
        return Path(__file__).parent / "bin" / "pymcuc-avr"
```

### Toolchain plugin (Python)

Implement `ToolchainPlugin` and register it under `pymcu.toolchains`:

```toml
[project.entry-points."pymcu.toolchains"]
avr = "pymcu.toolchain.avr:AvrToolchainPlugin"
```

```python
from pymcu.toolchain.sdk import ToolchainPlugin, ExternalToolchain
from rich.console import Console

class AvrToolchainPlugin(ToolchainPlugin):
    family = "avr"
    description = "AVR-GCC toolchain for PyMCU"
    version = "14.1.0"
    default_chip = "atmega328p"

    @classmethod
    def supports(cls, chip: str) -> bool:
        return chip.lower().startswith(("atmega", "attiny", "at90"))

    @classmethod
    def get_toolchain(cls, console: Console, chip: str) -> ExternalToolchain:
        ...
```

### Backend codegen binary (C#)

Reference the NuGet package in your `.csproj` and subclass `CodeGen`:

```xml
<PackageReference Include="PyMCU.Backend.SDK" Version="1.0.0-beta1" />
```

```csharp
using PyMCU.Backend;
using PyMCU.IR;

public sealed class AvrCodeGen : CodeGen
{
    public override void Compile(ProgramIR program, TextWriter output) { ... }
    public override void EmitContextSave()    { ... }
    public override void EmitContextRestore() { ... }
    public override void EmitInterruptReturn(){ ... }
}
```

---

## CI / Build pipelines

Artifacts are built automatically on every push and pull-request to `main`:

| Workflow | Trigger | Artifact |
|---|---|---|
| `build-python` | push / PR → `main` | `pymcu_plugin_sdk-*.whl` + sdist |
| `build-csharp` | push / PR → `main` | `PyMCU.Backend.SDK.*.nupkg` |

Artifacts are uploaded to the GitHub Actions run summary and are **not** published to PyPI / NuGet.org.

---

## License

MIT — © 2026 Ivan Montiel Cardona and the PyMCU Project Authors.  
See [`SPDX-License-Identifier: MIT`](https://spdx.org/licenses/MIT.html).

> **Safety notice:** This software is not designed for use in hazardous environments requiring fail-safe performance (nuclear facilities, aircraft navigation, life-support systems, weapons systems, etc.).
