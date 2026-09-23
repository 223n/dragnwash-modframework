using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using DragNWash.ModFramework.ToolWindow;
using TW = DragNWash.ModFramework.ToolWindow.ToolWindow;

namespace DragNWash.ModFramework.Bridge
{
    // The Bridge library's BepInEx entry point: listens while [Bridge] Enabled
    // and the developer tools are both on, declares itself on the Mods screen
    // (an incoming connection on this computer is still a connection), and has
    // a tab in the F1 window and a console command.
    [BepInPlugin(Bridge.Guid, "DragNWash.ModFramework.Bridge", Bridge.Version)]
    [BepInDependency(ModFramework.Guid, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(TW.Guid, BepInDependency.DependencyFlags.HardDependency)]
    internal sealed class BridgePlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static BridgeServer Server;
        private static bool? _runInBackground;   // the game's own setting, while the Bridge overrides it

        private ConfigEntry<bool> _enabled;
        private ConfigEntry<int> _port;
        private ConfigEntry<string> _openIn;
        private string _note = "";              // why the last Listen failed, whole, for the log and the console
        private ListenProblem _problem;         // the same, for the band at the top of the tab
        private int _problemPort;
        private string _problemText = "";
        private Vector2 _scroll;
        private float _contentHeight = 400;     // how tall the tab came out last draw
        private volatile bool _searching;       // Use a free port is looking
        private int _searchFrom;
        private int _found = -1;                // what it found, for Update: the port, 0 for none, -1 for nothing yet
        private int _portChangedFrom;           // the port clients were set up for, until they are set up again; 0 for none
        private DateTime _portChangedAt;

        private enum ListenProblem { None, Reserved, InUse, Other }

        private void Awake()
        {
            Log = Logger;
            _enabled = Config.Bind("Bridge", "Enabled", false,
                new ConfigDescription("Let AI clients on this computer (Claude Code, VS Code, Cursor) read the running game over MCP. Only while the developer tools are on. Read-only.",
                    null, new SettingMeta { DisplayName = "Let AI clients read the game (MCP)" }));
            _port = Config.Bind("Bridge", "Port", 47821,
                new ConfigDescription("The port on 127.0.0.1 the Bridge listens on.", new AcceptableValueRange<int>(1024, 65535), new SettingMeta { Advanced = true }));

            _openIn = Config.Bind("Bridge", "OpenPageIn", "App",
                new ConfigDescription("Where the code graph opens: App (a window of its own, CodeGraph.exe, on Windows) or Browser. Without the app, and off Windows, it opens in the browser.",
                    new AcceptableValueList<string>("App", "Browser"), new SettingMeta { DisplayName = "Open the code graph in" }));

            ModFramework.Register(new ModInfo
            {
                Guid = Bridge.Guid,
                DisplayName = "Drag'n Wash ModFramework: Bridge",
                Description = "Lets AI clients on this computer read the running game (objects, values, the log, mods, saves, dialogue) over MCP. Read-only, off by default, only with the developer tools on. Experimental.",
                Authors = new[] { "TomXV" },
                Website = "https://github.com/TomXV/dragnwash-modframework",
                IsLibrary = true,
                Network = new[]
                {
                    new NetworkUse
                    {
                        Host = "127.0.0.1 (this computer only; incoming)",
                        Purpose = "When switched on, waits for AI clients on this computer (Claude Code and the like) and answers their questions about the running game. Nothing leaves the computer.",
                        Sends = "What the client asks for: objects and their values, the log, the mods, the saves, the dialogue. Only to programs on this computer that know the token.",
                        TurnOff = "Mods → Drag'n Wash ModFramework: Bridge → Settings → Let AI clients read the game (it is off unless you turn it on)",
                    },
                },
            });

            _enabled.SettingChanged += (s, e) => Apply();
            _port.SettingChanged += (s, e) => { Stop("the port changed"); Apply(); };
            DeveloperTools.Changed += Apply;

            TW.AddTab(Bridge.Guid, "Bridge", DrawTab, 150);
            RegisterPageOperation();
            TW.AddCommand(Bridge.Guid, "bridge", "bridge | bridge on | bridge off | bridge token new | bridge disconnect  (AI clients over MCP; read-only)", Command);
            Apply();
        }

        private void OnDestroy()
        {
            Stop("the Bridge was unloaded");
        }

        private bool Wanted => _enabled.Value && DeveloperTools.Enabled;

        private void Apply()
        {
            if (Wanted && Server == null)
            {
                Listen();
            }
            else if (!Wanted)
            {
                Stop(_enabled.Value ? "the developer tools were turned off" : "it was switched off");
                // Off is off: an old failure no longer shows under it.
                ClearProblem();
            }
        }

        private void ClearProblem()
        {
            _note = "";
            _problem = ListenProblem.None;
            _problemText = "";
        }

        // Not named Start: Unity calls a MonoBehaviour's Start() by itself after Awake.
        private void Listen()
        {
            try
            {
                string unused = BridgeToken.Value;   // made now, so the setup can be shown at once
                var server = new BridgeServer(_port.Value);
                server.Start();
                Server = server;
                // The game stops when its window is not in front, and a client (the
                // browser, above all) takes the front: keep answering while listening.
                if (_runInBackground == null) _runInBackground = Application.runInBackground;
                Application.runInBackground = true;
                ClearProblem();
                Log.LogInfo($"[bridge] Listening on http://127.0.0.1:{_port.Value}/mcp (read-only, this computer only).");
            }
            catch (Exception ex)
            {
                Server = null;
                var socket = ex as System.Net.Sockets.SocketException ?? ex.InnerException as System.Net.Sockets.SocketException;
                _problem = socket != null && socket.SocketErrorCode == System.Net.Sockets.SocketError.AccessDenied ? ListenProblem.Reserved
                    : socket != null && socket.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse ? ListenProblem.InUse
                    : ListenProblem.Other;
                _problemPort = _port.Value;
                _problemText = ex.Message.Trim().TrimEnd('.', '。');
                _note = $"Could not listen on port {_port.Value}: {_problemText}. " + ListenAdvice(_problem);
                Log.LogWarning("[bridge] " + _note);
            }
        }

        // What to do about a port that could not be used. "Access denied" on
        // Windows nearly always means the port sits in a range Windows keeps for
        // Hyper-V, WSL or Docker (see "netsh interface ipv4 show
        // excludedportrange protocol=tcp"); those ranges can move at every
        // restart, so the port may work again later or stop working one day.
        private static string ListenAdvice(ListenProblem problem)
        {
            if (problem == ListenProblem.Reserved)
            {
                return "Windows has probably set this port aside (Hyper-V, WSL and Docker do that, and the ranges can change when the PC restarts). " +
                       "Press Use a free port in the Bridge tab of the F1 window (or change [Bridge] Port), and set your client up again.";
            }
            return "Another program may use it; press Use a free port in the Bridge tab of the F1 window, or change [Bridge] Port.";
        }

        // "Use a free port": looks on a worker thread (netsh and a bind per
        // port take a moment), then Update moves the Bridge there.
        private void UseFreePort()
        {
            if (_searching) return;
            _searching = true;
            int from = _searchFrom = _port.Value;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                int found = 0;
                try
                {
                    found = FreePort.After(from);
                }
                catch (Exception ex)
                {
                    Log.LogWarning($"[bridge] Looking for a free port failed: {ex.Message}");
                }
                System.Threading.Interlocked.Exchange(ref _found, found);
            });
        }

        private void Update()
        {
            int found = System.Threading.Interlocked.Exchange(ref _found, -1);
            if (found < 0) return;
            _searching = false;
            if (found == 0)
            {
                TW.ShowNotice($"None of the {FreePort.Tries} ports after {_searchFrom} is free. Try again after the PC restarts.", NoticeKind.Error);
                return;
            }
            // Turned off, moved elsewhere or listening again while it looked.
            if (!Wanted || Server != null || _port.Value != _searchFrom) return;
            if (_portChangedFrom == 0) _portChangedFrom = _searchFrom;
            if (_portChangedFrom == found) _portChangedFrom = 0;
            _portChangedAt = DateTime.Now;
            Log.LogInfo($"[bridge] Port {_searchFrom} could not be used; moving to {found}.");
            _port.Value = found;   // saved, and SettingChanged listens on it
            if (Server != null)
            {
                TW.ShowNotice(_portChangedFrom != 0
                    ? $"Listening on port {found} now. Clients set up for {_portChangedFrom} need the new setup (Copy setup)."
                    : $"Listening on port {found} now.", NoticeKind.Info, 10f);
            }
        }

        private void TryAgain()
        {
            int port = _port.Value;
            Apply();
            if (Server != null) TW.ShowNotice($"Listening on port {port} now.", NoticeKind.Info, 6f);
            else TW.ShowNotice($"Port {port} still can't be used.", NoticeKind.Warning, 6f);
        }

        private void Stop(string why)
        {
            ClearProblem();
            if (Server == null) return;
            Server.Stop();
            Server = null;
            if (_runInBackground != null)
            {
                Application.runInBackground = _runInBackground.Value;
                _runInBackground = null;
            }
            McpProtocol.EndAll();
            PageDoor.EndAll();
            Log.LogInfo($"[bridge] Stopped: {why}.");
        }

        // Opens the page on this computer (docs/CODE_GRAPH.md) with a one-time
        // code after '#', which browsers never send to a server. A write
        // operation, so MCP never offers it; the Inspector's Graph button and
        // the console call it.
        private void RegisterPageOperation()
        {
            Operations.Register(Bridge.Guid, "bridge.page.open", "Opens the Bridge's page on this computer in the browser, signed in, optionally at a method or type of the game's code.", OperationKind.Write,
                "a line saying it opened",
                args =>
                {
                    if (Server == null) throw new InvalidOperationException(!_enabled.Value ? "The Bridge is off: turn it on in the Bridge tab of the F1 window."
                        : _note.Length > 0 ? _note : "The Bridge is not listening: the developer tools are off.");
                    string focus = args.String("focus");
                    if (OpenInApp(focus)) return "Opened the code graph in its window.";
                    string url = $"http://127.0.0.1:{_port.Value}/page#code={PageDoor.NewCode()}";
                    if (!string.IsNullOrEmpty(focus)) url += "&focus=" + Uri.EscapeDataString(focus);
                    Application.OpenURL(url);
                    return "Opened the page in the browser (the link works once, within a minute).";
                },
                Operations.Parameter("focus", OperationType.String, "What to show: m:<method id>, t:<type name>, or v:graphs for the graphs editor; the search when left out."));
            // The Inspector's Graph buttons and the console open the page; a graph
            // of a data mod has no business opening windows on this computer.
            Operation open = Operations.Find("bridge.page.open");
            if (open != null) open.Audience = OperationAudience.Console | OperationAudience.Page;
        }

        private void OpenPage(string focus)
        {
            var args = focus == null ? null : new Dictionary<string, object> { ["focus"] = focus };
            OperationResult opened = Operations.CallNow("bridge.page.open", args, "bridge tab");
            if (opened.Ok) TW.ShowNotice(Convert.ToString(opened.Value), NoticeKind.Info, 8f);
            else TW.ShowNotice(opened.Error, NoticeKind.Error);
        }

        // CodeGraph.exe, next to this DLL, on Windows: it signs in with the token by itself,
        // and a window already open takes the focus instead of a second one opening.
        private bool OpenInApp(string focus)
        {
            if (_openIn.Value != "App" || Application.platform != RuntimePlatform.WindowsPlayer) return false;
            string exe = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(BridgePlugin).Assembly.Location) ?? "", "CodeGraph", "CodeGraph.exe");
            if (!System.IO.File.Exists(exe)) return false;
            try
            {
                string arguments = $"--from-game --port {_port.Value}" + (string.IsNullOrEmpty(focus) ? "" : " --focus \"" + focus.Replace("\"", "") + "\"");
                // Through the shell: a child started directly inherits the game's handles, the
                // Bridge's listening socket among them, and would keep the port after the game exits.
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, arguments) { UseShellExecute = true, WorkingDirectory = System.IO.Path.GetDirectoryName(exe) });
                return true;
            }
            catch (Exception ex)
            {
                Log.LogWarning($"[bridge] Could not start CodeGraph.exe ({ex.Message}); opening the browser instead.");
                return false;
            }
        }

        private const string NewTokenId = "bridge.token.new", DisconnectId = "bridge.disconnect";

        private static void NewToken(string disconnected)
        {
            BridgeToken.Renew();
            McpProtocol.EndAll();
            PageDoor.EndAll();
            TW.ShowNotice(disconnected == null
                ? "New token made; Copy setup has the new one."
                : $"New token made. {disconnected} {(disconnected.Contains(" and ") || disconnected.EndsWith(" clients") ? "were" : "was")} disconnected; Copy setup has the new one.", NoticeKind.Info, 8f);
        }

        // Who is connected, for the question and the notice: "claude-code",
        // "claude-code and the page", "3 clients and the page"; null for nobody.
        private static string Connected(List<McpProtocol.Session> sessions)
        {
            var names = new List<string>();
            foreach (McpProtocol.Session c in sessions)
            {
                string name = string.IsNullOrEmpty(c.Client) ? "a client" : c.Client;
                if (!names.Contains(name)) names.Add(name);
            }
            if (names.Count > 3)
            {
                names = new List<string> { sessions.Count + " clients" };
            }
            if (PageDoor.SignedIn > 0)
            {
                names.Add("the page");
            }
            if (names.Count == 0)
            {
                return null;
            }
            return names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[names.Count - 1];
        }

        private string Address => $"http://127.0.0.1:{_port.Value}/mcp";

        // The clients Copy setup has a setup for, each in the form its own
        // documentation gives: Claude Code's "claude mcp add" with --header,
        // VS Code's .vscode/mcp.json ("servers", type "http", "headers"), and
        // Cursor's mcp.json ("mcpServers", "url", "headers").
        private static readonly string[] Clients = { "Claude Code", "VS Code", "Cursor" };
        private int _client;

        private string SetupFor(int client)
        {
            string token = BridgeToken.Value;   // letters, digits, '-' and '_': nothing to escape
            string headers = "      \"headers\": { \"Authorization\": \"Bearer " + token + "\" }\n";
            switch (client)
            {
                case 1:
                    return "{\n  \"servers\": {\n    \"dragnwash\": {\n      \"type\": \"http\",\n      \"url\": \"" + Address + "\",\n" + headers + "    }\n  }\n}\n";
                case 2:
                    return "{\n  \"mcpServers\": {\n    \"dragnwash\": {\n      \"url\": \"" + Address + "\",\n" + headers + "    }\n  }\n}\n";
                default:
                    return $"claude mcp add --transport http dragnwash {Address} --header \"Authorization: Bearer {token}\"";
            }
        }

        private static string SetupNote(int client)
        {
            switch (client)
            {
                case 1:
                    return "For .vscode/mcp.json in your project. If the file lists other servers, add just the dragnwash entry to them.";
                case 2:
                    return "For .cursor/mcp.json in your project, or ~/.cursor/mcp.json for every project. If the file lists other servers, add just the dragnwash entry.";
                default:
                    return "Run it in a terminal. Already set up in Claude Code? Run claude mcp remove dragnwash first, since adding the same name twice fails.";
            }
        }

        private string Command(string[] args)
        {
            string what = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            switch (what)
            {
                case "on":
                    _enabled.Value = true;
                    return Server != null ? $"Listening on 127.0.0.1:{_port.Value}." : _note.Length > 0 ? _note : "On, but the developer tools are off.";
                case "off":
                    _enabled.Value = false;
                    return "The Bridge is off.";
                case "token":
                    if (args.Length > 1 && args[1].Equals("new", StringComparison.OrdinalIgnoreCase))
                    {
                        BridgeToken.Renew();
                        McpProtocol.EndAll();
                        PageDoor.EndAll();
                        return "New token made; every client was disconnected and needs the new setup (the Bridge tab shows it).";
                    }
                    return "bridge token new";
                case "disconnect":
                    int n = McpProtocol.AllSessions().Count;
                    McpProtocol.EndAll();
                    PageDoor.EndAll();
                    return $"Disconnected {n} client(s).";
            }
            var sb = new StringBuilder();
            sb.Append(Server != null ? $"Listening on http://127.0.0.1:{_port.Value}/mcp" : !_enabled.Value ? "Off ([Bridge] Enabled, or: bridge on)" : "On, but the developer tools are off");
            foreach (McpProtocol.Session s in McpProtocol.AllSessions())
            {
                sb.Append($"\n  {s.Client}: {s.Calls} call(s), last {s.LastUsed:HH:mm:ss}");
            }
            return sb.ToString();
        }

        // The state at the top of the tab: a panel with a 3 px bar in its
        // colour (accent listening, muted off, error when it cannot listen),
        // one line saying it, the reason under it, and the buttons that change
        // it. Returns the bottom.
        private float DrawStatus(float x, float y, float width)
        {
            var s = TW.Styles;
            bool failed = Server == null && _enabled.Value && _problem != ListenProblem.None;
            Color bar;
            string title, detail;
            if (Server != null)
            {
                bar = TW.AccentColor;
                title = $"Listening on 127.0.0.1:{Server.Port}";
                detail = "AI clients on this computer can read the game. Nothing can be changed from outside.";
            }
            else if (!_enabled.Value)
            {
                bar = TW.MutedColor;
                title = "Off";
                detail = "AI clients on this computer can't reach the game.";
            }
            else if (failed)
            {
                bar = TW.ErrorColor;
                ProblemLines(out title, out detail);
            }
            else
            {
                // Hardly seen: the F1 window is the developer tools' screen.
                bar = TW.MutedColor;
                title = "On, but the developer tools are off";
                detail = "The Bridge only listens while the developer tools are on.";
            }

            // Mending it where it shows: the Mods screen's port stepper jumps
            // by thousands, and a gamepad or the Deck has no cfg to edit.
            string[] buttons = failed
                ? new[] { _searching ? "Looking..." : "Use a free port", "Try again", "Turn off" }
                : new[] { _enabled.Value ? "Turn off" : "Turn on" };
            const float pad = 10, titleHeight = 26;
            float textX = x + 3 + pad, textWidth = width - 3 - pad * 2;
            float detailHeight = s.WrappedLabel.CalcHeight(new GUIContent(detail), textWidth);
            float height = pad + titleHeight + detailHeight + 8 + ButtonRow.Height(textWidth, buttons) + pad;
            TW.Fill(new Rect(x, y, width, height), TW.PanelColor);
            TW.Fill(new Rect(x, y, 3, height), bar);
            var titleRect = new Rect(textX, y + pad, textWidth, titleHeight);
            string shown = TW.Elide(title, s.Label, textWidth);
            GUI.Label(titleRect, shown, s.Label);
            if (shown != title) TW.Hint(titleRect, title);
            GUI.Label(new Rect(textX, y + pad + titleHeight, textWidth, detailHeight), detail, s.WrappedLabel);

            var row = new ButtonRow(textX, y + pad + titleHeight + detailHeight + 8, textWidth);
            if (failed)
            {
                bool was = GUI.enabled;
                GUI.enabled = was && !_searching;
                if (row.Button(buttons[0])) UseFreePort();
                if (row.Button(buttons[1])) TryAgain();
                GUI.enabled = was;
            }
            if (row.Button(buttons[buttons.Length - 1]))
            {
                _enabled.Value = !_enabled.Value;
            }
            return y + height;
        }

        // What went wrong, said for the player: the port is in the title so a
        // screenshot carries it, and the system's own words only when the
        // cause is not one of the two usual ones.
        private void ProblemLines(out string title, out string detail)
        {
            switch (_problem)
            {
                case ListenProblem.Reserved:
                    title = $"Not listening: Windows won't let the game use port {_problemPort}";
                    detail = "Windows keeps some ports for Hyper-V, WSL or Docker, and which ones can change when the PC restarts. Use a free port and set your client up again, or try this one later.";
                    break;
                case ListenProblem.InUse:
                    title = $"Not listening: port {_problemPort} is in use";
                    detail = "Another program probably has it. Use a free port, or close that program and try again.";
                    break;
                default:
                    title = $"Not listening on port {_problemPort}";
                    // Mono's message can come in the system's language.
                    detail = TW.Drawable(_problemText) + ". Use a free port, or try again.";
                    break;
            }
        }

        // Buttons in a row that wraps: each as wide as its text, on the next
        // line when it does not fit. Height measures without drawing, for a
        // panel that is filled before its buttons are drawn on it.
        private sealed class ButtonRow
        {
            private const float Gap = 8;
            private readonly float _left, _right;
            private float _x, _y;

            internal ButtonRow(float left, float top, float width)
            {
                _left = _x = left;
                _right = left + width;
                _y = top;
            }

            internal float Bottom => _y + TW.RowHeight;

            internal static float Width(string label) => Mathf.Max(90f, TW.Styles.Button.CalcSize(new GUIContent(label)).x + 20f);

            internal static float Height(float width, params string[] labels)
            {
                var row = new ButtonRow(0, 0, width);
                foreach (string label in labels) row.Place(Width(label));
                return row.Bottom;
            }

            internal Rect Place(float width)
            {
                if (_x > _left && _x + width > _right)
                {
                    _x = _left;
                    _y += TW.RowHeight + 6;
                }
                var rect = new Rect(_x, _y, width, TW.RowHeight);
                _x += width + Gap;
                return rect;
            }

            internal bool Button(string label, bool lit = false)
            {
                return GUI.Button(Place(Width(label)), label, lit ? TW.Styles.SelectedButton : TW.Styles.Button);
            }
        }

        // Address, token and setup, a row each, every one with its own Copy.
        // The token is never drawn: the tab may be on a stream or in a
        // screenshot. Copying it alone gives away no more than Copy setup,
        // which always had it in.
        private float DrawConnection(float x, float y, float width, string who)
        {
            var s = TW.Styles;
            float row = TW.RowHeight, labelWidth = 80, left = x + labelWidth, room = width - labelWidth;
            // Copying works while it is not listening, for setting a client up
            // ahead; the notice says it won't answer yet.
            string later = Server != null ? "" : " It won't answer until the Bridge is listening.";
            GUI.Label(new Rect(x, y, width, 26), "CONNECTION", s.Label);
            y += 30;

            string address = Address;
            float copyWidth = ButtonRow.Width("Copy");
            GUI.Label(new Rect(x, y, labelWidth, row), "Address", s.MutedLabel);
            float addressWidth = Mathf.Max(0, room - copyWidth - 8);
            GUI.Label(new Rect(left, y, addressWidth, row), TW.Elide(address, s.Label, addressWidth), s.Label);
            if (GUI.Button(new Rect(x + width - copyWidth, y, copyWidth, row), "Copy", s.Button))
            {
                GUIUtility.systemCopyBuffer = address;
                _portChangedFrom = 0;
                TW.ShowNotice("The address is on the clipboard." + later, NoticeKind.Info, 8f);
            }
            y += row + 6;
            // After Use a free port, until a copy or a client connecting shows
            // the client has the new address.
            if (_portChangedFrom != 0)
            {
                Color was = GUI.contentColor;
                GUI.contentColor = TW.WarningColor;
                string changed = $"Port changed from {_portChangedFrom}. Set your client up again with Copy setup.";
                GUI.Label(new Rect(left, y, room, 26), TW.Elide(changed, s.Label, room), s.Label);
                GUI.contentColor = was;
                TW.Hint(new Rect(left, y, room, 26), changed);
                y += 30;
            }

            float newWidth = ButtonRow.Width("New token"), buttonsWidth = copyWidth + 8 + newWidth;
            GUI.Label(new Rect(x, y, labelWidth, row), "Token", s.MutedLabel);
            const string kept = "Kept in your user profile, never shown here";
            var keptRect = new Rect(left, y, Mathf.Max(0, room - buttonsWidth - 8), row);
            string keptShown = TW.Elide(kept, s.MutedLabel, keptRect.width);
            GUI.Label(keptRect, keptShown, s.MutedLabel);
            if (keptShown != kept) TW.Hint(keptRect, kept);
            if (GUI.Button(new Rect(x + width - buttonsWidth, y, copyWidth, row), "Copy", s.Button))
            {
                GUIUtility.systemCopyBuffer = BridgeToken.Value;
                TW.ShowNotice("The token is on the clipboard." + later, NoticeKind.Info, 8f);
            }
            // It cuts off whoever is connected, so it asks first - and only
            // then: with nobody connected there is nothing to lose.
            if (GUI.Button(new Rect(x + width - newWidth, y, newWidth, row), "New token", TW.IsConfirming(NewTokenId) ? s.SelectedButton : s.Button))
            {
                if (who == null) NewToken(null);
                else TW.AskConfirm(NewTokenId);
            }
            y += row + 6;
            if (TW.IsConfirming(NewTokenId))
            {
                if (TW.Confirm(new Rect(x, y, width, row), NewTokenId, who != null ? $"New token? Disconnects {who}." : "New token?", "Yes, new token",
                    "Yes makes the token; Cancel or 5 s keeps the old one. Esc = Cancel."))
                {
                    NewToken(who);
                }
                y += row + 6;
            }

            GUI.Label(new Rect(x, y, labelWidth, row), "Setup", s.MutedLabel);
            var buttons = new ButtonRow(left, y, room);
            for (int i = 0; i < Clients.Length; i++)
            {
                if (buttons.Button(Clients[i], _client == i)) _client = i;
            }
            if (buttons.Button("Copy setup"))
            {
                GUIUtility.systemCopyBuffer = SetupFor(_client);
                _portChangedFrom = 0;
                TW.ShowNotice($"The {Clients[_client]} setup, with the token, is on the clipboard." + later, NoticeKind.Info, 8f);
            }
            y = buttons.Bottom + 4;
            string note = SetupNote(_client);
            float noteHeight = s.WrappedLabel.CalcHeight(new GUIContent(note), room);
            GUI.Label(new Rect(left, y, room, noteHeight), note, s.WrappedLabel);
            return y + noteHeight;
        }

        private void DrawTab(Rect area)
        {
            var s = TW.Styles;
            TW.Fill(area, TW.InsetColor);
            float x = 12, w = area.width - 24, row = TW.RowHeight;
            float inner = w - 20;
            List<McpProtocol.Session> sessions = McpProtocol.AllSessions();
            List<McpProtocol.CallRecord> calls = McpProtocol.RecentCalls();
            TW.ApplyScroll(area, ref _scroll);
            // As tall as the last draw came out: the panel and the rows wrap
            // with the window's width, so no fixed sum is ever right.
            _scroll = GUI.BeginScrollView(area, _scroll, new Rect(0, 0, inner, Mathf.Max(area.height, _contentHeight)), false, false);
            float y = 8;
            GUI.Label(new Rect(x, y, inner, 26), "BRIDGE: AI CLIENTS OVER MCP (READ-ONLY)", s.Label);
            y += 32;
            y = DrawStatus(x, y, inner) + 12;
            string who = Connected(sessions);
            if (_portChangedFrom != 0 && sessions.Any(c => c.Started >= _portChangedAt)) _portChangedFrom = 0;
            y = DrawConnection(x, y, inner, who) + 16;
            // The row wraps: six buttons do not fit a narrow window, and the
            // last of them was walking off the edge.
            float bx = x, by = y;
            bool Button(string label, float width, bool lit = false)
            {
                if (bx > x && bx + width > x + inner)
                {
                    bx = x;
                    by += row + 6;
                }
                bool pressed = GUI.Button(new Rect(bx, by, width, row), label, lit ? s.SelectedButton : s.Button);
                bx += width + 8;
                return pressed;
            }

            // The page is where graphs are made, so it needs a way in that does
            // not go through the game's code: the Inspector's Graph buttons open
            // it at a method, which is no help to somebody writing a graph.
            if (Button("Open page", 120))
            {
                OpenPage(null);
            }
            // The same page, on the editor: somebody writing a graph has no
            // reason to arrive at the game's code first.
            if (Button("Graphs", 120))
            {
                OpenPage("v:graphs");
            }
            // It cuts off whoever is connected, so it asks first - and only
            // then: with nobody connected there is nothing to lose.
            if (Button("Disconnect all", 150, TW.IsConfirming(DisconnectId)))
            {
                if (who == null) TW.ShowNotice("No client is connected.", NoticeKind.Info, 6f);
                else TW.AskConfirm(DisconnectId);
            }
            y = by;
            y += row + 10;
            if (TW.IsConfirming(DisconnectId))
            {
                if (TW.Confirm(new Rect(x, y, inner, row), DisconnectId, who != null ? $"Disconnect {who}?" : "Disconnect every client?", "Yes, disconnect",
                    "Yes disconnects them; they can connect again with the same token. Cancel or 5 s keeps them. Esc = Cancel."))
                {
                    McpProtocol.EndAll();
                    PageDoor.EndAll();
                    TW.ShowNotice(who != null ? $"Disconnected {who}." : "Disconnected every client.", NoticeKind.Info, 8f);
                }
                y += row + 6;
            }
            GUI.Label(new Rect(x, y, inner, 26), $"CLIENTS ({sessions.Count})" + (PageDoor.SignedIn > 0 ? $"   PAGE: {PageDoor.SignedIn} signed in" : ""), s.Label);
            y += 28;
            if (sessions.Count == 0)
            {
                GUI.Label(new Rect(x, y, inner, 26), "None connected.", s.MutedLabel);
                y += 26;
            }
            foreach (McpProtocol.Session c in sessions)
            {
                GUI.Label(new Rect(x, y, inner, 26), $"{c.Client}  (MCP {c.Version}), {c.Calls} call(s), since {c.Started:HH:mm:ss}, last {c.LastUsed:HH:mm:ss}", s.MutedLabel);
                y += 26;
            }
            y += 8;
            GUI.Label(new Rect(x, y, inner, 26), "LAST CALLS", s.Label);
            y += 28;
            if (calls.Count == 0)
            {
                GUI.Label(new Rect(x, y, inner, 26), "None yet.", s.MutedLabel);
                y += 26;
            }
            for (int i = calls.Count - 1; i >= 0; i--)
            {
                McpProtocol.CallRecord c = calls[i];
                GUI.Label(new Rect(x, y, inner, 26), $"{c.Time:HH:mm:ss}  {c.Client}: {c.Tool}{(c.Ok ? "" : "  (failed)")}", s.MutedLabel);
                y += 26;
            }
            _contentHeight = y + 12;
            GUI.EndScrollView();
        }
    }
}
