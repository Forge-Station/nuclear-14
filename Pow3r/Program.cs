using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ImGuiNET;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using ErrorCode = OpenTK.Windowing.GraphicsLibraryFramework.ErrorCode;
using Vector2 = System.Numerics.Vector2;

// ReSharper disable PossibleNullReferenceException

namespace Pow3r
{
    internal sealed unsafe partial class Program
    {
        private Renderer _renderer = Renderer.Veldrid;

        [UnmanagedCallersOnly]
        private static byte* GetClipboardTextCallback(void* userData)
        {
            return GLFW.GetClipboardStringRaw((Window*) userData);
        }

        [UnmanagedCallersOnly]
        private static void SetClipboardTextCallback(void* userData, byte* text)
        {
            GLFW.SetClipboardStringRaw((Window*) userData, text);
        }

        private static readonly GLFWCallbacks.ErrorCallback ErrorCallback = GlfwErrorCallback;

        private static void GlfwErrorCallback(ErrorCode error, string description)
        {
            Console.WriteLine($"{error}: {description}");
        }

        private bool[] _mouseJustPressed = new bool[5];

        private bool _fullscreen;
        private int _monitorIdx;
        private bool _vsync = true;
        private GameWindow _window;
        private readonly Stopwatch _stopwatch = new();
        private readonly Cursor*[] _cursors = new Cursor*[9];
        private readonly float[] _frameTimings = new float[180];
        private int _frameTimeIdx = 0;
        private int _tps = 60;

        private void Run(string[] args)
        {
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--renderer")
                {
                    _renderer = Enum.Parse<Renderer>(args[++i]);
                }
                else if (args[i] == "--veldrid")
                {
                    _vdRenderer = Enum.Parse<VeldridRenderer>(args[++i]);
                }
                else if (args[i] == "--fullscreen")
                {
                    _fullscreen = true;
                }
                else if (args[i] == "--monitor-idx")
                {
                    _monitorIdx = int.Parse(args[++i]);
                }
                else if (args[i] == "--no-vsync")
                {
                    _vsync = false;
                }
                else if (args[i] == "--help")
                {
                    Console.WriteLine("--renderer <Veldrid|OpenGL>");
                    Console.WriteLine("--veldrid <Vulkan|OpenGL|D3D11>");
                    Console.WriteLine("--no-vsync");
                    Console.WriteLine("--fullscreen");
                    Console.WriteLine("--monitor-idx");
                    Console.WriteLine("--help");
                    return;
                }
                else
                {
                    Console.WriteLine($"unknown arg \"{args[i]}\"");
                    return;
                }
            }

            Console.WriteLine($"Renderer: {_renderer}");
            if (_renderer == Renderer.Veldrid)
                Console.WriteLine($"Veldrid API: {_vdRenderer}");

            Console.WriteLine($"Fullscreen: {_fullscreen}");
            Console.WriteLine($"VSync: {_vsync}");

            //NativeLibrary.Load("nvapi64.dll");
            GLFW.Init();
            GLFW.SetErrorCallback(ErrorCallback);

            // var sw = Stopwatch.StartNew();
            GLFW.WindowHint(WindowHintBool.SrgbCapable, true);
            var windowSettings = new NativeWindowSettings
            {
                Size = (1280, 720),
                WindowState = WindowState.Maximized,
                StartVisible = false,

                Title = "Pow3r"
            };


            var openGLBased = _renderer == Renderer.OpenGL ||
                              (_renderer == Renderer.Veldrid && _vdRenderer == VeldridRenderer.OpenGL);

            if (openGLBased)
            {
                windowSettings.API = ContextAPI.OpenGL;
                if (_renderer == Renderer.Veldrid)
                {
                    windowSettings.Profile = ContextProfile.Core;
                    windowSettings.APIVersion = new Version(4, 6);
                    windowSettings.Flags = ContextFlags.ForwardCompatible;
                }
                else
                {
                    windowSettings.Profile = ContextProfile.Any;
                    windowSettings.APIVersion = new Version(1, 5);
                }
#if DEBUG
                windowSettings.Flags |= ContextFlags.Debug;
#endif
            }
            else
            {
                windowSettings.API = ContextAPI.NoAPI;
            }

            _window = new GameWindow(GameWindowSettings.Default, windowSettings);

            // Console.WriteLine(sw.ElapsedMilliseconds);

            if (_fullscreen)
            {
                var monitors = GLFW.GetMonitors();
                var monitor = monitors[_monitorIdx];
                var monitorMode = GLFW.GetVideoMode(monitor);

                GLFW.SetWindowMonitor(
                    _window.WindowPtr,
                    monitor,
                    0, 0,
                    monitorMode->Width,
                    monitorMode->Height,
                    monitorMode->RefreshRate);
            }

