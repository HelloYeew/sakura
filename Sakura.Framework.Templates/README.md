# Sakura Framework Template

A project template for create app/game using Sakura Framework. It targets .NET 11, so it needs the .NET 11 SDK.

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

Run the iOS project on a simulator with `dotnet build MyAwesomeGame.iOS -t:Run -p:RuntimeIdentifier=iossimulator-arm64`.
