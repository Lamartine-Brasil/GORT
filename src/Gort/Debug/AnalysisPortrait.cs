using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Gort.Core;
using Gort.Loop;

namespace Gort.Debug;

/// <summary>
/// Retrato de análise (RF-492): arquivo estruturado por ciclo com instante,
/// modo, OCR, serviço, textos e, por área, linhas/palavras/caixas e blocos.
/// </summary>
public static class AnalysisPortrait
{
    public static string Write(
        string windowMode, string ocrEngine, string service,
        List<(int Index, bool Snapshot, Platform.ScreenRect Area,
            Platform.ScreenRect Result, List<Text.Block> Blocks,
            List<string> Translations)> areas)
    {
        try
        {
            Directory.CreateDirectory(Paths.DebugDir);
            string name = $"cycle-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json";
            string path = Path.Combine(Paths.DebugDir, name);
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions
            {
                Indented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder
                    .UnsafeRelaxedJsonEscaping,
            }))
            {
                w.WriteStartObject();
                w.WriteString("instant", DateTime.Now.ToString("o"));
                w.WriteString("window", windowMode);
                w.WriteString("ocr", ocrEngine);
                w.WriteString("service", service);
                w.WriteStartArray("areas");
                foreach (var (idx, snap, area, result, blocks, trs) in areas)
                {
                    w.WriteStartObject();
                    w.WriteNumber("index", idx);
                    w.WriteBoolean("snapshot", snap);
                    w.WriteString("area", $"{area.X},{area.Y},{area.W},{area.H}");
                    w.WriteString("result", $"{result.X},{result.Y},{result.W},{result.H}");
                    w.WriteStartArray("lines");
                    int li = 0;
                    foreach (var b in blocks)
                        foreach (var ln in b.Lines)
                        {
                            w.WriteStartObject();
                            w.WriteNumber("line", li++);
                            w.WriteString("text", ln.Text);
                            w.WriteString("box", $"{ln.X},{ln.Y},{ln.W},{ln.H}");
                            w.WriteStartArray("words");
                            foreach (var wd in ln.Words)
                            {
                                w.WriteStartObject();
                                w.WriteString("text", wd.Text);
                                w.WriteString("box", $"{wd.X},{wd.Y},{wd.W},{wd.H}");
                                w.WriteEndObject();
                            }
                            w.WriteEndArray();
                            w.WriteEndObject();
                        }
                    w.WriteEndArray();
                    w.WriteStartArray("blocks");
                    for (int i = 0; i < blocks.Count; i++)
                    {
                        var b = blocks[i];
                        w.WriteStartObject();
                        w.WriteString("text", i < trs.Count ? trs[i] : "");
                        w.WriteBoolean("title", b.IsTitle);
                        w.WriteString("orientation", b.Orientation.ToString());
                        w.WriteString("origin", $"{b.OX},{b.OY},{b.OW},{b.OH}");
                        w.WriteString("view", $"{b.VX},{b.VY},{b.VW},{b.VH}");
                        w.WriteString("content", $"{b.CX},{b.CY},{b.CW},{b.CH}");
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            File.WriteAllBytes(path, ms.ToArray());
            return path;
        }
        catch { return ""; }
    }
}