            if (openGLBased)
            {
                _window.VSync = _vsync ? VSyncMode.On : VSyncMode.Off;
            }

            var context = ImGui.CreateContext();
            ImGui.SetCurrentContext(context);
            ImGui.StyleColorsDark();

            var io = ImGui.GetIO();
            io.Fonts.AddFontDefault();

            delegate* unmanaged<void*, byte*> getClipboardCallback = &GetClipboardTextCallback;
            io.GetClipboardTextFn = (IntPtr) getClipboardCallback;
            delegate* unmanaged<void*, byte*, void> setClipboardCallback = &SetClipboardTextCallback;
            io.GetClipboardTextFn = (IntPtr) setClipboardCallback;
            io.ClipboardUserData = (IntPtr) _window.WindowPtr;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
            io.BackendFlags |= ImGuiBackendFlags.HasSetMousePos;
            io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors;

            _cursors[(int) ImGuiMouseCursor.Arrow] = GLFW.CreateStandardCursor(CursorShape.Arrow);
            _cursors[(int) ImGuiMouseCursor.TextInput] = GLFW.CreateStandardCursor(CursorShape.IBeam);
            _cursors[(int) ImGuiMouseCursor.ResizeNS] = GLFW.CreateStandardCursor(CursorShape.VResize);
            _cursors[(int) ImGuiMouseCursor.ResizeEW] = GLFW.CreateStandardCursor(CursorShape.HResize);
            _cursors[(int) ImGuiMouseCursor.Hand] = GLFW.CreateStandardCursor(CursorShape.Hand);
            _cursors[(int) ImGuiMouseCursor.ResizeAll] = GLFW.CreateStandardCursor(CursorShape.Arrow);
            _cursors[(int) ImGuiMouseCursor.ResizeNESW] = GLFW.CreateStandardCursor(CursorShape.Arrow);
            _cursors[(int) ImGuiMouseCursor.ResizeNWSE] = GLFW.CreateStandardCursor(CursorShape.Arrow);
            _cursors[(int) ImGuiMouseCursor.NotAllowed] = GLFW.CreateStandardCursor(CursorShape.Arrow);

            InitRenderer();

            _window.MouseDown += OnMouseDown;
            _window.TextInput += WindowOnTextInput;
            _window.MouseWheel += WindowOnMouseWheel;
            _window.KeyDown += args => KeyCallback(args, true);
            _window.KeyUp += args => KeyCallback(args, false);

            _stopwatch.Start();

            LoadFromDisk();

            _window.IsVisible = true;

            var lastTick = TimeSpan.Zero;
            var lastFrame = TimeSpan.Zero;
            var curTime = TimeSpan.Zero;

            while (!GLFW.WindowShouldClose(_window.WindowPtr))
            {
                NativeWindow.ProcessWindowEvents(false);

                var tickSpan = TimeSpan.FromSeconds(1f / _tps);
                while (curTime - lastTick > tickSpan)
                {
                    lastTick += tickSpan;

                    Tick((float) tickSpan.TotalSeconds);
                }

                _frameTimeIdx = (_frameTimeIdx + 1) % _frameTimings.Length;

                var dt = curTime - lastFrame;
                lastFrame = curTime;
                _frameTimings[_frameTimeIdx] = (float) dt.TotalMilliseconds;

                FrameUpdate((float) dt.TotalSeconds);
                Render();
                curTime = _stopwatch.Elapsed;
            }

            SaveToDisk();
        }

        private static void KeyCallback(KeyboardKeyEventArgs obj, bool down)
        {
            var io = ImGui.GetIO();
            if (obj.Key == Keys.Unknown)
                return;

            var imguiKey = TranslateKey(obj.Key);
            if (imguiKey != ImGuiKey.None)
                io.AddKeyEvent(imguiKey, down);

            io.AddKeyEvent(ImGuiKey.ModCtrl, obj.Control);
            io.AddKeyEvent(ImGuiKey.ModShift, obj.Shift);
            io.AddKeyEvent(ImGuiKey.ModAlt, obj.Alt);
        }

