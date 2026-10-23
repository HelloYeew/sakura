# Sakura Framework Template

A project template for create app/game using Sakura Framework

```bash
dotnet new install Sakura.Framework.Templates
dotnet new sakura -n MyAwesomeGame
```

To make an Android project as well (it needs the .NET Android workload, `dotnet workload install android`):

```bash
dotnet new sakura -n MyAwesomeGame --android
```

And an iOS project (it needs a Mac with Xcode, the .NET 11 SDK and its iOS workload, `dotnet workload install ios`):

```bash
dotnet new sakura -n MyAwesomeGame --ios
```

The iOS project targets `net11.0-ios`, as iOS has CoreCLR from .NET 11 on; the app's own project stays on `net10.0`. Run it
on a simulator with `dotnet build MyAwesomeGame.iOS -t:Run -p:RuntimeIdentifier=iossimulator-arm64`.
