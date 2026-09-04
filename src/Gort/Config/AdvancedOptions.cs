using System.Collections.Generic;

namespace Gort.Config;

/// <summary>
/// Opções avançadas (RF-031): arquivo separado, GLOBAL — não muda com perfil (RF-032).
/// Criado com padrões quando ausente/vazio (RF-033).
/// </summary>
public sealed class AdvancedOptions
{
    public const int SchemaVersion = 1;

    // Aplicativo
    public bool TrayMode { get; set; } = false;
    public bool RightToLeft { get; set; } = false;
    public bool RemoteAlwaysOnTop { get; set; } = false;
    public bool FollowCompat { get; set; } = false;
    public bool FollowOnly { get; set; } = true;
    public bool AttachedYellowBorder { get; set; } = false;
    public string SelectBg { get; set; } = "#FFFFFFFF";   // branco (com alfa)
    public string SelectAccent { get; set; } = "#FF000000"; // preto

    // Atalhos avançados: 4× abrir perfil + transparência + 7× trocar serviço
    public List<OpenProfileShortcut> OpenProfile { get; set; } =
        [new(), new(), new(), new()];
    public string ToggleForcedTransparency { get; set; } = "";
    public Dictionary<string, string> ServiceSwitch { get; set; } = new();

    // Janela de tradução
    public bool OverlayAutoFont { get; set; } = false;
    public bool OverlayMerge { get; set; } = false;
    public bool OverlayKeepDir { get; set; } = false;
    public bool OverlayOutline { get; set; } = false;
    public bool OverlayBgAlpha { get; set; } = false;
    public bool AutoColorMaster { get; set; } = true;
    public bool AutoColorFg { get; set; } = true;
    public bool AutoColorBg { get; set; } = true;
    public double AutoMinPt { get; set; } = 10;
    public double AutoMaxPt { get; set; } = 50;
    public int SnapshotStaySec { get; set; } = 5;         // P-131 🔒
    public string DarkFont { get; set; } = "";
    public bool LayerBottom { get; set; } = false;
    public bool LayerRight { get; set; } = false;
    public bool TopOnlyDuring { get; set; } = false;
    public bool IgnoreEmpty { get; set; } = false;            // RF-240
    public bool HideAlsoTranslates { get; set; } = false;     // RF-322 (Etapa 9)
    public bool ForcedTransparency { get; set; } = false;      // RF-335 (Etapa 11)
    public bool DisplayMemory { get; set; } = false;
    public int DisplayMemoryN { get; set; } = 5;          // P-49 (1–10)
    public int DisplayMemorySec { get; set; } = 10;       // P-50 (até 200)

    // Coletânea
    public List<string> CollectActive { get; set; } = [];
    public bool CollectAsDb { get; set; } = true;
    public bool CollectIgnoreCase { get; set; } = true;

    // Tradução
    public bool Bridge { get; set; } = false;
    public bool FallbackTranslator { get; set; } = true;
    public List<CustomPreset> CustomPresets { get; set; } = [];
    public bool CustomSameCodes { get; set; } = true;
    public string CustomSource { get; set; } = "en";
    public string CustomTarget { get; set; } = "pt-BR";
    public string CustomUrl { get; set; } = "http://localhost:8080/translator";
    public string LlmInstruction { get; set; } = "";
    public string LlmCustomModel { get; set; } = "gemini-2.0-flash";
    public bool LlmNoDefault { get; set; } = false;
    public string LlmPreset { get; set; } = "default";    // default|eco|custom
    public int LlmTemp { get; set; } = 20;                // P-64 🔒
    public int LlmReason { get; set; } = 0;               // P-65 🔒
    public int LlmMaxOut { get; set; } = 4000;            // P-66 🔒
    public bool ClipboardTranslate { get; set; } = false;
    public bool ClipboardShowOriginal { get; set; } = false;
    public bool ClipboardShowWorking { get; set; } = false;
    public string ClipboardCopyFormat { get; set; } = "ocr-only";

    // OCR / dicionário
    public bool CloudPriority { get; set; } = false;
    public int DictExtraPasses { get; set; } = 0;         // P-46 (0–3)

    public void Normalize()
    {
        if (DisplayMemoryN < 1) DisplayMemoryN = 1; if (DisplayMemoryN > 10) DisplayMemoryN = 10;
        if (DisplayMemorySec < 1) DisplayMemorySec = 1; if (DisplayMemorySec > 200) DisplayMemorySec = 200;
        if (DictExtraPasses < 0) DictExtraPasses = 0; if (DictExtraPasses > 3) DictExtraPasses = 3;
        if (AutoMinPt > AutoMaxPt) AutoMaxPt = AutoMinPt; // RF-524
        while (OpenProfile.Count < 4) OpenProfile.Add(new());
        if (OpenProfile.Count > 4) OpenProfile.RemoveRange(4, OpenProfile.Count - 4);
    }
}

public sealed class OpenProfileShortcut
{
    public string Keys { get; set; } = "";
    public string File { get; set; } = "";
}

public sealed class CustomPreset
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public List<string> Headers { get; set; } = [];
    public string ReqTemplate { get; set; } = "";
    public string ResTemplate { get; set; } = "";
    public bool FromFile { get; set; } = false;           // RF-303: só-leitura na UI
}
