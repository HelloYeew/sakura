// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Diagnostics.CodeAnalysis;
using Silk.NET.SDL;

namespace Sakura.Framework.Input;

/// <summary>
/// A set of mapping from SDL enum codes to framework's key enum.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal static class SDLEnumMapping
{
    /// <summary>
    /// Convert an SDL scancode to a framework <see cref="Key"/>
    /// </summary>
    /// <param name="scancode">The SDL scancode to convert.</param>
    /// <returns>The corresponding framework <see cref="Key"/>.</returns>
    public static Key ToSakuraKey(Scancode scancode)
    {
        switch (scancode)
        {
            case Scancode.ScancodeA: return Key.A;
            case Scancode.ScancodeB: return Key.B;
            case Scancode.ScancodeC: return Key.C;
            case Scancode.ScancodeD: return Key.D;
            case Scancode.ScancodeE: return Key.E;
            case Scancode.ScancodeF: return Key.F;
            case Scancode.ScancodeG: return Key.G;
            case Scancode.ScancodeH: return Key.H;
            case Scancode.ScancodeI: return Key.I;
            case Scancode.ScancodeJ: return Key.J;
            case Scancode.ScancodeK: return Key.K;
            case Scancode.ScancodeL: return Key.L;
            case Scancode.ScancodeM: return Key.M;
            case Scancode.ScancodeN: return Key.N;
            case Scancode.ScancodeO: return Key.O;
            case Scancode.ScancodeP: return Key.P;
            case Scancode.ScancodeQ: return Key.Q;
            case Scancode.ScancodeR: return Key.R;
            case Scancode.ScancodeS: return Key.S;
            case Scancode.ScancodeT: return Key.T;
            case Scancode.ScancodeU: return Key.U;
            case Scancode.ScancodeV: return Key.V;
            case Scancode.ScancodeW: return Key.W;
            case Scancode.ScancodeX: return Key.X;
            case Scancode.ScancodeY: return Key.Y;
            case Scancode.ScancodeZ: return Key.Z;
            case Scancode.Scancode1: return Key.Number1;
            case Scancode.Scancode2: return Key.Number2;
            case Scancode.Scancode3: return Key.Number3;
            case Scancode.Scancode4: return Key.Number4;
            case Scancode.Scancode5: return Key.Number5;
            case Scancode.Scancode6: return Key.Number6;
            case Scancode.Scancode7: return Key.Number7;
            case Scancode.Scancode8: return Key.Number8;
            case Scancode.Scancode9: return Key.Number9;
            case Scancode.Scancode0: return Key.Number0;
            case Scancode.ScancodeReturn: return Key.Enter;
            case Scancode.ScancodeEscape: return Key.Escape;
            case Scancode.ScancodeBackspace: return Key.BackSpace;
            case Scancode.ScancodeTab: return Key.Tab;
            case Scancode.ScancodeSpace: return Key.Space;
            case Scancode.ScancodeMinus: return Key.Minus;
            case Scancode.ScancodeEquals: return Key.Plus; // Often paired with Plus
            case Scancode.ScancodeLeftbracket: return Key.BracketLeft;
            case Scancode.ScancodeRightbracket: return Key.BracketRight;
            case Scancode.ScancodeBackslash: return Key.BackSlash;
            case Scancode.ScancodeSemicolon: return Key.Semicolon;
            case Scancode.ScancodeApostrophe: return Key.Quote;
            case Scancode.ScancodeGrave: return Key.Tilde;
            case Scancode.ScancodeComma: return Key.Comma;
            case Scancode.ScancodePeriod: return Key.Period;
            case Scancode.ScancodeSlash: return Key.Slash;
            case Scancode.ScancodeCapslock: return Key.CapsLock;
            case Scancode.ScancodeF1: return Key.F1;
            case Scancode.ScancodeF2: return Key.F2;
            case Scancode.ScancodeF3: return Key.F3;
            case Scancode.ScancodeF4: return Key.F4;
            case Scancode.ScancodeF5: return Key.F5;
            case Scancode.ScancodeF6: return Key.F6;
            case Scancode.ScancodeF7: return Key.F7;
            case Scancode.ScancodeF8: return Key.F8;
            case Scancode.ScancodeF9: return Key.F9;
            case Scancode.ScancodeF10: return Key.F10;
            case Scancode.ScancodeF11: return Key.F11;
            case Scancode.ScancodeF12: return Key.F12;
            case Scancode.ScancodePrintscreen: return Key.PrintScreen;
            case Scancode.ScancodeScrolllock: return Key.ScrollLock;
            case Scancode.ScancodePause: return Key.Pause;
            case Scancode.ScancodeInsert: return Key.Insert;
            case Scancode.ScancodeHome: return Key.Home;
            case Scancode.ScancodePageup: return Key.PageUp;
            case Scancode.ScancodeDelete: return Key.Delete;
            case Scancode.ScancodeEnd: return Key.End;
            case Scancode.ScancodePagedown: return Key.PageDown;
            case Scancode.ScancodeRight: return Key.Right;
            case Scancode.ScancodeLeft: return Key.Left;
            case Scancode.ScancodeDown: return Key.Down;
            case Scancode.ScancodeUp: return Key.Up;
            case Scancode.ScancodeNumlockclear: return Key.NumLock;
            case Scancode.ScancodeKPDivide: return Key.KeypadDivide;
            case Scancode.ScancodeKPMultiply: return Key.KeypadMultiply;
            case Scancode.ScancodeKPMinus: return Key.KeypadSubtract;
            case Scancode.ScancodeKPPlus: return Key.KeypadAdd;
            case Scancode.ScancodeKPEnter: return Key.KeypadEnter;
            case Scancode.ScancodeKP1: return Key.Keypad1;
            case Scancode.ScancodeKP2: return Key.Keypad2;
            case Scancode.ScancodeKP3: return Key.Keypad3;
            case Scancode.ScancodeKP4: return Key.Keypad4;
            case Scancode.ScancodeKP5: return Key.Keypad5;
            case Scancode.ScancodeKP6: return Key.Keypad6;
            case Scancode.ScancodeKP7: return Key.Keypad7;
            case Scancode.ScancodeKP8: return Key.Keypad8;
            case Scancode.ScancodeKP9: return Key.Keypad9;
            case Scancode.ScancodeKP0: return Key.Keypad0;
            case Scancode.ScancodeKPPeriod: return Key.KeypadDecimal;
            case Scancode.ScancodeLctrl: return Key.ControlLeft;
            case Scancode.ScancodeLshift: return Key.ShiftLeft;
            case Scancode.ScancodeLalt: return Key.AltLeft;
            case Scancode.ScancodeLgui: return Key.WinLeft;
            case Scancode.ScancodeRctrl: return Key.ControlRight;
            case Scancode.ScancodeRshift: return Key.ShiftRight;
            case Scancode.ScancodeRalt: return Key.AltRight;
            case Scancode.ScancodeRgui: return Key.WinRight;
            default: return Key.Unknown;
        }
    }

    /// <summary>
    /// Convert SDL MouseButtonFlags to framework <see cref="MouseButton"/>
    /// Reference : https://wiki.libsdl.org/SDL3/SDL_MouseButtonFlags
    /// </summary>
    /// <param name="sdlButton">The SDL mouse button to convert.</param>
    /// <returns>The corresponding framework <see cref="MouseButton"/>.</returns>
    public static MouseButton ToSakuraMouseButton(byte sdlButton)
    {
        return sdlButton switch
        {
            1 => MouseButton.Left,
            2 => MouseButton.Middle,
            3 => MouseButton.Right,
            4 => MouseButton.Button4,
            5 => MouseButton.Button5,
            _ => MouseButton.Unknown
        };
    }
}
