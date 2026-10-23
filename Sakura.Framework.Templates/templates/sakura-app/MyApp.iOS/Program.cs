using Sakura.Framework.iOS;

namespace MyApp.iOS;

public static class Program
{
    public static void Main(string[] args) => SakuraApplication.Run("my-app", () => new MyAppApp());
}
