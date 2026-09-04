// Parte IV — Parâmetros e calibragem.
// Valores marcados com 🔒 na especificação são reproduzidos exatamente.
// Não arredondar, não unificar, não recalibrar por intuição (Parte XII).
namespace Gort.Core;

/// <summary>Catálogo de parâmetros P-xx da Parte IV. Somente leitura.</summary>
public static class Params
{
    // IV.1 — Ciclo de vida e temporização
    public const double P01_SplashStaySec = 0.7;          // 🔒 FIXO
    public const double P02_SplashFadeSec = 2.0;          // FIXO
    public const int P03_LoopWaitMs = 3000;               // FIXO
    public const int P04_HookWaitMs = 250;                // 🔒 FIXO (~300ms mata o hook)
    public const int P05_Speed1Ms = 300;                  // 🔒 UI
    public const int P06_Speed2Ms = 1000;                 // 🔒 UI
    public const int P07_Speed3Ms = 1500;                 // 🔒 UI
    public const int P08_Speed4Ms = 2000;                 // 🔒 UI
    public const int P09_Speed5Ms = 2500;                 // 🔒 UI
    public const int P125_IdleSleepMs = 100;              // FIXO
    public const int P126_StopPollMs = 50;                // FIXO
    public const int P132_TaskCounterReset = 100000;      // FIXO

    // IV.2 — Áreas de captura
    // P-10: max(alfa,75)/255*0.15 — implementado em Regions/SelectionOverlay (Etapa 3).
    public const int P11_EdgeZoneBase = 31;               // 🔒 FIXO (= P-14+P-15+P-16, ×DPI)
    public const int P12_MinFrameSize = 50;               // FIXO (50x50)
    public const double P13_DragRecalcSec = 0.3;          // 🔒 FIXO
    public const int P14_BorderInner = 3;                 // 🔒 FIXO (px base ×DPI)
    public const int P15_BorderOuter = 8;                 // 🔒 FIXO (px base ×DPI)
    public const int P16_TitleBarH = 20;                  // 🔒 FIXO (px base ×DPI)
    public const int P144_CaptureAlign = 4;               // 🔒 FIXO
    public const double P140_ExclusionOpacity = 0.7;      // FIXO
    public const int P141_DpiReference = 96;              // FIXO
    public const int P145_MinSelectSize = 4;              // FIXO
    public const int P139_DropperZoomMin = 1;             // UI
    public const int P139_DropperZoomMax = 4;             // UI

    // IV.3 — Captura de imagem
    public const int P17_AttachedBuffer = 5;              // 🔒 FIXO
    public const int P18_AttachedWarmEvery = 10;          // 🔒 FIXO
    public const double P19_AttachedMaxAgeSec = 0.1;      // 🔒 FIXO
    public const int P20_CaptureRetryMs = 2;              // FIXO
    public const int P31_OsOcrWaitMs = 2;                 // FIXO

    // IV.4 — Pré-processamento
    public const int P21_Threshold = 127;                 // UI
    public const double P22_ZoomDefault = 2.0;            // 🔒 UI
    public const double P23_ZoomMin = 0.1;                // UI
    public const double P24_ZoomMax = 10.0;               // UI
    public const double P25_ZoomStep = 0.5;               // UI
    public const int P26_DarkS1a = 0, P26_DarkS1b = 8, P26_DarkV1a = 0, P26_DarkV1b = 32;    // 🔒
    public const int P27_DarkS2a = 95, P27_DarkS2b = 100, P27_DarkV2a = 0, P27_DarkV2b = 32; // 🔒
    public const int P28_LightS1a = 0, P28_LightS1b = 10, P28_LightV1a = 75, P28_LightV1b = 100; // 🔒

    // IV.5 — OCR
    public const int P29_CloudMonthlyLimit = 950;         // 🔒 UI
    public const int P30_ModernMaxLines = 1000;           // FIXO
    public const double P32_ModernVerticalRatio = 1.5;    // 🔒 FIXO

