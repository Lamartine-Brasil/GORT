using System.Collections.Generic;
using Gort.Core;

namespace Gort.Config;

/// <summary>
/// Grupo de cor (7.6). Invariante RF-043/RF-055...: se início &gt; fim, trocar.
/// </summary>
public sealed class ColorGroup
{
    public int R { get; set; }
    public int G { get; set; }
    public int B { get; set; }
    public int S1 { get; set; }
    public int S2 { get; set; }
    public int V1 { get; set; }
    public int V2 { get; set; }

    public void Normalize()
    {
        R = Clamp(R, 0, 255); G = Clamp(G, 0, 255); B = Clamp(B, 0, 255);   // RF-042
        S1 = Clamp(S1, 0, 100); S2 = Clamp(S2, 0, 100);
        V1 = Clamp(V1, 0, 100); V2 = Clamp(V2, 0, 100);
        if (S1 > S2) (S1, S2) = (S2, S1);   // RF-043
        if (V1 > V2) (V1, V2) = (V2, V1);   // RF-043
    }

    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
}

public sealed class OcrArea
{
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
    public List<int> ColorGroups { get; set; } = new();
}

public sealed class ExclusionArea
{
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
}

/// <summary>
/// Perfil principal (RF-020..RF-046). Padrões na Parte IV §IV.12.
/// </summary>
public sealed class Profile
{
    public const int SchemaVersion = 1;
    public int LoadedSchema { get; set; } = SchemaVersion;

    // Identificadores textuais (RF-026)
    // RF-225: padrão = Google Tradutor (id estável "web-free", endpoint
    // translate.googleapis.com). RF-309: origens en/ja; RF-314: destino pt-BR.
    public string WindowMode { get; set; } = "overlay";
    public string TranslationService { get; set; } = "web-free";
    public string CustomPresetSubkey { get; set; } = "";   // RF-030
    public string OcrEngine { get; set; } = "modern";
    public string OcrLanguage { get; set; } = "en";        // IV.12: inglês
    public string TargetLanguage { get; set; } = "pt-BR";  // RF-314

    public string ClassicDataset { get; set; } = "eng";
    public bool ClassicFast { get; set; } = false;

    public bool ShowOcrText { get; set; } = true;
    public bool SaveResultFile { get; set; } = false;
    public bool CopyToClipboard { get; set; } = false;
    public string CopyFormat { get; set; } = "ocr-only";   // ocr-only|translation-only|both

    public int Speed { get; set; } = 2;                    // 1..5 → P-05..P-09
    public string DbFile { get; set; } = "empty.txt";
    public bool DbIgnoreCase { get; set; } = false;               // IV.12
    public bool DbPartial { get; set; } = false;                  // IV.12
    public string DictFile { get; set; } = "myDic.txt";
    public bool UseDict { get; set; } = true;
    public bool DictByWord { get; set; } = true;           // RF-044 (ajustado por idioma)
    public bool Erode { get; set; } = false;

    public List<ColorGroup> ColorGroups { get; set; } = [new ColorGroup()];
    public string ColorFilter { get; set; } = "none";      // none|rgb|hsv|threshold (mutuamente exclusivos RF-104)
    public int Threshold { get; set; } = 127;

    public List<OcrArea> Areas { get; set; } = [];         // RF-066 persistidas
    public List<ExclusionArea> Exclusions { get; set; } = [];

    public string TextOrder { get; set; } = "left";        // left|center
    public bool RemoveSpaces { get; set; } = false;
    public bool CaptureActiveWindow { get; set; } = false;
    public bool TextBackground { get; set; } = true;
    public bool AreaNumbering { get; set; } = false;
    public double Zoom { get; set; } = 2.0;                // P-22 🔒
    public bool Tts { get; set; } = false;
    public bool TtsWait { get; set; } = false;
    public int LayerX { get; set; } = -1;                  // -1 = não definido (RF-045)
    public int LayerY { get; set; } = -1;
    public int LayerW { get; set; } = -1;
    public int LayerH { get; set; } = -1;
    // Camada — teto de tamanho: a janela acompanha o texto até esse máximo
    // e a fonte encolhe para caber; 0 = livre (tamanho 100% manual).
    public bool LayerAutoFit { get; set; } = true;
    public int LayerMaxW { get; set; } = 0;
    public int LayerMaxH { get; set; } = 0;
    public bool MergeLinesOverlay { get; set; } = false;
    public bool KeepDirection { get; set; } = false;
    public bool AutoColorMaster { get; set; } = true;
    public bool AutoColorBg { get; set; } = true;
    public bool AutoColorFg { get; set; } = true;
    public bool OverlayOutline { get; set; } = true;
    public bool BgTransparency { get; set; } = false;
    public bool AutoFontSize { get; set; } = false;

