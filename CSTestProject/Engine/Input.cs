using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.Engine {
    public enum KeyCode : ushort {
        Backspace = 0x08,
        Tab = 0x09,
        Return = 0x0D,
        Pause = 0x13,
        Escape = 0x1B,
        Space = 0x20,
        PageUp = 0x21,
        PageDown = 0x22,
        End = 0x23,
        Home = 0x24,
        LeftArrow = 0x25,
        UpArrow = 0x26,
        RightArrow = 0x27,
        DownArrow = 0x28,
        Select = 0x29,
        Print = 0x2A,
        Execute = 0x2B,
        PrintScreen = 0x2C,
        Insert = 0x2D,
        Delete = 0x2E,
        Help = 0x2F,
        Num0 = 0x30,
        Num1 = 0x31,
        Num2 = 0x32,
        Num3 = 0x33,
        Num4 = 0x34,
        Num5 = 0x35,
        Num6 = 0x36,
        Num7 = 0x37,
        Num8 = 0x38,
        Num9 = 0x39,
        A = 0x41,
        B = 0x42,
        C = 0x43,
        D = 0x44,
        E = 0x45,
        F = 0x46,
        G = 0x47,
        H = 0x48,
        I = 0x49,
        J = 0x4A,
        K = 0x4B,
        L = 0x4C,
        M = 0x4D,
        N = 0x4E,
        O = 0x4F,
        P = 0x50,
        Q = 0x51,
        R = 0x52,
        S = 0x53,
        T = 0x54,
        U = 0x55,
        V = 0x56,
        W = 0x57,
        X = 0x58,
        Y = 0x59,
        Z = 0x5A,
        WinLeft = 0x5B,
        WinRight = 0x5C,
        Application = 0x5D,
        Sleep = 0x5F,
        Numpad0 = 0x60,
        Numpad1 = 0x61,
        Numpad2 = 0x62,
        Numpad3 = 0x63,
        Numpad4 = 0x64,
        Numpad5 = 0x65,
        Numpad6 = 0x66,
        Numpad7 = 0x67,
        Numpad8 = 0x68,
        Numpad9 = 0x69,
        NumpadMultiply = 0x6A,
        NumpadAdd = 0x6B,
        NumpadSeparator = 0x6C,
        NumpadSubtract = 0x6D,
        NumpadPeriod = 0x6E,
        NumpadDivide = 0x6F,
        F1 = 0x70,
        F2 = 0x71,
        F3 = 0x72,
        F4 = 0x73,
        F5 = 0x74,
        F6 = 0x75,
        F7 = 0x76,
        F8 = 0x77,
        F9 = 0x78,
        F10 = 0x79,
        F11 = 0x7A,
        F12 = 0x7B,
        NumLock = 0x90,
        ScrollLock = 0x91,
        LeftShift = 0xA0,
        RightShift = 0xA1,
        LeftControl = 0xA2,
        RightControl = 0xA3,
        LeftAlt = 0xA4,
        RightAlt = 0xA5,
        WebBack = 0xA6,
        WebForward = 0xA7,
        WebRefresh = 0xA8,
        WebStop = 0xA9,
        VolumeMute = 0xAD,
        VolumeDown = 0xAE,
        VolumeUp = 0xAF,
        MediaNext = 0xB0,
        MediaPrevious = 0xB1,
        MediaStop = 0xB2,
        MediaPlay = 0xB3,
        Play = 0xFA,
        Zoom = 0xFB,
        Clear = 0xFE,
    }
    public enum Modifiers : ushort {
        None = 0x00,
        Shift = 0x01,
        Control = 0x02,
        Alt = 0x04,
    }
    public static class Input {

        private static Core core => Core.ActiveInstance;

        unsafe public static Vector2 GetMousePosition() {
            var pointers = core.GetInput().GetPointers();
            if (pointers.Length == 0) return Vector2.Zero;
            return pointers[0].mPositionCurrent;
        }

        // Is the key currently pressed
        public static bool GetKeyDown(KeyCode key) {
            return core.GetInput().GetKeyDown((char)key);
        }
        public static float GetSignedAxis(KeyCode negative, KeyCode positive) {
            return (GetKeyDown(negative) ? -1f : 0f) + (GetKeyDown(positive) ? 1f : 0f);
        }
        // Was the key pressed this frame
        public static bool GetKeyPressed(KeyCode key) {
            return core.GetInput().GetKeyPressed((char)key);
        }
        // Was the key released this frame
        public static bool GetKeyReleased(KeyCode key) {
            return core.GetInput().GetKeyReleased((char)key);
        }

    }
}