        private static ImGuiKey TranslateKey(Keys key)
        {
            return key switch
            {
                Keys.Tab => ImGuiKey.Tab,
                Keys.Left => ImGuiKey.LeftArrow,
                Keys.Right => ImGuiKey.RightArrow,
                Keys.Up => ImGuiKey.UpArrow,
                Keys.Down => ImGuiKey.DownArrow,
                Keys.PageUp => ImGuiKey.PageUp,
                Keys.PageDown => ImGuiKey.PageDown,
                Keys.Home => ImGuiKey.Home,
                Keys.End => ImGuiKey.End,
                Keys.Insert => ImGuiKey.Insert,
                Keys.Delete => ImGuiKey.Delete,
                Keys.Backspace => ImGuiKey.Backspace,
                Keys.Space => ImGuiKey.Space,
                Keys.Enter => ImGuiKey.Enter,
                Keys.Escape => ImGuiKey.Escape,
                Keys.Apostrophe => ImGuiKey.Apostrophe,
                Keys.Comma => ImGuiKey.Comma,
                Keys.Minus => ImGuiKey.Minus,
                Keys.Period => ImGuiKey.Period,
                Keys.Slash => ImGuiKey.Slash,
                Keys.Semicolon => ImGuiKey.Semicolon,
                Keys.Equal => ImGuiKey.Equal,
                Keys.LeftBracket => ImGuiKey.LeftBracket,
                Keys.Backslash => ImGuiKey.Backslash,
                Keys.RightBracket => ImGuiKey.RightBracket,
                Keys.GraveAccent => ImGuiKey.GraveAccent,
                Keys.CapsLock => ImGuiKey.CapsLock,
                Keys.ScrollLock => ImGuiKey.ScrollLock,
                Keys.NumLock => ImGuiKey.NumLock,
                Keys.PrintScreen => ImGuiKey.PrintScreen,
                Keys.Pause => ImGuiKey.Pause,
                Keys.KeyPad0 => ImGuiKey.Keypad0,
                Keys.KeyPad1 => ImGuiKey.Keypad1,
                Keys.KeyPad2 => ImGuiKey.Keypad2,
                Keys.KeyPad3 => ImGuiKey.Keypad3,
                Keys.KeyPad4 => ImGuiKey.Keypad4,
                Keys.KeyPad5 => ImGuiKey.Keypad5,
                Keys.KeyPad6 => ImGuiKey.Keypad6,
                Keys.KeyPad7 => ImGuiKey.Keypad7,
                Keys.KeyPad8 => ImGuiKey.Keypad8,
                Keys.KeyPad9 => ImGuiKey.Keypad9,
                Keys.KeyPadDecimal => ImGuiKey.KeypadDecimal,
                Keys.KeyPadDivide => ImGuiKey.KeypadDivide,
                Keys.KeyPadMultiply => ImGuiKey.KeypadMultiply,
                Keys.KeyPadSubtract => ImGuiKey.KeypadSubtract,
                Keys.KeyPadAdd => ImGuiKey.KeypadAdd,
                Keys.KeyPadEnter => ImGuiKey.KeypadEnter,
                Keys.KeyPadEqual => ImGuiKey.KeypadEqual,
                Keys.LeftShift => ImGuiKey.LeftShift,
                Keys.LeftControl => ImGuiKey.LeftCtrl,
                Keys.LeftAlt => ImGuiKey.LeftAlt,
                Keys.LeftSuper => ImGuiKey.LeftSuper,
                Keys.RightShift => ImGuiKey.RightShift,
                Keys.RightControl => ImGuiKey.RightCtrl,
                Keys.RightAlt => ImGuiKey.RightAlt,
                Keys.RightSuper => ImGuiKey.RightSuper,
                Keys.Menu => ImGuiKey.Menu,
                Keys.D0 => ImGuiKey._0,
                Keys.D1 => ImGuiKey._1,
                Keys.D2 => ImGuiKey._2,
                Keys.D3 => ImGuiKey._3,
                Keys.D4 => ImGuiKey._4,
                Keys.D5 => ImGuiKey._5,
                Keys.D6 => ImGuiKey._6,
                Keys.D7 => ImGuiKey._7,
                Keys.D8 => ImGuiKey._8,
                Keys.D9 => ImGuiKey._9,
                Keys.A => ImGuiKey.A,
                Keys.B => ImGuiKey.B,
                Keys.C => ImGuiKey.C,
                Keys.D => ImGuiKey.D,
                Keys.E => ImGuiKey.E,
                Keys.F => ImGuiKey.F,
                Keys.G => ImGuiKey.G,
                Keys.H => ImGuiKey.H,
                Keys.I => ImGuiKey.I,
                Keys.J => ImGuiKey.J,
                Keys.K => ImGuiKey.K,
                Keys.L => ImGuiKey.L,
                Keys.M => ImGuiKey.M,
                Keys.N => ImGuiKey.N,
                Keys.O => ImGuiKey.O,
                Keys.P => ImGuiKey.P,
                Keys.Q => ImGuiKey.Q,
                Keys.R => ImGuiKey.R,
                Keys.S => ImGuiKey.S,
                Keys.T => ImGuiKey.T,
                Keys.U => ImGuiKey.U,
                Keys.V => ImGuiKey.V,
                Keys.W => ImGuiKey.W,
                Keys.X => ImGuiKey.X,
                Keys.Y => ImGuiKey.Y,
                Keys.Z => ImGuiKey.Z,
                Keys.F1 => ImGuiKey.F1,
                Keys.F2 => ImGuiKey.F2,
                Keys.F3 => ImGuiKey.F3,
                Keys.F4 => ImGuiKey.F4,
                Keys.F5 => ImGuiKey.F5,
                Keys.F6 => ImGuiKey.F6,
                Keys.F7 => ImGuiKey.F7,
                Keys.F8 => ImGuiKey.F8,
                Keys.F9 => ImGuiKey.F9,
                Keys.F10 => ImGuiKey.F10,
                Keys.F11 => ImGuiKey.F11,
                Keys.F12 => ImGuiKey.F12,
                _ => ImGuiKey.None
            };
        }

