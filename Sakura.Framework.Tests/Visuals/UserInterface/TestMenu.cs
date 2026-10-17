// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Cursor;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.UserInterface;

public partial class TestMenu : ManualInputManagerTestScene
{
    private BasicMenu menuBar = null!;
    private ContextMenuContainer contextMenus = null!;
    private ContextBox boxA = null!;
    private ContextBox boxB = null!;
    private ClickCountingBox background = null!;
    private ToggleMenuItem showGrid = null!;

    private readonly List<string> performed = new List<string>();

    [SetUp]
    public void SetUp()
    {
        AddStep("Build menus", () =>
        {
            performed.Clear();
            TestContent.Clear();

            showGrid = new ToggleMenuItem("Show grid", true);

            TestContent.Add(contextMenus = new ContextMenuContainer
            {
                RelativeSizeAxes = Axes.Both,
                Size = Vector2.One
            });

            contextMenus.Add(background = new ClickCountingBox
            {
                RelativeSizeAxes = Axes.Both,
                Size = Vector2.One,
                Color = ColorExtensionsShim.Dark
            });

            contextMenus.Add(boxA = new ContextBox
            {
                Position = new Vector2(200, 200),
                Size = new Vector2(150, 100),
                Color = Color.SteelBlue,
                Items = new MenuItem[]
                {
                    new MenuItem("Copy", () => performed.Add("copy A")) { ShortcutText = "Ctrl+C" },
                    new MenuItem("Delete", () => performed.Add("delete A")),
                    new MenuSeparator(),
                    new MenuItem("Disabled", () => performed.Add("disabled")) { Enabled = { Value = false } },
                    new MenuItem("Snap", new MenuItem[]
                    {
                        new MenuItem("1/2", () => performed.Add("snap 1/2")),
                        new MenuItem("1/4", () => performed.Add("snap 1/4"))
                    })
                }
            });

            contextMenus.Add(boxB = new ContextBox
            {
                Position = new Vector2(500, 200),
                Size = new Vector2(150, 100),
                Color = Color.DarkOrange,
                Items = new MenuItem[]
                {
                    new MenuItem("Only B", () => performed.Add("only B"))
                }
            });

            // Added after the context menus, so the bar and its menus sit in front of them.
            TestContent.Add(menuBar = new BasicMenu(FlowDirection.Horizontal, true)
            {
                RelativeSizeAxes = Axes.X,
                Size = new Vector2(1, 0),
                Items = new MenuItem[]
                {
                    new MenuItem("File", new MenuItem[]
                    {
                        new MenuItem("Save", () => performed.Add("save")) { ShortcutText = "Ctrl+S" },
                        new MenuItem("Export", new MenuItem[]
                        {
                            new MenuItem("As .osz", () => performed.Add("export osz"))
                        })
                    }),
                    new MenuItem("View", new MenuItem[] { showGrid }),
                    new MenuItem("Help", () => performed.Add("help"))
                }
            });
        });

        AddWaitStep("Let the menus lay out", 50);
    }

    private DrawableMenuItem barItem(int index) => menuBar.DrawableItems[index];

    // Resolved when the step runs: steps are queued before SetUp's step has built the boxes.
    private void openContextMenuOn(Func<Drawable> target, string name)
    {
        AddStep($"Right-click {name}", () =>
        {
            InputManager.MoveMouseTo(target());
            InputManager.Click(MouseButton.Right);
        });
    }

    [Test]
    public void TestContextMenuOpensOnRightClickAndRunsAnItem()
    {
        openContextMenuOn(() => boxA, "box A");
        AddAssert("Menu is open", () => contextMenus.Menu.State == MenuState.Open);
        AddAssert("Menu shows A's items", () => contextMenus.Menu.DrawableItems.Count == 5);
        AddAssert("Background never saw the right-click", () => background.Clicks == 0);

        AddStep("Click Copy", () =>
        {
            InputManager.MoveMouseTo(contextMenus.Menu.DrawableItems[0]);
            InputManager.Click(MouseButton.Left);
        });

        AddAssert("Copy ran", () => performed.Contains("copy A"));
        AddAssert("Menu closed", () => contextMenus.Menu.State == MenuState.Closed);
    }

    [Test]
    public void TestClickingAwayClosesWithoutClickingThrough()
    {
        openContextMenuOn(() => boxA, "box A");

        AddStep("Click empty space", () =>
        {
            InputManager.MoveMouseTo(new Vector2(50, 500));
            InputManager.Click(MouseButton.Left);
        });

        AddAssert("Menu closed", () => contextMenus.Menu.State == MenuState.Closed);
        AddAssert("The dismissing click didn't reach the background", () => background.Clicks == 0);

        AddStep("Click empty space again", () => InputManager.Click(MouseButton.Left));
        AddAssert("With no menu open, clicks go through", () => background.Clicks == 1);
    }

    [Test]
    public void TestRightClickingElsewhereMovesTheMenu()
    {
        openContextMenuOn(() => boxA, "box A");
        openContextMenuOn(() => boxB, "box B");

        AddAssert("Menu is open", () => contextMenus.Menu.State == MenuState.Open);
        AddAssert("Menu shows B's items", () => contextMenus.Menu.DrawableItems.Count == 1);
    }

    [Test]
    public void TestDisabledItemsAndSeparatorsDoNothing()
    {
        openContextMenuOn(() => boxA, "box A");

        AddStep("Click the disabled item", () =>
        {
            InputManager.MoveMouseTo(contextMenus.Menu.DrawableItems[3]);
            InputManager.Click(MouseButton.Left);
        });

        AddWaitStep("Let focus settle", 50);
        AddAssert("Nothing ran", () => performed.Count == 0);
        AddAssert("Menu is still open", () => contextMenus.Menu.State == MenuState.Open);
    }