    // IV.6 — Estruturação e pós-processamento 🔒
    public const double P33_VerticalRatio = 1.5;          // 🔒 FIXO
    public const double P34_FontAdjacencyRatio = 1.3;     // 🔒 FIXO
    public const double P35_FlowGapFactor = 1.25;         // 🔒 FIXO
    public const double P36_TransverseOverlap = 0.25;     // 🔒 FIXO
    public const double P37_StartAlignFactor = 2.0;       // 🔒 FIXO
    public const int P38_FontWhenNone = 10;               // 🔒 FIXO
    public static readonly string[] P39_StrongBullets = ["•", "●", "○", "◦", "▪", "■", "‣", "⁃", "·", "・", "･"]; // 🔒
    public const int P40_ShortChars = 10;                 // 🔒 FIXO
    public const int P41_ShortCharsNoSpaces = 6;          // 🔒 FIXO
    public const int P42_VerticalDiscount = 3;            // 🔒 FIXO
    public const int P43_ShortWords = 3;                  // 🔒 FIXO
    public const double P148_TitleLengthRatio = 1.5;      // 🔒 FIXO (teto)
    public const double P44_BlockAppendRatio = 1.2;       // 🔒 FIXO
    public static readonly string[] P45_ClosingChars = ["\"", "'", "”", "’", "」", "』", "】", ")", "》"]; // 🔒
    public static readonly string[] P149_SentenceEnd = [".", "?", "!", "。", "？", "！"]; // 🔒
    public const int P150_NumberedMaxLen = 3;             // 🔒 FIXO
    public const int P46_DictExtraPasses = 0;             // UI (faixa 0–3)

    // IV.7 — Detecção de mudança e cache
    public const int P47_IdleRepaintMs = 1000;            // 🔒 FIXO
    public const int P48_MemoryMaxEntries = 10000;        // 🔒 FIXO
    public const int P49_DisplayMemoryCount = 5;          // UI (1–10)
    public const int P50_DisplayMemorySec = 10;           // UI (até 200)

    // IV.8 — Tradução
    public const string P51_DefaultToken = "//////";      // 🔒 REMOTO
    public const string P52_BrowserToken = "@@@@@@";      // 🔒 REMOTO
    public const bool P151_AdvancedToken = false;         // 🔒 REMOTO
    public const int P53_LowQualityHours = 1;             // FIXO
    public const int P54_WebTimeoutMs = 2000;             // FIXO
    public const int P55_MaxApiKeys = 20;                 // FIXO
    public const int P56_PostRequestJitterMs = 650;       // 🔒 FIXO (0–650)
    public const int P57_SheetMinRows = 50;               // FIXO
    public const string P58_BrowserSuffix = "^^^^";       // FIXO
    public const int P59_BrowserTimeoutSec = 5;           // FIXO
    public const int P60_BrowserTimeoutAltSec = 3;        // FIXO
    public const int P61_BrowserFirstExtraSec = 5;        // FIXO
    public const double P62_BrowserRepeatSec = 1.5;       // FIXO
    public const int P63_BeforeNavigateJitterMs = 140;    // FIXO (0–140)
    public const int P136_ClearFieldTries = 4;            // FIXO (a cada 50ms)
    public const int P137_ResultPollMs = 80;              // FIXO
    public const int P64_LlmTempDefault = 20;             // 🔒 UI (=0,20)
    public const int P65_LlmReasonDefault = 0;            // 🔒 UI (0–3)
    public const int P66_LlmMaxOutDefault = 4000;         // 🔒 UI
    public const int P67_LlmTempEco = 0;                  // 🔒 UI
    public const int P68_LlmReasonEco = 1;                // 🔒 UI
    public const int P69_LlmMaxOutEco = 2000;             // 🔒 UI
    public const int P70_TempMin = 0, P71_TempMax = 100;  // UI
    public const int P72_ReasonMin = 0, P73_ReasonMax = 3;// UI
    public const int P74_MaxOutMin = 500, P75_MaxOutMax = 10000; // UI
    public const int P152_MaxOutInitial = 1000;           // 🔒 UI
    public const int P76_ProReasonBudget = 512;           // 🔒 FIXO
    public const int P77_LlmTimeoutSec = 300;             // FIXO
    public const int P78_LocalCodePage = 932;             // FIXO
    public const int P135_PipeMaxMsg = 65535;             // FIXO
    public const int P138_PipeInitPollMs = 250;           // FIXO
    public const int P143_PipeReplyPollMs = 50;           // FIXO
    public const int P134_NativeAccumCap = 8192;          // FIXO