        private static void WindowOnMouseWheel(MouseWheelEventArgs obj)
        {
            var io = ImGui.GetIO();
            io.MouseWheelH += obj.OffsetX;
            io.MouseWheel += obj.OffsetY;
        }

        private static void WindowOnTextInput(TextInputEventArgs obj)
        {
            var io = ImGui.GetIO();
            io.AddInputCharacter((uint) obj.Unicode);
        }

        private void OnMouseDown(MouseButtonEventArgs obj)
        {
            var button = (int) obj.Button;
            if (obj.IsPressed && button < _mouseJustPressed.Length)
                _mouseJustPressed[button] = true;
        }

        private void FrameUpdate(float dt)
        {
            //var sw = Stopwatch.StartNew();
            var io = ImGui.GetIO();
            GLFW.GetFramebufferSize(_window.WindowPtr, out var fbW, out var fbH);
            GLFW.GetWindowSize(_window.WindowPtr, out var wW, out var wH);
            io.DisplaySize = new Vector2(wW, wH);
            io.DisplayFramebufferScale = new Vector2(fbW / (float) wW, fbH / (float) wH);
            io.DeltaTime = dt;

            UpdateMouseState(io);
            UpdateCursorState(io);

            //Console.WriteLine($"INPUT: {sw.Elapsed.TotalMilliseconds}");

            ImGui.NewFrame();

            DoUI(dt);
        }

        private void UpdateCursorState(ImGuiIOPtr io)
        {
            var cursor = ImGui.GetMouseCursor();
            if (cursor == ImGuiMouseCursor.None)
            {
                GLFW.SetInputMode(_window.WindowPtr, CursorStateAttribute.Cursor, CursorModeValue.CursorHidden);
            }
            else
            {
                GLFW.SetCursor(_window.WindowPtr, _cursors[(int) cursor]);
                GLFW.SetInputMode(_window.WindowPtr, CursorStateAttribute.Cursor, CursorModeValue.CursorNormal);
            }
        }

        private void UpdateMouseState(ImGuiIOPtr io)
        {
            for (var i = 0; i < io.MouseDown.Count; i++)
            {
                io.MouseDown[i] = _mouseJustPressed[i] ||
                                  GLFW.GetMouseButton(_window.WindowPtr, (MouseButton) i) == InputAction.Press;
                _mouseJustPressed[i] = false;
            }

            var oldMousePos = io.MousePos;
            io.MousePos = new Vector2(float.PositiveInfinity, float.PositiveInfinity);

            var focused = _window.IsFocused;
            if (focused)
            {
                if (io.WantSetMousePos)
                {
                    GLFW.SetCursorPos(_window.WindowPtr, oldMousePos.X, oldMousePos.Y);
                }
                else
                {
                    GLFW.GetCursorPos(_window.WindowPtr, out var x, out var y);
                    io.MousePos = new Vector2((float) x, (float) y);
                }
            }
        }

        private void InitRenderer()
        {
            switch (_renderer)
            {
                case Renderer.OpenGL:
                    InitOpenGL();
                    break;

                case Renderer.Veldrid:
                    InitVeldrid();
                    break;
            }
        }

        private void Render()
        {
            ImGui.Render();

            switch (_renderer)
            {
                case Renderer.OpenGL:
                    RenderOpenGL();
                    break;

                case Renderer.Veldrid:
                    RenderVeldrid();
                    break;
            }
        }

        private static void Main(string[] args)
        {
            new Program().Run(args);
        }

        public enum Renderer
        {
            OpenGL,
            Veldrid
        }
    }
}
