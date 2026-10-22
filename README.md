# Kurippu

Kurippu is a C# / Avalonia desktop video editor. The maintained application
lives in [`avalonia/`](avalonia/README.md); the former Electron implementation
and its Node build pipeline have been removed.

Build and test with .NET 10:

```sh
dotnet test avalonia/Kurippu.slnx
dotnet run --project avalonia/Kurippu.Desktop
```

FFmpeg and libmpv are staged for packaged builds; see the Avalonia README for
platform-specific packaging and validation. The project is licensed under
GPL-3.0-only.