    // IV.9 — Janelas de tradução
    public const int P79_LayerIdleAlpha = 190;            // 🔒 FIXO
    public const int P80_OutlineOuter = 5;                // FIXO
    public const int P81_OutlineInner = 2;                // FIXO
    public const int P82_BgPadLeft = 8;                   // FIXO
    public const int P83_BgPadTop = 4;                    // FIXO
    public const int P84_BgPadWidth = 16;                 // FIXO
    public const int P85_BgPadHeight = 8;                 // FIXO
    public const int P86_LayerMargin = 15;                // FIXO
    public const int P87_LayerMinW = 200;                 // FIXO
    public const int P88_LayerMinH = 100;                 // FIXO
    public const int P89_ResizeZone = 30;                 // FIXO
    public const int P133_LayerDefaultX = 20;             // 🔒 (y = altura−300) 973x192
    public const int P133_LayerDefaultW = 973, P133_LayerDefaultH = 192, P133_LayerDefaultYOffset = 300;
    public const int P90_OverlapWarnSec = 10;             // FIXO
    public const int P91_ScreenshotMs = 5000;             // 🔒 FIXO
    public const double P92_OverlaySlack = 1.3;           // FIXO
    public const int P93_ContentShrink = 4;               // FIXO
    public const int P154_ContentShrinkInitial = 4;       // FIXO
    public const double P94_LeaderRatio = 1.3;            // FIXO
    public const double P95_FontScale = 1.15;             // FIXO
    public const int P96_FontBisectIters = 9;             // FIXO
    public const double P97_FontBisectEps = 0.25;         // FIXO (pt)
    public const double P98_LineAdvance = 1.2;            // FIXO
    public const double P99_OutlineSlack = 2.5;           // FIXO
    public const double P100_BreakSlack = 1.2;            // FIXO (× fonte)
    public const int P131_SnapshotStaySec = 5;            // 🔒 UI (0+)
    public const double P129_AutoMinPt = 10, P129_CtrlMinPt = 5;  // UI
    public const double P130_AutoMaxPt = 50;              // UI (mínimo do controle: 5)
    public const double P127_DefaultPt = 15;              // 🔒 UI
    public const int P128_UiMinPt = 8;                    // UI
    public const string P163_DefaultFamily = "";          // UI ("" = fonte de interface do SO, RF-387)
    public static readonly byte[] P101_TextColor = [255, 255, 255];   // 🔒 UI
    public static readonly byte[] P102_Outline1 = [192, 192, 192];    // 🔒 UI
    public static readonly byte[] P103_Outline2 = [0, 0, 0];          // 🔒 UI
    public static readonly byte[] P104_BgColor = [170, 0, 0, 0];      // 🔒 UI (ARGB)

    // IV.10 — Análise automática de cor 🔒
    public const int P105_MaxSamplesBg = 65536;           // 🔒 FIXO
    public const int P106_MaxSamplesWord = 4096;          // 🔒 FIXO
    public const int P107_MinAlpha = 128;                 // FIXO
    public const double P108_EdgeRatio = 0.15;            // FIXO
    public const int P109_EdgeMax = 4;                    // FIXO
    public const int P110_MinProbes = 3;                  // FIXO
    public const double P111_GlobalSupport = 0.4;         // FIXO
    public const double P112_RingRatio = 0.2;             // FIXO
    public const int P113_RingMin = 1, P114_RingMax = 4;  // FIXO
    public const double P115_MinContrast = 2.5;           // FIXO
    public const int P158_QuantizeDropBits = 3;           // FIXO (32 níveis)
    public const double P160_LumR = 0.2126, P160_LumG = 0.7152, P160_LumB = 0.0722;  // FIXO
    public const double P161_LinearT = 0.04045, P161_LinearD = 12.92;
    public const double P161_GammaO = 1.055, P161_GammaB = 0.055, P161_GammaE = 2.4; // FIXO
    public const double P162_ContrastConst = 0.05;        // FIXO

    // IV.11 — Atualização, atalhos e auxiliares
    public const int P116_FailWaitMin = 10;               // 🔒 FIXO
    public const int P117_MoveTries = 10;                 // FIXO
    public const int P118_MoveIntervalMs = 500;           // FIXO
    public const int P119_OpenProfileShortcuts = 4;       // FIXO
    public const int P120_ForegroundChecks = 15;          // 🔒 FIXO
    public const int P121_ForegroundIntervalMs = 100;     // FIXO
    public const int P122_FollowTimerMs = 30;             // FIXO
    public const int P123_FollowRecalcMs = 100;           // FIXO
    public const int P124_FollowBlinkMs = 500;            // FIXO

    /// <summary>Resolve velocidade 1..5 → intervalo (RF-040→aplicar: P-05..P-09).</summary>
    public static int SpeedToInterval(int speed) => speed switch
    {
        1 => P05_Speed1Ms,
        2 => P06_Speed2Ms,
        3 => P07_Speed3Ms,
        4 => P08_Speed4Ms,
        _ => P09_Speed5Ms,
    };
}
