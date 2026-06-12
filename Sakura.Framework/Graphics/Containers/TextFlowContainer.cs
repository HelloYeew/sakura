// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;

namespace Sakura.Framework.Graphics.Containers;

/// <summary>
/// A paragraph-text container: lays out text as individual word <see cref="SpriteText"/>s in a
/// wrapping flow, so long text automatically breaks into lines at the container's width.
/// Supports mixed styles within one block — each <see cref="AddText(string, Action{SpriteText})"/>
/// call can style its words independently (color, font, size)
/// <remarks>
/// Typical usage: fix the width (or use <see cref="Axes.X"/> relative sizing) and let
/// <see cref="Container.AutoSizeAxes"/> = <see cref="Axes.Y"/> grow the height with content.
/// Newlines in added text break lines; <see cref="AddParagraph(string, Action{SpriteText})"/>
/// adds <see cref="ParagraphSpacing"/> before its text.
/// </remarks>
/// </summary>
public partial class TextFlowContainer : FlowContainer
{
    private readonly Action<SpriteText>? defaultCreationParameters;

    /// <summary>
    /// Vertical gap inserted by <see cref="NewParagraph"/> (and <see cref="AddParagraph(string, Action{SpriteText})"/>),
    /// in pixels.
    /// </summary>
    public float ParagraphSpacing { get; set; } = 12;

    /// <param name="defaultCreationParameters">
    /// Applied to every created <see cref="SpriteText"/> before any per-call parameters,
    /// e.g. to set a base font or color for the whole block.
    /// </param>
    public TextFlowContainer(Action<SpriteText>? defaultCreationParameters = null)
    {
        this.defaultCreationParameters = defaultCreationParameters;

        Direction = FlowDirection.Horizontal;
        AutoSizeAxes = Axes.Y;
    }

    /// <summary>
    /// Replaces the entire content with the given text.
    /// </summary>
    public string Text
    {
        set
        {
            Clear();
            AddText(value);
        }
    }

    /// <summary>
    /// Appends text, split into words that wrap at the container's width.
    /// Newline characters ('\n') break lines.
    /// </summary>
    /// <param name="text">The text to append.</param>
    /// <param name="creationParameters">Styling applied to each created word (after the container defaults).</param>
    /// <returns>The created <see cref="SpriteText"/> words, for further manipulation (e.g. fading).</returns>
    public IReadOnlyList<SpriteText> AddText(string text, Action<SpriteText>? creationParameters = null)
    {
        var created = new List<SpriteText>();

        if (string.IsNullOrEmpty(text))
            return created;

        string[] lines = text.Split('\n');

        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            if (lineIndex > 0)
                NewLine();

            string[] words = lines[lineIndex].Split(' ', StringSplitOptions.RemoveEmptyEntries);

            for (int wordIndex = 0; wordIndex < words.Length; wordIndex++)
            {
                // Keep the separating space with the word so natural spacing survives wrapping
                // with mixed fonts/sizes (the shaped advance of the space matches the word's font).
                string wordText = wordIndex < words.Length - 1 ? words[wordIndex] + " " : words[wordIndex];

                var word = new SpriteText { Text = wordText };

                defaultCreationParameters?.Invoke(word);
                creationParameters?.Invoke(word);

                created.Add(word);
                Add(word);
            }
        }

        return created;
    }

    /// <summary>
    /// Appends text as a new paragraph (a line break plus <see cref="ParagraphSpacing"/>).
    /// </summary>
    /// <param name="text">The paragraph text.</param>
    /// <param name="creationParameters">Styling applied to each created word (after the container defaults).</param>
    /// <returns>The created <see cref="SpriteText"/> words.</returns>
    public IReadOnlyList<SpriteText> AddParagraph(string text, Action<SpriteText>? creationParameters = null)
    {
        if (Children.Count > 0)
            NewParagraph();

        return AddText(text, creationParameters);
    }

    /// <summary>
    /// Forces a line break at the current position.
    /// </summary>
    public void NewLine() => Add(createBreak(0));

    /// <summary>
    /// Forces a line break plus <see cref="ParagraphSpacing"/> of vertical space.
    /// </summary>
    public void NewParagraph() => Add(createBreak(ParagraphSpacing));

    // A full-width, non-drawing child: the flow wraps before it (it can't fit beside
    // anything) and after it (nothing fits beside it), producing exactly one line break.
    // Its height adds the paragraph gap.
    private static Drawable createBreak(float height) => new Container
    {
        RelativeSizeAxes = Axes.X,
        Width = 1,
        Height = height,
    };
}
