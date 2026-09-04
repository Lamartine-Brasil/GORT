using Xunit;

// Os testes mutam estado global (LangCodes.All, RemoteDefaults) —
// sem isso o xUnit roda classes em paralelo e gera flaky
// (ex.: HardeningTests adiciona "xx" enquanto TranslationTests lê).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
