using System;
using System.Collections.Generic;
using System.Linq;

namespace DragNWash.ModFramework.Graphs
{
    // The lines this library wrote, kept so the editor on the Bridge's page can
    // show what a graph is doing while it runs. The page asks for everything
    // after a number it was given last time (there are no server-sent events
    // through the Bridge, so it polls), and the number only ever grows.
    //
    // A ring of the last few hundred lines: a graph that logs in a loop must not
    // be able to fill memory, and nobody scrolls further back than this anyway.
    // The log itself is BepInEx's, as before; this is a copy for the page.
    internal static class GraphLog
    {
        private const int Keep = 400;

        private sealed class Line
        {
            internal int Number;
            internal string At;
            internal string Level;
            internal string Text;
        }

        private static readonly Queue<Line> Lines = new Queue<Line>();
        private static int _next = 1;
        private static readonly object Lock = new object();

        internal static void Add(GraphLevel level, string text)
        {
            lock (Lock)
            {
                Lines.Enqueue(new Line
                {
                    Number = _next++,
                    At = DateTime.Now.ToString("HH:mm:ss"),
                    Level = level.ToString().ToLowerInvariant(),
                    Text = text ?? "",
                });
                while (Lines.Count > Keep)
                {
                    Lines.Dequeue();
                }
            }
        }

        /// <summary>
        /// Everything after <paramref name="after"/>, and the number to ask with
        /// next time. A caller that has fallen behind the ring gets what is left
        /// and a note saying how many lines it missed.
        /// </summary>
        internal static Dictionary<string, object> Since(int after)
        {
            lock (Lock)
            {
                var lines = Lines.Where(l => l.Number > after).ToList();
                int missed = 0;
                if (Lines.Count > 0 && after > 0 && after < Lines.Peek().Number - 1)
                {
                    missed = Lines.Peek().Number - 1 - after;
                }
                return new Dictionary<string, object>
                {
                    ["next"] = _next - 1,
                    ["missed"] = missed,
                    ["lines"] = lines.Select(l => (object)new Dictionary<string, object>
                    {
                        ["number"] = l.Number,
                        ["at"] = l.At,
                        ["level"] = l.Level,
                        ["text"] = l.Text,
                    }).ToList(),
                };
            }
        }
    }
}