    // Texto (aba 1)
    public string FontFamily { get; set; } = "";           // "" = SO (RF-387)
    public double FontSize { get; set; } = 15;             // P-127 🔒
    public double AutoMinPt { get; set; } = 10;            // P-129 (Etapa 17: UI)
    public double AutoMaxPt { get; set; } = 50;            // P-130
    public byte[] TextColor { get; set; } = [255, 255, 255];
    public byte[] Outline1 { get; set; } = [192, 192, 192];
    public byte[] Outline2 { get; set; } = [0, 0, 0];
    public byte[] BgColor { get; set; } = [170, 0, 0, 0];  // ARGB

    // Tradução por serviço: origem/destino (aba 3)
    public Dictionary<string, string> ServiceSource { get; set; } = new();
    public Dictionary<string, string> ServiceTarget { get; set; } = new();

    public int CloudMonthlyLimit { get; set; } = 950;      // P-29 🔒
    public string CloudCredFile { get; set; } = "";          // RF-126/539 (Etapa 17: UI)
    public string DeepLEndpoint { get; set; } = "free";      // RF-271: free|paid
    // Google (web-free): qualidade manual — auto (padrão, cai para baixa em
    // 429), high (sempre alta, 429 vira erro) ou low (sempre baixa/rápida).
    public string WebQuality { get; set; } = "auto";
    public string SheetsSheetId { get; set; } = "";          // RF-255 (Etapa 17: UI)
    public string LlmModel { get; set; } = "";               // RF-278: "" = padrão remoto

    public bool ModernVertical { get; set; } = false;        // RF-140 (Etapa 17: UI)
    public bool PreprocessOff { get; set; } = false;         // RF-118 (Etapa 17: UI)

    public static Profile Defaults() => new();

    /// <summary>
    /// Normaliza após carregar ou aplicar (RF-042 saturar, RF-043 trocar,
    /// RF-028 id desconhecido → padrão, RF-044 dicionário-por-palavra por idioma).
    /// Quando <paramref name="deriveLangDefaults"/> é falso, valores explícitos de
    /// dicionário/remoção são preservados (RF-148 age no evento de troca, na UI).
    /// </summary>
    public void Normalize(out List<string> notices, bool deriveLangDefaults = true)
    {
        notices = new();
        if (!Catalogs.KnownId(Catalogs.WindowModes, WindowMode))
        { notices.Add($"window_mode desconhecido '{WindowMode}'; padrão overlay."); WindowMode = "overlay"; }
        if (!Catalogs.KnownId(Catalogs.TranslationServices, TranslationService))
        { notices.Add($"translation_service desconhecido '{TranslationService}'; padrão web-free."); TranslationService = "web-free"; }
        if (!Catalogs.KnownId(Catalogs.OcrEngines, OcrEngine))
        { notices.Add($"ocr_engine desconhecido '{OcrEngine}'; padrão modern."); OcrEngine = "modern"; }
        if (LanguageTable.Find(OcrLanguage) is null)
        { notices.Add($"ocr_language desconhecido; padrão en."); OcrLanguage = "en"; }
        if (LanguageTable.Find(TargetLanguage) is null)
        { notices.Add($"target_language desconhecido; padrão pt-BR."); TargetLanguage = "pt-BR"; }

        if (Speed < 1) Speed = 1; if (Speed > 5) Speed = 5;
        Threshold = Clamp(Threshold, 0, 255);                                    // RF-042
        if (Zoom > 10) Zoom = Params.P22_ZoomDefault;                            // RF-042
        if (Zoom < Params.P23_ZoomMin) Zoom = Params.P23_ZoomMin;
        if (Zoom > Params.P24_ZoomMax) Zoom = Params.P24_ZoomMax;
        foreach (var g in ColorGroups) g.Normalize();
        if (ColorGroups.Count == 0) ColorGroups.Add(new ColorGroup());
        if (LayerMaxW < 0) LayerMaxW = 0;                       // 0 = livre
        if (LayerMaxH < 0) LayerMaxH = 0;
        if (WebQuality != "high" && WebQuality != "low") WebQuality = "auto";
        FontSize = ClampD(FontSize, 8, 72);                                      // P-128 piso
        if (CloudMonthlyLimit < 0) CloudMonthlyLimit = 0;

        var lang = LanguageTable.Find(OcrLanguage);
        if (lang is not null && deriveLangDefaults)
        {
            // RF-044 / RF-148 🔒: o PADRÃO segue a propriedade do idioma
            // ("separa palavras por espaço"), não o identificador.
            bool separates = lang.SeparatesWords;
            DictByWord = separates;
            RemoveSpaces = !separates;
        }
    }

    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    private static double ClampD(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
}
