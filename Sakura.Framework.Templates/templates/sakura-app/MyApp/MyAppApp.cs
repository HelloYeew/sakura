using Sakura.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;

namespace MyApp;

public class MyAppApp : App
{
    private Box box = null!;

    public override void Load()
    {
        base.Load();

        Add(box = new Box()
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(100, 100),
            Color = Color.LightPink
        });
    }

    public override void Update()
    {
        base.Update();
        box.Rotation += (float)Clock.ElapsedFrameTime / 10;
    }
}