    [Test]
    public void TestSubmenuOpensOnHover()
    {
        openContextMenuOn(() => boxA, "box A");

        AddStep("Hover Snap", () => InputManager.MoveMouseTo(contextMenus.Menu.DrawableItems[4]));
        AddUntilStep("Sub-menu opens", () => contextMenus.Menu.Submenu?.State == MenuState.Open);

        AddStep("Click 1/4", () =>
        {
            InputManager.MoveMouseTo(contextMenus.Menu.Submenu!.DrawableItems[1]);
            InputManager.Click(MouseButton.Left);
        });

        AddAssert("1/4 ran", () => performed.Contains("snap 1/4"));
        AddAssert("Everything closed", () => contextMenus.Menu.State == MenuState.Closed && contextMenus.Menu.Submenu!.State == MenuState.Closed);
    }

    [Test]
    public void TestKeyboard()
    {
        openContextMenuOn(() => boxA, "box A");

        AddStep("Down, down", () =>
        {
            InputManager.PressKey(Key.Down);
            InputManager.ReleaseKey(Key.Down);
            InputManager.PressKey(Key.Down);
            InputManager.ReleaseKey(Key.Down);
        });

        AddAssert("Delete is highlighted", () => contextMenus.Menu.DrawableItems[1].Highlighted);

        AddStep("Down skips the separator and the disabled item", () =>
        {
            InputManager.PressKey(Key.Down);
            InputManager.ReleaseKey(Key.Down);
        });

        AddAssert("Snap is highlighted", () => contextMenus.Menu.DrawableItems[4].Highlighted);

        AddStep("Right opens Snap", () =>
        {
            InputManager.PressKey(Key.Right);
            InputManager.ReleaseKey(Key.Right);
        });

        AddAssert("Sub-menu open", () => contextMenus.Menu.Submenu?.State == MenuState.Open);

        AddStep("Enter", () =>
        {
            InputManager.PressKey(Key.Enter);
            InputManager.ReleaseKey(Key.Enter);
        });

        AddAssert("1/2 ran", () => performed.Contains("snap 1/2"));
    }

    [Test]
    public void TestEscapeClosesOneLevelAtATime()
    {
        openContextMenuOn(() => boxA, "box A");

        AddStep("Hover Snap", () => InputManager.MoveMouseTo(contextMenus.Menu.DrawableItems[4]));
        AddUntilStep("Sub-menu opens", () => contextMenus.Menu.Submenu?.State == MenuState.Open);

        AddStep("Escape", () =>
        {
            InputManager.PressKey(Key.Escape);
            InputManager.ReleaseKey(Key.Escape);
        });

        AddAssert("Only the sub-menu closed", () => contextMenus.Menu.Submenu!.State == MenuState.Closed && contextMenus.Menu.State == MenuState.Open);

        AddStep("Escape", () =>
        {
            InputManager.PressKey(Key.Escape);
            InputManager.ReleaseKey(Key.Escape);
        });

        AddAssert("Menu closed", () => contextMenus.Menu.State == MenuState.Closed);
    }

    [Test]
    public void TestMenuBar()
    {
        AddStep("Click File", () =>
        {
            InputManager.MoveMouseTo(barItem(0));
            InputManager.Click(MouseButton.Left);
        });

        AddAssert("File's menu is open", () => menuBar.Submenu?.State == MenuState.Open && menuBar.Submenu.DrawableItems.Count == 2);
        AddAssert("It opened below the bar", () => menuBar.Submenu!.Y >= menuBar.DrawHeight - 1);

        AddStep("Move along to View", () => InputManager.MoveMouseTo(barItem(1)));
        AddAssert("View's menu replaced it without a click", () => menuBar.Submenu!.State == MenuState.Open && menuBar.Submenu.DrawableItems[0].Item == showGrid);

        AddStep("Click Show grid", () =>
        {
            InputManager.MoveMouseTo(menuBar.Submenu!.DrawableItems[0]);
            InputManager.Click(MouseButton.Left);
        });

        AddAssert("Toggled off", () => !showGrid.State.Value);
        AddAssert("Menu closed", () => menuBar.Submenu!.State == MenuState.Closed);

        AddStep("Click File, then click File again", () =>
        {
            InputManager.MoveMouseTo(barItem(0));
            InputManager.Click(MouseButton.Left);
            InputManager.Click(MouseButton.Left);
        });

        AddAssert("A second click on the bar closes it", () => menuBar.Submenu!.State == MenuState.Closed);

        AddStep("Click Help", () =>
        {
            InputManager.MoveMouseTo(barItem(2));
            InputManager.Click(MouseButton.Left);
        });

        AddAssert("A bar item without a menu just runs", () => performed.Contains("help"));
    }

    [Test]
    public void TestMenuBarClosesOnClickAway()
    {
        AddStep("Click File", () =>
        {
            InputManager.MoveMouseTo(barItem(0));
            InputManager.Click(MouseButton.Left);
        });

        AddStep("Click empty space", () =>
        {
            InputManager.MoveMouseTo(new Vector2(50, 500));
            InputManager.Click(MouseButton.Left);
        });

        AddAssert("Menu closed", () => menuBar.Submenu!.State == MenuState.Closed);
    }

    private static class ColorExtensionsShim
    {
        public static Color Dark => Color.FromArgb(255, 24, 24, 30);
    }

    private partial class ContextBox : Box, IHasContextMenu
    {
        public IReadOnlyList<MenuItem>? Items { get; set; }

        public IReadOnlyList<MenuItem>? ContextMenuItems => Items;
    }

    private partial class ClickCountingBox : Box
    {
        public int Clicks { get; private set; }

        public override bool OnClick(MouseButtonEvent e)
        {
            Clicks++;
            return true;
        }
    }
}
