using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Input;

namespace rust_midi
{
    public abstract class GlobalHotKey : IDisposable
    {
        private static readonly InvisibleWindowForMessages Window = new InvisibleWindowForMessages();
        private static int _currentId;
        private const uint ModNorepeat = 0x4000;
        private static readonly List<HotKeyWithAction> RegisteredHotKeys = new List<HotKeyWithAction>();

        static GlobalHotKey()
        {
            Window.KeyPressed += (s, e) =>
            {
                RegisteredHotKeys.ForEach(x =>
                {
                    if (e.Modifier == x.Modifier && e.Key == x.Key) x.Action();
                });
            };
        }

        public void Dispose()
        {
            // unregister all the registered hot keys.
            for (var i = _currentId; i > 0; i--) UnregisterHotKey(Window.Handle, i);

            // dispose the inner native window.
            Window.Dispose();
        }

        /// <summary>
        ///     Registers a global hotkey
        /// </summary>
        /// <param name="aKeyGesture">e.g. Alt + Shift + Control + Win + S</param>
        /// <param name="aAction">Action to be called when hotkey is pressed</param>
        /// <returns>true, if registration succeeded, otherwise false</returns>
        public static bool RegisterHotKey(string aKeyGestureString, Action aAction)
        {
            var c = new KeyGestureConverter();
            var aKeyGesture = (KeyGesture)c.ConvertFrom(aKeyGestureString);
            return aKeyGesture != null && RegisterHotKey(aKeyGesture.Modifiers, aKeyGesture.Key, aAction);
        }

        public static bool RegisterHotKey(ModifierKeys aModifier, Key aKey, Action aAction)
        {
            //if (aModifier == ModifierKeys.None)
            //{
            //    throw new ArgumentException("Modifier must not be ModifierKeys.None");
            //}
            if (aAction is null) throw new ArgumentNullException(nameof(aAction));

            var aVirtualKeyCode = (Keys)KeyInterop.VirtualKeyFromKey(aKey);
            _currentId = _currentId + 1;
            var aRegistered = RegisterHotKey(Window.Handle, _currentId,
                (uint)aModifier | ModNorepeat,
                (uint)aVirtualKeyCode);

            if (aRegistered) RegisteredHotKeys.Add(new HotKeyWithAction(aModifier, aKey, aAction));
            return aRegistered;
        }

        // Registers a hot key with Windows.
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        // Unregisters the hot key with Windows.
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private class HotKeyWithAction
        {
            public HotKeyWithAction(ModifierKeys modifier, Key key, Action action)
            {
                Modifier = modifier;
                Key = key;
                Action = action;
            }

            public ModifierKeys Modifier { get; }
            public Key Key { get; }
            public Action Action { get; }
        }

        private sealed class InvisibleWindowForMessages : NativeWindow, IDisposable
        {
            private static readonly int WM_HOTKEY = 0x0312;

            public InvisibleWindowForMessages()
            {
                CreateHandle(new CreateParams());
            }

            #region IDisposable Members

            public void Dispose()
            {
                DestroyHandle();
            }

            #endregion

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);

                if (m.Msg == WM_HOTKEY)
                {
                    var aWpfKey = KeyInterop.KeyFromVirtualKey(((int)m.LParam >> 16) & 0xFFFF);
                    var modifier = (ModifierKeys)((int)m.LParam & 0xFFFF);
                    if (KeyPressed != null) KeyPressed(this, new HotKeyPressedEventArgs(modifier, aWpfKey));
                }
            }


            public event EventHandler<HotKeyPressedEventArgs> KeyPressed;

            public class HotKeyPressedEventArgs : EventArgs
            {
                internal HotKeyPressedEventArgs(ModifierKeys modifier, Key key)
                {
                    Modifier = modifier;
                    Key = key;
                }

                public ModifierKeys Modifier { get; }

                public Key Key { get; }
            }
        }
    }
}